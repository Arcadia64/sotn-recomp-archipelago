#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SotnArchipelago;

// What the Messages window shows: the server's messages (PrintJSON) in coloured parts, as Archipelago's
// Text Client shows them, and the hints for this slot (the server keeps them in data storage,
// _read_hints_<team>_<slot>, and tells us when they change). Filled on the network thread, drawn on the UI's.
static class Messages
{
    public enum Kind { Chat, Hint, MyItem, OtherItem, Server, Command }

    // Colour 0 = the normal text colour. ImGui colours are 0xAABBGGRR.
    public readonly record struct Part(string Text, uint Colour);
    public sealed record Message(DateTime Time, Kind Kind, Part[] Parts);

    public sealed record Hint(int ReceivingPlayer, int FindingPlayer, long Location, long Item, int ItemFlags,
                              bool Found, string Entrance, int Status);

    const int MaxMessages = 500;

    // Archipelago's colours for message parts, and its hint statuses.
    public const uint Own = 0xFFEE00EE, OtherPlayer = 0xFFD2FAFA, LocationColour = 0xFF7FFF00, EntranceColour = 0xFFED9564;
    public const uint Red = 0xFF5A5AEE, Green = 0xFF5AD25A, Grey = 0xFF909090;
    public const int StatusFound = 40, StatusPriority = 30, StatusAvoid = 20, StatusNoPriority = 10;

    static readonly object _gate = new();
    static readonly List<Message> _messages = [];
    static Hint[] _hints = [];

    public static int Version { get; private set; }

    public static Message[] Snapshot()
    {
        lock (_gate) return _messages.ToArray();
    }

    public static Hint[] Hints
    {
        get { lock (_gate) return _hints; }
    }

    public static void Clear()
    {
        lock (_gate) { _messages.Clear(); Version++; }
    }

    static void Add(Kind kind, Part[] parts)
    {
        if (parts.Length == 0 || parts.All(p => p.Text.Length == 0)) return;
        lock (_gate)
        {
            _messages.Add(new Message(DateTime.Now, kind, parts));
            if (_messages.Count > MaxMessages) _messages.RemoveRange(0, _messages.Count - MaxMessages);
            Version++;
        }
    }

    // A line of our own (DeathLink, connection changes), in the server's category.
    public static void AddLocal(string text, uint colour = 0) => Add(Kind.Server, [new Part(text, colour)]);

    // ---- PrintJSON ----

    public static void AddPrint(JsonObject p)
    {
        string type = p["type"]?.GetValue<string>() ?? "";
        int slot = ApClient.Slot;
        int receiving = p["receiving"]?.GetValue<int>() ?? -1;
        int finding = p["item"]?["player"]?.GetValue<int>() ?? -1;
        bool mine = receiving == slot || finding == slot;
        var kind = type switch
        {
            "Chat" or "ServerChat" => Kind.Chat,
            "Hint" => Kind.Hint,
            "ItemSend" or "ItemCheat" => mine ? Kind.MyItem : Kind.OtherItem,
            "CommandResult" or "AdminCommandResult" => Kind.Command,
            _ => Kind.Server,
        };
        Add(kind, Parts(p["data"] as JsonArray));
    }

    static Part[] Parts(JsonArray? data)
    {
        if (data == null) return [];
        var parts = new List<Part>();
        foreach (var node in data)
        {
            if (node is not JsonObject part) continue;
            string text = part["text"]?.GetValue<string>() ?? "";
            string type = part["type"]?.GetValue<string>() ?? "text";
            int player = part["player"]?.GetValue<int>() ?? ApClient.Slot;
            int flags = part["flags"]?.GetValue<int>() ?? 0;
            parts.Add(type switch
            {
                "player_id" when int.TryParse(text, out int id) => PlayerPart(id),
                "player_name" => new Part(text, text == ApClient.SlotName ? Own : OtherPlayer),
                "item_id" when long.TryParse(text, out long item) => new Part(ApClient.ItemName(item, player), ItemColour(flags)),
                "item_name" => new Part(text, ItemColour(flags)),
                "location_id" when long.TryParse(text, out long loc) => new Part(ApClient.LocationName(loc, player), LocationColour),
                "location_name" => new Part(text, LocationColour),
                "entrance_name" => new Part(text, EntranceColour),
                "hint_status" => new Part(text, StatusColour(part["status"]?.GetValue<int>() ?? 0)),
                "color" => new Part(text, NamedColour(part["color"]?.GetValue<string>())),
                _ => new Part(text, 0),
            });
        }
        return parts.ToArray();
    }

    public static Part PlayerPart(int id) => new(ApClient.PlayerName(id), id == ApClient.Slot ? Own : OtherPlayer);

    // The same colours as the in-game badge and the log (ItemClass). Worked out here, not with ImGui: this
    // runs on the network thread.
    public static uint ItemColour(int flags)
    {
        var c = ItemClass.Colour(new NetworkItem(0, 0, 0, flags));
        static uint Byte(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        return Byte(c.W) << 24 | Byte(c.Z) << 16 | Byte(c.Y) << 8 | Byte(c.X);
    }

    public static uint StatusColour(int status) => status switch
    {
        StatusFound => Green,
        StatusPriority => 0xFFEF99AF,
        StatusAvoid => 0xFF7280FA,
        StatusNoPriority => 0xFFE88B6D,
        _ => 0,
    };

    public static string StatusName(int status) => status switch
    {
        StatusFound => "Found",
        StatusPriority => "Priority",
        StatusAvoid => "Avoid",
        StatusNoPriority => "No priority",
        _ => "Unspecified",
    };

    static uint NamedColour(string? name) => name switch
    {
        "red" => Red,
        "green" => Green,
        "yellow" => 0xFF5AE6E6,
        "blue" => 0xFFE6825A,
        "magenta" => Own,
        "cyan" => 0xFFE6E65A,
        "white" => 0xFFFFFFFF,
        "black" => Grey,
        _ => 0,
    };

    // ---- hints ----

    public static string HintsKey(int team, int slot) => $"_read_hints_{team}_{slot}";

    static JsonNode? _hintsJson;

    // As the server sent them, for the seed cache.
    public static JsonNode? HintsJson()
    {
        lock (_gate) return _hintsJson?.DeepClone();
    }

    public static void SetHints(JsonNode? value)
    {
        var hints = new List<Hint>();
        if (value is JsonArray list)
            foreach (var node in list)
            {
                if (node is not JsonObject h) continue;
                bool found = h["found"]?.GetValue<bool>() ?? false;
                hints.Add(new Hint(
                    h["receiving_player"]?.GetValue<int>() ?? 0,
                    h["finding_player"]?.GetValue<int>() ?? 0,
                    h["location"]?.GetValue<long>() ?? 0,
                    h["item"]?.GetValue<long>() ?? 0,
                    h["item_flags"]?.GetValue<int>() ?? 0,
                    found,
                    h["entrance"]?.GetValue<string>() ?? "",
                    h["status"]?.GetValue<int>() ?? (found ? StatusFound : 0)));
            }
        lock (_gate)
        {
            _hints = hints.ToArray();
            _hintsJson = value?.DeepClone();
            Version++;
        }
    }
}
