#nullable enable
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace SotnArchipelago;

// A save always plays with its own seed; the game waits for it before starting. Without it, items are the
// vanilla ones, and spots collected then can't be made good later (their own items are never given). The
// file select screen waits at the step that starts the game (SEL_Update's step 0x12 in g_GameEngineStep: it
// clears the screen and hands over) until the seed is in:
//   - a new game (or a save never linked to a seed) needs the connection: the seed comes from the server;
//   - a save linked to a seed plays with that seed: connected to it, or from this PC's cache (SeedCache) —
//     offline, even if connected to a server running another seed (SeedCache.SwitchTo). Not cached and not
//     connected to it: it waits for a connection to its own seed.
static class ConnectGate
{
    const uint MenuStepAddr = 0x8003C9A4;   // g_GameEngineStep, the menu's step while in the SEL overlay
    const uint StartGameStep = 0x12;

    // Opens the Archipelago window (set by ArchipelagoMod).
    public static Action? ShowPanel;

    static bool _saveLoaded;   // the game about to start is a loaded save (not a new game)
    static string _told = "";

    // The file select screen applies a save's data to RAM before starting it; a new game doesn't.
    [PostHook("sel", "ApplySaveData_sel")]
    static void SaveApplied(CpuContext c, IMemory m) => _saveLoaded = true;

    [PreHook("sel", "UpdateNameEntry")]
    static void NewGame(CpuContext c, IMemory m) => _saveLoaded = false;

    [PreHook("sel", "SEL_Update")]
    static bool HoldUntilSeed(CpuContext c, IMemory m)
    {
        if (m.ReadU32(MenuStepAddr) != StartGameStep)
        {
            _told = "";
            return true;
        }

        uint link = _saveLoaded ? SaveLink.StoredHash(m) : 0;
        bool connected = ApClient.State == ConnectionState.Connected;
        bool haveSeed = link == 0 ? connected : SeedCache.SwitchTo(link);
        if (haveSeed && SeedPlan.Ready)
        {
            _saveLoaded = false;
            return true;
        }

        if (!haveSeed)
            Tell(link == 0
                ? "Connect to your Archipelago server to start: the game begins as soon as you're connected."
                : connected
                    ? "This save is from a different seed than the server's, and that seed isn't on this PC: connect to this save's server to play it."
                    : "This save's seed isn't on this PC yet: connect to its Archipelago server to play it.");
        return false;
    }

    static void Tell(string message)
    {
        if (message == _told) return;
        _told = message;
        Log.Info($"starting the game: {message}");
        ApClient.ShowToast("Archipelago", message, 10f);
        ShowPanel?.Invoke();
    }
}
