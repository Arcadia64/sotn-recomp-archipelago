#nullable enable
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SotnArchipelago;

// Keeps the seeds this PC has connected to in files next to the game (archipelago-seeds\<id>.json, id = the
// seed fingerprint a linked save holds, SaveLink: 4 bytes in the save's castle flags), so a save of that
// seed can be loaded and played without a connection. Only the SeedsKept most recently played are kept.
// A file holds what the mod gets on connecting: slot data (placement, options, logic), what's at each
// location, names, checked locations and items received. Played offline, the game records checks as usual;
// they're sent, and new items received, the next time we connect.
static class SeedCache
{
    const int CheckEveryFrames = 60 * 5;
    const int SeedsKept = 10;   // the most recently played; older ones are deleted

    static (int Connection, int Checked, int Received, int Scouts, bool Goal) _saved = (-1, -1, -1, -1, false);

    static int _goalLookedOn = -1;

    static string Folder => Path.Combine(AppContext.BaseDirectory, "archipelago-seeds");
    static string FileFor(uint id) => Path.Combine(Folder, $"{id:X8}.json");

    // Writes the current seed when something in it changed (called every frame, checks every few seconds).
    public static void Tick(long frame)
    {
        if (frame % CheckEveryFrames != 0 || !ApClient.HasSeed || !ApClient.ScoutsComplete) return;
        string path = FileFor(SaveLink.ExpectedHash());
        // Dracula beaten while offline, and this connection came straight to the seed (no save loaded from
        // the cache yet): the file still has it; the server is told before the file is rewritten.
        if (_goalLookedOn != ApClient.ConnectionId)
        {
            _goalLookedOn = ApClient.ConnectionId;
            if (!ApClient.GoalReached && CachedGoal(path)) ApClient.ReachGoal();
        }
        var now = (ApClient.ConnectionId, ApClient.CheckedCount, ApClient.Received.Length, ApClient.ScoutCount, ApClient.GoalReached);
        if (now == _saved) return;
        var seed = ApClient.ExportSeed();
        if (seed == null) return;
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(path + ".tmp", seed.ToJsonString());
            File.Move(path + ".tmp", path, overwrite: true);
            Prune(path);
            _saved = now;
        }
        catch (Exception ex)
        {
            Log.Error($"couldn't save the seed for offline play: {ex.Message}");
            _saved = now; // don't retry every few seconds
        }
    }

    // Keeps the SeedsKept most recently played seeds (by file time: written while connected, touched when
    // loaded) and the one in use; deletes the rest and any half-written file.
    static void Prune(string current)
    {
        try
        {
            var files = new DirectoryInfo(Folder).GetFiles("*.json");
            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = SeedsKept; i < files.Length; i++)
                if (!string.Equals(files[i].FullName, Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase)) files[i].Delete();
            foreach (var stale in new DirectoryInfo(Folder).GetFiles("*.tmp")) stale.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"couldn't tidy the seed cache: {ex.Message}");
        }
    }

    public static bool Has(uint id) => id != 0 && File.Exists(FileFor(id));

    static bool CachedGoal(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject seed
                && seed["goal"] is JsonValue goal && goal.TryGetValue(out bool reached) && reached;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    static uint _failedFor;

    // Every frame: while a save linked to a seed is being played, keep that seed in use.
    public static void UseForSave(RecompOne.Runtime.Memory.IMemory m, long frame)
    {
        if (frame % 30 != 0 || m.ReadU8(Sotn.Game.GameStateAddr) != (byte)Sotn.GameState.Play) return;
        uint link = SaveLink.StoredHash(m);
        if (link != 0) SwitchTo(link);
    }

    // Makes the seed with this fingerprint the one in use if it isn't: from the cache, offline. Connected to a
    // server running another seed, that connection is dropped first (the save plays its own seed; nothing goes
    // to the other one). While still connecting, the cache is used meanwhile; the connection takes over if it's
    // the same seed. False if the seed isn't in the cache.
    public static bool SwitchTo(uint link)
    {
        bool connected = ApClient.State == ConnectionState.Connected;
        if (ApClient.HasSeed && link == SaveLink.ExpectedHash()) return true;
        if (!Has(link) || link == _failedFor) return false;
        if (connected)
        {
            ApClient.Disconnect();
            ApClient.ShowToast("Archipelago", "This save is from a different seed than the server's: playing it offline with its own seed. Connect to its server to sync.", 12f);
        }
        if (!Load(link))
        {
            _failedFor = link;
            return false;
        }
        if (!connected)
            ApClient.ShowToast("Archipelago", "Playing offline with this save's seed: checks are sent and items received once you connect.");
        return true;
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
            File.SetLastWriteTimeUtc(FileFor(id), DateTime.UtcNow); // played now: kept by Prune
            _saved = (ApClient.ConnectionId, ApClient.CheckedCount, ApClient.Received.Length, ApClient.ScoutCount, ApClient.GoalReached);
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
