#nullable enable
using System;
using System.Linq;
using System.Reflection;
using RecompOne.Runtime.Memory;

namespace SotnArchipelago;

// The recomp's own randomizer, and a flag of it the seed relies on.
static class RecompRando
{
    // RandoPatch's preset byte (sotn.io presets 1-12, Integrated = 13 for the recomp's Randomizer panel, also
    // set when a save it stamped is loaded). Not zero, Randomizer.ApplyInternalRandomizer rewrites enemy drops,
    // item tables, the shop and relics on every stage load, over the seed. While a seed applies it stays off.
    const uint PresetAddr = 0x8000C000;

    // A start in the inverted castle (starting_zone): Rom.py adds a routine at 0x800DB9B8 that makes Leap stone
    // and Gravity boots count as owned there until the first castle is visited. The recomp does the same in
    // RandoPatch.OverrideIsRelicActive, but only while its byte 0x8000C003 is set, which happens once, when the
    // Entrance trapdoor sends you there, and is never saved: after a restart the relics would be gone. Keep it
    // set while the seed has that routine.
    const uint SecondCastleStartAddr = 0x8000C003;
    const uint StartRoutineAddr = 0x800DB9B8;
    const uint StartRoutineWord0 = 0x3C028009, StartRoutineWord1 = 0x8C4274A0;

    static bool _setSecondCastleStart;

    // Every frame, and before each stage load's placement.
    public static void Guard(IMemory m)
    {
        if (!SeedPlan.ActiveFor(m))
        {
            if (_setSecondCastleStart && m.ReadU8(SecondCastleStartAddr) != 0) m.WriteU8(SecondCastleStartAddr, 0);
            _setSecondCastleStart = false;
            return;
        }

        if (m.ReadU8(PresetAddr) != 0)
        {
            m.WriteU8(PresetAddr, 0);
            ClearSaveStamp();
            Log.Info("the recomp's own randomizer was switched off: this game uses the Archipelago seed");
            ApClient.ShowToast("Archipelago", "The recomp's own Randomizer is off while playing an Archipelago seed (it would replace the seed's items).", 10f);
        }

        bool startRoutine = m.ReadU32(StartRoutineAddr) == StartRoutineWord0 && m.ReadU32(StartRoutineAddr + 4) == StartRoutineWord1;
        if (startRoutine && m.ReadU8(SecondCastleStartAddr) == 0)
        {
            m.WriteU8(SecondCastleStartAddr, 1);
            _setSecondCastleStart = true;
        }
    }

    // Recompiled.SaveLoadManager.Clear(): forget the randomizer settings of a stamped save, so they aren't
    // applied again or stamped into new saves.
    static void ClearSaveStamp()
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Recompiled.SaveLoadManager")).FirstOrDefault(t => t != null);
            type?.GetMethod("Clear", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)?.Invoke(null, null);
        }
        catch (Exception ex)
        {
            Log.Error($"couldn't clear the recomp randomizer's save stamp: {ex.Message}");
        }
    }
}
