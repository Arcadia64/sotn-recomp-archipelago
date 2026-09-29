#nullable enable
using System;
using System.Reflection;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// The AP world's always-on fixes (Rom.py apply_acessibility_patches, the Maria dialog skip and
// "Clear Game status"), for play on a seed. Most have native equivalents in the recomp behind its
// BugFixes setting (patches/qol/FunctionFixes.cs, MariaAlchemyCutsceneFix), which the recomp also
// switches on for its own randomizer; the rest are done here. Power of Sire flashing is left to the
// player's own "remove flashing" setting.
static class Fixes
{
    const uint ClockRoomCutsceneAddr = 0x800A2984; // DRA data; Rom.py: "Patch Clock Room cutscene"
    const byte ClockRoomCutsceneValue = 0x40;
    const uint GameClearFlagAddr = 0x8003BDE0;     // as the recomp's "Clear File" option sets it
    const byte GameClearValue = 0x02;

    const uint ForcedInputAddr = 0x80072EF4;       // input the Clock Room controller forces during the ring walk
    const uint RingWalkBlockerAddr = 0x80072EE8;   // cleared by the AP fix so Alucard can't get stuck
    const int MariaAfterHippogryphFlag = 0x62;     // castle flag: conversation already seen

    static bool OnSeed => RecompOne.Runtime.Runtime.Mem is { } m && SeedPlan.ActiveFor(m);

    // Recompiled.QualityOfLife is internal to the game assembly, so its BugFixes switch is set by
    // reflection. It is only forced on while connected; the player's own setting comes back after.
    static readonly FieldInfo? BugFixesField =
        Type.GetType("Recompiled.QualityOfLife, sotn")?.GetField("BugFixes", BindingFlags.Public | BindingFlags.Static);
    static bool? _playerBugFixes;
    static byte? _originalClockRoomCutscene;

    public static void Tick(IMemory m)
    {
        if (!OnSeed)
        {
            RestoreBugFixes();
            if (_originalClockRoomCutscene is { } original)
            {
                m.WriteU8(ClockRoomCutsceneAddr, original);
                _originalClockRoomCutscene = null;
            }
            return;
        }
        ForceBugFixes();
        // DRA table entry, 0x00 in vanilla; global game data, so put back on disconnect.
        _originalClockRoomCutscene ??= m.ReadU8(ClockRoomCutsceneAddr);
        if (m.ReadU8(ClockRoomCutsceneAddr) != ClockRoomCutsceneValue) m.WriteU8(ClockRoomCutsceneAddr, ClockRoomCutsceneValue);
        // Save data (the save links to this seed), as the AP patch leaves it: not reverted.
        if (m.ReadU8(GameClearFlagAddr) != GameClearValue) m.WriteU8(GameClearFlagAddr, GameClearValue);
    }

    static void ForceBugFixes()
    {
        if (BugFixesField == null) return;
        _playerBugFixes ??= (bool)BugFixesField.GetValue(null)!;
        BugFixesField.SetValue(null, true);
    }

    static void RestoreBugFixes()
    {
        if (BugFixesField == null || _playerBugFixes is not { } previous) return;
        BugFixesField.SetValue(null, previous);
        _playerBugFixes = null;
    }

    // Gold & Silver ring soft-lock (Rom.py): in the two steps where the Clock Room controller walks
    // Alucard into the passage (forced input 0x8040 / 0x2040), AP also clears 0x80072EE8.
    [PostHook("no0", "EntityClockRoomController_no0")]
    static void AfterClockRoomController(CpuContext c, IMemory m)
    {
        if (!OnSeed) return;
        uint forced = m.ReadU32(ForcedInputAddr);
        if (forced is 0x8040 or 0x2040) m.WriteU8(RingWalkBlockerAddr, 0);
    }

    // Maria's conversation after the Hippogryph can trap the player; AP always skips it by forcing the
    // "already seen" branch at 0x801A4FD4. Setting the flag that branch tests has the same effect.
    [PreHook("bo5", "func_801A4E40")]
    static void BeforeMariaAfterHippogryph(CpuContext c, IMemory m)
    {
        if (!OnSeed) return;
        uint flag = Progress.CastleFlagsAddr + MariaAfterHippogryphFlag;
        if (m.ReadU8(flag) == 0) m.WriteU8(flag, 1);
    }
}
