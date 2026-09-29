#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SotnArchipelago;

// Keeps each seed this PC has connected to in a file next to the game (archipelago-seeds\<id>.json, id =
// the seed fingerprint a linked save holds, SaveLink), so a save of that seed can be loaded and played
// without a connection. The file holds what the mod gets on connecting: slot data (placement, options,
// logic), what's at each location, names, checked locations and items received. Played offline, the
// game records checks as usual; they're sent, and new items received, the next time we connect.
static class SeedCache
{
    const int CheckEveryFrames = 60 * 5;

    static (int Connection, int Checked, int Received, int Scouts) _saved = (-1, -1, -1, -1);

    static string Folder => Path.Combine(AppContext.BaseDirectory, "archipelago-seeds");
    static string FileFor(uint id) => Path.Combine(Folder, $"{id:X8}.json");

    // Writes the current seed when something in it changed (called every frame, checks every few seconds).
    public static void Tick(long frame)
    {
        if (frame % CheckEveryFrames != 0 || !ApClient.HasSeed || !ApClient.ScoutsComplete) return;
        var now = (ApClient.ConnectionId, ApClient.CheckedCount, ApClient.Received.Length, ApClient.ScoutCount);
        if (now == _saved) return;
        var seed = ApClient.ExportSeed();
        if (seed == null) return;
        try
        {
            Directory.CreateDirectory(Folder);
            string path = FileFor(SaveLink.ExpectedHash());
            File.WriteAllText(path + ".tmp", seed.ToJsonString());
            File.Move(path + ".tmp", path, overwrite: true);
            _saved = now;
        }
        catch (Exception ex)
        {
            Log.Error($"couldn't save the seed for offline play: {ex.Message}");
            _saved = now; // don't retry every few seconds
        }
    }

    public static bool Has(uint id) => id != 0 && File.Exists(FileFor(id));

    static uint _triedFor;

    // Not connected (and not trying to) while playing a save whose seed isn't the one in use: use that
    // seed's cache, if this PC has it. E.g. after disconnecting from a server running another seed.
    public static void UseForSave(RecompOne.Runtime.Memory.IMemory m)
    {
        if (ApClient.State != ConnectionState.Disconnected) return;
        if (m.ReadU8(Sotn.Game.GameStateAddr) != (byte)Sotn.GameState.Play) return;
        uint link = SaveLink.StoredHash(m);
        if (link == 0 || (ApClient.Offline && link == SaveLink.ExpectedHash())) return;
        if (link == _triedFor) return;
        _triedFor = link;
        if (Load(link)) ApClient.ShowToast("Archipelago", "Playing offline with this save's seed: checks are sent and items received once you connect.");
    }

    // Plays the cached seed with this fingerprint, if there is one.
    public static bool Load(uint id)
    {
        if (!Has(id)) return false;
        try
        {
            var seed = JsonNode.Parse(File.ReadAllText(FileFor(id))) as JsonObject;
            if (seed == null || seed["version"]?.GetValue<int>() != 1) return false;
            if (!ApClient.ImportSeed(seed)) return false;
            _saved = (ApClient.ConnectionId, ApClient.CheckedCount, ApClient.Received.Length, ApClient.ScoutCount);
            Log.Info($"playing offline with seed {ApClient.SeedName}, slot {ApClient.Slot} ({ApClient.SlotName}) from this PC");
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error($"couldn't read the cached seed {id:X8}: {ex.Message}");
            return false;
        }
    }
}
