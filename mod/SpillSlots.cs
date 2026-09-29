#nullable enable
using System.Collections.Generic;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// Three relic spots that can hold an item use an item-table slot past the end of their stage's table
// (Locations.py "index"): the Castle Keep's Ghost card spot (index 20), Death Wing's Lair's Rib of Vlad spot
// (12) and the Reverse Clock Tower's Ring of Vlad spot (12). Those two bytes belong to something else: the
// portraits of the Keep's cutscene (read by TOP_EntityCutscene), a velocity table in Death Wing's Lair and the
// first frame of the Reverse Clock Tower's explosion puffs. The AP patch writes the item there regardless,
// which changes them. Here that data stays as it is, and the item is in the slot only while the stage's
// item-pickup code runs (the one place each stage reads its item table).
static class SpillSlots
{
    static readonly (int Stage, uint Addr)[] Slots =
    [
        (0x0B, 0x80180D38), // TOP, index 20
        (0x24, 0x80180D58), // RNO2, index 12
        (0x2D, 0x80180EE0), // RNZ1, index 12
    ];

    static readonly Dictionary<uint, (int Stage, byte Value)> _seed = [];
    static readonly List<(uint Addr, byte Value)> _saved = [];

    public static void Clear() => _seed.Clear();

    // A placement write to one of the slots is kept here instead of going to RAM (SeedPlan.Add).
    public static bool Take(int stage, uint addr, ushort value, int size)
    {
        bool taken = false;
        for (int i = 0; i < size; i++)
        {
            if (!IsSlotByte(stage, addr + (uint)i)) continue;
            _seed[addr + (uint)i] = (stage, (byte)(value >> (8 * i)));
            taken = true;
        }
        return taken;
    }

    static bool IsSlotByte(int stage, uint addr)
    {
        foreach (var slot in Slots)
            if (slot.Stage == stage && (addr == slot.Addr || addr == slot.Addr + 1)) return true;
        return false;
    }

    [PreHook("top", "EntityHeartDrop_top")] static void TopIn(CpuContext c, IMemory m) => Put(m);
    [PostHook("top", "EntityHeartDrop_top")] static void TopOut(CpuContext c, IMemory m) => Restore(m);
    [PreHook("rno2", "RNO2_EntityHeartDrop")] static void Rno2In(CpuContext c, IMemory m) => Put(m);
    [PostHook("rno2", "RNO2_EntityHeartDrop")] static void Rno2Out(CpuContext c, IMemory m) => Restore(m);
    [PreHook("rnz1", "func_801B3A54_rnz1")] static void Rnz1In(CpuContext c, IMemory m) => Put(m);
    [PostHook("rnz1", "func_801B3A54_rnz1")] static void Rnz1Out(CpuContext c, IMemory m) => Restore(m);

    static void Put(IMemory m)
    {
        Restore(m); // never left in (a pickup code that didn't return normally)
        if (_seed.Count == 0 || !SeedPlan.ActiveFor(m)) return;
        int stage = m.ReadU8(Game.StageIdAddr);
        foreach (var (addr, (seedStage, value)) in _seed)
        {
            if (seedStage != stage) continue;
            _saved.Add((addr, m.ReadU8(addr)));
            m.WriteU8(addr, value);
        }
    }

    static void Restore(IMemory m)
    {
        foreach (var (addr, value) in _saved) m.WriteU8(addr, value);
        _saved.Clear();
    }
}
