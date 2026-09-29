#nullable enable
using System;
using System.Linq;
using System.Reflection;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// With extended widescreen on, the recomp runs its own copy of HitDetection (WidescreenPatch), and that copy
// has the prize table (what an enemy drops when it drops neither of its items: hearts, gold, ...) built in,
// as in the vanilla game. randomize_drop's "global" options change each stage's table instead, so without
// this those drops would stay vanilla in extended widescreen. Keep the built-in copy equal to the current
// stage's table (addresses: OptionData.PrizeTables, found by the generator).
static class PrizeTableSync
{
    const int Entries = 32;
    static ushort[]? _table;
    static ushort[]? _vanilla;
    static bool _looked;

    public static void Tick(IMemory m)
    {
        if (!SeedPlan.ActiveFor(m))
        {
            // No seed (or another seed's save): the built-in copy goes back to what it was.
            if (_vanilla != null && Table() is { } built && !built.AsSpan().SequenceEqual(_vanilla)) _vanilla.CopyTo(built, 0);
            return;
        }
        if (m.ReadU8(Game.GameStateAddr) != (byte)GameState.Play || m.ReadU8(ItemGiver.EngineStepAddr) != ItemGiver.EngineNormal) return;
        if (!OptionData.PrizeTables.TryGetValue(m.ReadU8(Game.StageIdAddr), out uint at)) return;
        var table = Table();
        if (table == null) return;
        for (int i = 0; i < Entries; i++)
        {
            ushort value = m.ReadU16(at + (uint)i * 2);
            if (table[i] != value) table[i] = value;
        }
    }

    // Recompiled.WidescreenPatch.TestCollPrizeTable (static readonly array: its contents can change).
    static ushort[]? Table()
    {
        if (_looked) return _table;
        _looked = true;
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Recompiled.WidescreenPatch")).FirstOrDefault(t => t != null);
        _table = type?.GetField("TestCollPrizeTable", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as ushort[];
        _vanilla = (ushort[]?)_table?.Clone();
        if (_table == null || _table.Length != Entries)
        {
            Log.Info("extended widescreen's prize table not found; randomized global drops apply only without it");
            _table = null;
        }
        return _table;
    }
}
