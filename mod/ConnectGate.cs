#nullable enable
using System;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// The game needs the seed before it starts: without it, items are the vanilla ones, and spots collected then
// can't always be made good later. The file select screen waits at the step that starts the game (SEL_Update's
// step 0x12 in g_GameEngineStep: it clears the screen and hands over) until the seed is in:
//   - a new game needs the connection (the seed comes from the server);
//   - a loaded save linked to a seed this PC has played (SeedCache) starts right away, offline if not
//     connected; checks are sent and items received once connected.
static class ConnectGate
{
    const uint MenuStepAddr = 0x8003C9A4;   // g_GameEngineStep, the menu's step while in the SEL overlay
    const uint StartGameStep = 0x12;

    // Opens the Archipelago window (set by ArchipelagoMod).
    public static Action? ShowPanel;

    static bool _saveLoaded;   // the game about to start is a loaded save (not a new game)
    static bool _told;
    static uint _triedCache;

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
            _told = false;
            _triedCache = 0;
            return true;
        }

        bool connected = ApClient.State == ConnectionState.Connected;
        uint link = _saveLoaded ? SaveLink.StoredHash(m) : 0;
        if (link != 0 && !connected && (!ApClient.Offline || link != SaveLink.ExpectedHash()) && _triedCache != link)
        {
            _triedCache = link;
            SeedCache.Load(link);
        }

        // Connected, any save starts (one from another seed plays without the seed, with a warning: SeedPlan.ActiveFor).
        bool ready = SeedPlan.Ready && (connected || (ApClient.Offline && link != 0 && link == SaveLink.ExpectedHash()));
        if (ready)
        {
            _saveLoaded = false;
            return true;
        }

        if (!_told)
        {
            _told = true;
            string why = link == 0
                ? "Connect to your Archipelago server to start a new game: it begins as soon as you're connected."
                : SeedCache.Has(link)
                    ? "Loading this save's seed..."
                    : "This save's seed isn't on this PC yet: connect to your Archipelago server to play it.";
            Log.Info($"waiting to start the game: {why}");
            ApClient.ShowToast("Archipelago", why + " (To play without Archipelago, turn the mod off.)");
            if (!SeedCache.Has(link)) ShowPanel?.Invoke();
        }
        return false;
    }
}
