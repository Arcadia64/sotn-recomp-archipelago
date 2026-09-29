#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SotnArchipelago;

public enum ConnectionState { Disconnected, Connecting, Connected }

// Archipelago network protocol: https://github.com/ArchipelagoMW/Archipelago/blob/main/docs/network%20protocol.md
// The socket runs on a background task. Anything the game has to act on is queued
// and drained on the game thread (see Drain*), so game RAM is never touched from here.
public static class ApClient
{
    // This project's AP world (apworld/sotn_recomp). fdelduque's world for BizHawk is "Symphony of the Night".
    public const string GameName = "Symphony of the Night (Recomp)";

    // Remote items and starting inventory come from the server; our own items are placed in the game.
    const int ItemsHandling = 0b101;

    static readonly object _gate = new();
    static WebSocketTransport? _socket;
    static CancellationTokenSource? _cts;

    static string _slotName = "";
    static string _password = "";
    static string _uuid = "";
    static volatile bool _refused;
    static readonly int[] RetrySeconds = [2, 5, 10, 20, 30];

    // Everything below is guarded by _gate.
    static ConnectionState _state = ConnectionState.Disconnected;
    static string _status = "Not connected";
    static string _seedName = "";
    static int _slot = -1;
    static int _team = -1;
    static JsonObject? _slotData;
    static readonly Dictionary<int, (string Name, string Alias, string Game)> _players = [];
    static readonly Dictionary<string, Dictionary<long, string>> _itemNames = [];
    static readonly Dictionary<string, Dictionary<long, string>> _locationNames = [];
    static readonly HashSet<long> _checked = [];
    static readonly HashSet<long> _missing = [];
    static readonly Dictionary<long, NetworkItem> _scouts = [];
    static readonly List<NetworkItem> _received = [];
    static readonly HashSet<string> _tags = [];

    static int _connectionId;

    static readonly ConcurrentQueue<(string Title, string Message)> _toasts = new();
    static readonly ConcurrentQueue<string> _deaths = new();

    public static ConnectionState State { get { lock (_gate) return _state; } }
    public static string Status { get { lock (_gate) return _status; } }
    public static string SeedName { get { lock (_gate) return _seedName; } }
    public static int Slot { get { lock (_gate) return _slot; } }
    public static string SlotName => _slotName;
    public static JsonObject? SlotData { get { lock (_gate) return _slotData; } }
    public static int CheckedCount { get { lock (_gate) return _checked.Count; } }
    public static int LocationCount { get { lock (_gate) return _checked.Count + _missing.Count; } }
    public static int ScoutCount { get { lock (_gate) return _scouts.Count; } }

    // Changes on every successful connect, so per-seed state can tell it's stale.
    public static int ConnectionId { get { lock (_gate) return _connectionId; } }

    public static bool ScoutsComplete
    {
        get { lock (_gate) return _slot >= 0 && _scouts.Count > 0 && _scouts.Count >= _checked.Count + _missing.Count; }
    }

    public static NetworkItem[] Received { get { lock (_gate) return _received.ToArray(); } }

    public static bool TryGetScout(long location, out NetworkItem item)
    {
        lock (_gate) return _scouts.TryGetValue(location, out item);
    }

    public static bool IsChecked(long location)
    {
        lock (_gate) return _checked.Contains(location);
    }

    public static bool IsMissing(long location)
    {
        lock (_gate) return _missing.Contains(location);
    }

    // ---- connecting ----

    public static void Connect(string server, string slotName, string password)
    {
        Disconnect();
        _slotName = slotName.Trim();
        _password = password;
        _uuid = ClientUuid();
        _refused = false;
        var cts = new CancellationTokenSource();
        _cts = cts;
        SetState(ConnectionState.Connecting, $"Connecting to {server}...");
        _ = Task.Run(() => KeepConnectedAsync(server.Trim(), cts.Token));
    }

    // Reconnects after a dropped connection, backing off, until Disconnect or the server refuses us.
    static async Task KeepConnectedAsync(string server, CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            bool wasConnected = await RunAsync(server, ct);
            if (ct.IsCancellationRequested || _refused) return;
            failures = wasConnected ? 0 : failures + 1;
            int wait = RetrySeconds[System.Math.Min(failures, RetrySeconds.Length - 1)];
            string why;
            lock (_gate) why = _status;
            SetState(ConnectionState.Connecting, $"{why}. Retrying in {wait}s...");
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    public static void Disconnect()
    {
        var cts = _cts;
        var socket = _socket;
        _cts = null;
        _socket = null;
        cts?.Cancel();
        if (socket != null) _ = socket.CloseAsync().ContinueWith(_ => socket.Dispose());
        lock (_gate)
        {
            _slot = -1;
            _slotData = null;
            _checked.Clear();
            _missing.Clear();
            _scouts.Clear();
            _received.Clear();
            _tags.Clear();
        }
        ItemGiver.ClearDirect();
        SetState(ConnectionState.Disconnected, "Not connected");
    }

    // One connection attempt and session. True if the server accepted us before it ended.
    static async Task<bool> RunAsync(string server, CancellationToken ct)
    {
        int connectedBefore = ConnectionId;
        WebSocketTransport? socket = null;
        Exception? lastError = null;
        foreach (var uri in CandidateUris(server))
        {
            try
            {
                socket = await WebSocketTransport.ConnectAsync(uri, ct);
                Log.Info($"socket open: {uri}");
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                lastError = ex;
                Log.Info($"could not open {uri}: {ex.Message}");
            }
        }

        if (socket == null)
        {
            if (!ct.IsCancellationRequested)
                SetState(ConnectionState.Disconnected, $"Could not connect: {lastError?.Message}");
            return false;
        }

        _socket = socket;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var text = await socket.ReceiveTextAsync(ct);
                if (text == null) break;
                HandleMessage(text);
            }
            if (!ct.IsCancellationRequested) SetState(ConnectionState.Disconnected, "Server closed the connection");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error($"connection lost: {ex.Message}");
            if (!ct.IsCancellationRequested) SetState(ConnectionState.Disconnected, $"Connection lost: {ex.Message}");
        }
        finally
        {
            if (_socket == socket) _socket = null;
            socket.Dispose();
        }
        return ConnectionId != connectedBefore;
    }

    // Same order as the official client: secure first when no scheme is given.
    static IEnumerable<Uri> CandidateUris(string server)
    {
        if (server.Contains("://"))
        {
            yield return new Uri(server);
            yield break;
        }
        if (!server.Contains(':')) server += ":38281";
        yield return new Uri("wss://" + server);
        yield return new Uri("ws://" + server);
    }

    // ---- receiving ----

    static void HandleMessage(string text)
    {
        JsonArray packets;
        try
        {
            packets = JsonNode.Parse(text)!.AsArray();
        }
        catch (Exception ex)
        {
            Log.Error($"bad message from server: {ex.Message}");
            return;
        }

        foreach (var node in packets)
        {
            if (node is not JsonObject packet) continue;
            string cmd = packet["cmd"]?.GetValue<string>() ?? "";
            try
            {
                switch (cmd)
                {
                    case "RoomInfo": OnRoomInfo(packet); break;
                    case "DataPackage": OnDataPackage(packet); break;
                    case "Connected": OnConnected(packet); break;
                    case "ConnectionRefused": OnConnectionRefused(packet); break;
                    case "ReceivedItems": OnReceivedItems(packet); break;
                    case "LocationInfo": OnLocationInfo(packet); break;
                    case "RoomUpdate": OnRoomUpdate(packet); break;
                    case "PrintJSON": OnPrintJson(packet); break;
                    case "Bounced": OnBounced(packet); break;
                    case "InvalidPacket": Log.Error($"server rejected a packet: {packet["text"]}"); break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"handling {cmd}: {ex.Message}");
            }
        }
    }

    static void OnRoomInfo(JsonObject p)
    {
        var games = p["games"]?.AsArray().Select(g => g!.GetValue<string>()).Distinct().ToList() ?? [];
        lock (_gate) _seedName = p["seed_name"]?.GetValue<string>() ?? "";
        Log.Info($"room info: seed {SeedName}, games: {string.Join(", ", games)}");

        // Names for every game in the room, so messages can say what was sent and to whom.
        var connect = new JsonObject
        {
            ["cmd"] = "Connect",
            ["password"] = _password,
            ["game"] = GameName,
            ["name"] = _slotName,
            ["uuid"] = _uuid,
            ["version"] = new JsonObject { ["major"] = 0, ["minor"] = 6, ["build"] = 2, ["class"] = "Version" },
            ["items_handling"] = ItemsHandling,
            ["tags"] = new JsonArray(),
            ["slot_data"] = true,
        };
        var request = new JsonObject
        {
            ["cmd"] = "GetDataPackage",
            ["games"] = new JsonArray(games.Select(g => (JsonNode)JsonValue.Create(g)!).ToArray()),
        };
        Send(request, connect);
    }

    static void OnDataPackage(JsonObject p)
    {
        var games = p["data"]?["games"]?.AsObject();
        if (games == null) return;
        lock (_gate)
        {
            foreach (var (game, data) in games)
            {
                _itemNames[game] = Invert(data?["item_name_to_id"]?.AsObject());
                _locationNames[game] = Invert(data?["location_name_to_id"]?.AsObject());
            }
        }
        Log.Info($"names received for {games.Count} game(s)");
    }

    static Dictionary<long, string> Invert(JsonObject? nameToId)
    {
        var map = new Dictionary<long, string>();
        if (nameToId == null) return map;
        foreach (var (name, id) in nameToId)
            if (id != null) map[id.GetValue<long>()] = name;
        return map;
    }

    static void OnConnected(JsonObject p)
    {
        List<long> all;
        lock (_gate)
        {
            _connectionId++;
            _scouts.Clear();
            _slot = p["slot"]!.GetValue<int>();
            _team = p["team"]!.GetValue<int>();
            _slotData = p["slot_data"]?.AsObject();
            ReadPlayers(p);
            _checked.Clear();
            _missing.Clear();
            foreach (var id in Ids(p["checked_locations"])) _checked.Add(id);
            foreach (var id in Ids(p["missing_locations"])) _missing.Add(id);
            all = _checked.Concat(_missing).ToList();
        }

        CheckTracker.Reset();
        SetState(ConnectionState.Connected, $"Connected as {_slotName} (slot {Slot})");
        Log.Info($"connected: slot {Slot}, {CheckedCount}/{LocationCount} locations checked");
        _toasts.Enqueue(("Archipelago", $"Connected as {_slotName}"));

        // What is at each of our locations: which item, whose it is, and how important.
        Send(new JsonObject
        {
            ["cmd"] = "LocationScouts",
            ["locations"] = new JsonArray(all.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
            ["create_as_hint"] = 0,
        });

        if (OptionInt("death_link") > 0) SetTag("DeathLink", true);
    }

    static void ReadPlayers(JsonObject p)
    {
        var games = new Dictionary<int, string>();
        if (p["slot_info"] is JsonObject info)
            foreach (var (slot, data) in info)
                games[int.Parse(slot)] = data?["game"]?.GetValue<string>() ?? "";

        if (p["players"] is JsonArray players)
        {
            foreach (var player in players)
            {
                if (player is not JsonObject pl) continue;
                if (pl["team"]?.GetValue<int>() != _team) continue;
                int slot = pl["slot"]!.GetValue<int>();
                _players[slot] = (
                    pl["name"]?.GetValue<string>() ?? $"Player {slot}",
                    pl["alias"]?.GetValue<string>() ?? $"Player {slot}",
                    games.TryGetValue(slot, out var g) ? g : _players.GetValueOrDefault(slot).Game ?? "");
            }
        }
    }

    static void OnConnectionRefused(JsonObject p)
    {
        var errors = p["errors"]?.AsArray().Select(e => e?.GetValue<string>()).ToList() ?? [];
        var reason = errors.Count > 0 ? string.Join(", ", errors) : "unknown reason";
        Log.Error($"connection refused: {reason}");
        if (errors.Contains("InvalidGame"))
        {
            Log.Error($"that slot isn't a \"{GameName}\" slot: if it's Symphony of the Night, it was generated with fdelduque's world (for BizHawk); generate with sotn_recomp.apworld");
            ShowToast("Archipelago", "That slot isn't for this game. Was it generated with sotn_recomp.apworld?");
        }
        _refused = true;
        _cts?.Cancel();
        SetState(ConnectionState.Disconnected, $"Refused: {reason}");
    }

    static void OnReceivedItems(JsonObject p)
    {
        int index = p["index"]!.GetValue<int>();
        var items = Items(p["items"]).ToList();
        bool resync = false;
        lock (_gate)
        {
            if (index == 0) _received.Clear();
            if (index == _received.Count) _received.AddRange(items);
            else resync = true;
        }

        if (resync)
        {
            Log.Info($"received-item index {index} out of order, asking server to resend");
            Send(new JsonObject { ["cmd"] = "Sync" });
            return;
        }

        foreach (var item in items)
            Log.Info($"received {ItemName(item.Item, Slot)} from {PlayerName(item.Player)} ({LocationName(item.Location, item.Player)})");
    }

    static void OnLocationInfo(JsonObject p)
    {
        int count = 0;
        lock (_gate)
        {
            foreach (var item in Items(p["locations"]))
            {
                _scouts[item.Location] = item;
                count++;
            }
        }
        Log.Info($"scouted {count} location(s)");
    }

    static void OnRoomUpdate(JsonObject p)
    {
        lock (_gate)
        {
            foreach (var id in Ids(p["checked_locations"]))
            {
                _checked.Add(id);
                _missing.Remove(id);
            }
            if (p.ContainsKey("players")) ReadPlayers(p);
        }
    }

    static void OnPrintJson(JsonObject p)
    {
        var text = RenderText(p["data"] as JsonArray);
        if (text.Length == 0) return;

        // Pop up what we found for other players; the game shows our own finds, and received
        // items get their own message when they're given (ItemGiver). The log has everything.
        string type = p["type"]?.GetValue<string>() ?? "";
        int receiving = p["receiving"]?.GetValue<int>() ?? -1;
        var itemNode = p["item"] as JsonObject;
        if (type == "ItemSend" && itemNode != null)
        {
            var item = Items(new JsonArray(itemNode.DeepClone())).First();
            Log.Item(text, item);
            if (item.Player == Slot && receiving != Slot)
                _toasts.Enqueue(($"Sent ({ItemClass.Name(item)})", $"{ItemName(item.Item, receiving)} to {PlayerName(receiving)}"));
            return;
        }
        Log.Info(text);
    }

    static void OnBounced(JsonObject p)
    {
        var tags = p["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList() ?? [];
        if (!tags.Contains("DeathLink")) return;
        var data = p["data"];
        string source = data?["source"]?.GetValue<string>() ?? "someone";
        if (source == _slotName) return;
        string cause = data?["cause"]?.GetValue<string>() ?? $"{source} died";
        Log.Info($"DeathLink: {cause}");
        _deaths.Enqueue(cause);
    }

    // ---- sending ----

    public static void SendChecks(IEnumerable<long> locations)
    {
        var list = new List<long>();
        lock (_gate)
        {
            foreach (var id in locations)
                if (_checked.Add(id))
                {
                    _missing.Remove(id);
                    list.Add(id);
                }
        }
        if (list.Count == 0) return;
        Send(new JsonObject
        {
            ["cmd"] = "LocationChecks",
            ["locations"] = new JsonArray(list.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
        });
    }

    public static void SendGoal() => Send(new JsonObject { ["cmd"] = "StatusUpdate", ["status"] = 30 });

    public static void SendDeath(string cause)
    {
        Send(new JsonObject
        {
            ["cmd"] = "Bounce",
            ["tags"] = new JsonArray("DeathLink"),
            ["data"] = new JsonObject
            {
                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                ["source"] = _slotName,
                ["cause"] = cause,
            },
        });
    }

    public static void SetDataStorage(string key, JsonNode value)
    {
        Send(new JsonObject
        {
            ["cmd"] = "Set",
            ["key"] = key,
            ["default"] = value.DeepClone(),
            ["want_reply"] = false,
            ["operations"] = new JsonArray(new JsonObject { ["operation"] = "replace", ["value"] = value }),
        });
    }

    public static void Say(string text) => Send(new JsonObject { ["cmd"] = "Say", ["text"] = text });

    static void SetTag(string tag, bool on)
    {
        string[] tags;
        lock (_gate)
        {
            if (on) _tags.Add(tag); else _tags.Remove(tag);
            tags = _tags.ToArray();
        }
        Send(new JsonObject
        {
            ["cmd"] = "ConnectUpdate",
            ["tags"] = new JsonArray(tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()),
        });
    }

    static void Send(params JsonObject[] packets)
    {
        var socket = _socket;
        var ct = _cts?.Token ?? CancellationToken.None;
        if (socket == null) return;
        var text = new JsonArray(packets.Select(p => (JsonNode)p).ToArray()).ToJsonString();
        _ = Task.Run(async () =>
        {
            try { await socket.SendTextAsync(text, ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log.Error($"send failed: {ex.Message}"); }
        });
    }

    // ---- game thread ----

    public static bool TryDequeueToast(out (string Title, string Message) toast) => _toasts.TryDequeue(out toast);

    public static void ShowToast(string title, string message) => _toasts.Enqueue((title, message));

    public static bool TryDequeueDeath(out string cause) => _deaths.TryDequeue(out cause!);

    // ---- names ----

    public static string PlayerName(int slot)
    {
        lock (_gate)
        {
            if (slot == 0) return "Server";
            return _players.TryGetValue(slot, out var p) ? p.Alias : $"Player {slot}";
        }
    }

    public static string ItemName(long item, int ownerSlot)
    {
        lock (_gate) return LookUp(_itemNames, item, ownerSlot, "Item");
    }

    public static string LocationName(long location, int ownerSlot)
    {
        lock (_gate) return LookUp(_locationNames, location, ownerSlot, "Location");
    }

    static string LookUp(Dictionary<string, Dictionary<long, string>> names, long id, int slot, string fallback)
    {
        var game = _players.TryGetValue(slot, out var p) ? p.Game : GameName;
        if (names.TryGetValue(game, out var map) && map.TryGetValue(id, out var name)) return name;
        return $"{fallback} {id}";
    }

    static string RenderText(JsonArray? parts)
    {
        if (parts == null) return "";
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            string text = part?["text"]?.GetValue<string>() ?? "";
            string type = part?["type"]?.GetValue<string>() ?? "text";
            int player = part?["player"]?.GetValue<int>() ?? Slot;
            switch (type)
            {
                case "player_id": sb.Append(PlayerName(int.Parse(text))); break;
                case "item_id": sb.Append(ItemName(long.Parse(text), player)); break;
                case "location_id": sb.Append(LocationName(long.Parse(text), player)); break;
                default: sb.Append(text); break;
            }
        }
        return sb.ToString();
    }

    // ---- helpers ----

    // Option values in slot_data are numbers; read them without throwing on odd types.
    public static int OptionInt(string name, int fallback = 0)
    {
        var node = SlotData?[name];
        if (node is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
            if (v.TryGetValue<double>(out var d)) return (int)d;
        }
        return fallback;
    }

    static IEnumerable<long> Ids(JsonNode? node) =>
        node is JsonArray a ? a.Where(n => n != null).Select(n => n!.GetValue<long>()) : [];

    static IEnumerable<NetworkItem> Items(JsonNode? node)
    {
        if (node is not JsonArray a) yield break;
        foreach (var n in a)
        {
            if (n is not JsonObject o) continue;
            yield return new NetworkItem(
                o["item"]!.GetValue<long>(),
                o["location"]!.GetValue<long>(),
                o["player"]!.GetValue<int>(),
                o["flags"]?.GetValue<int>() ?? 0);
        }
    }

    static void SetState(ConnectionState state, string status)
    {
        lock (_gate)
        {
            _state = state;
            _status = status;
        }
    }

    static string ClientUuid()
    {
        var view = RecompOne.Runtime.Runtime.View;
        var id = view.GetString("Archipelago.Uuid");
        if (id.Length == 0)
        {
            id = Guid.NewGuid().ToString("N");
            view.SetString("Archipelago.Uuid", id);
            RecompOne.Runtime.Runtime.SaveView();
        }
        return id;
    }
}
