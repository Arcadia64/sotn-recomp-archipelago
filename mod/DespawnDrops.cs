#nullable enable
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Items that fall out of a breakable wall or vase (LocationData.Despawn) vanish if not picked up in
// time: the drop rests for 240 frames, blinks for 80, and is gone. Another player's item there is
// safe (the check is sent when the wall breaks), but one of your own would be lost. So while such a
// drop rests on the ground, hold its timer up and it stays until picked up.
//
// The game keeps placed items with params bit 15, but for a drop that bit would also mark a slot in
// the stage's item table, so the timer is the safe handle. The generator checks every hooked stage's
// drop code still works this way (OptionHookSites, tools/gen_location_data.py).
static class DespawnDrops
{
    const uint StepOffset = 0x2C, ParamsOffset = 0x30, TimerOffset = 0x80;
    const ushort Resting = 3;
    const ushort Persistent = 0x8000;
    const byte FullTimer = 0xF0;

    // PreHook on each stage's item-drop entity; A0 is the entity.
    public static void Keep(CpuContext c, IMemory m)
    {
        uint self = c.A0;
        if (m.ReadU16(self + StepOffset) != Resting) return;
        ushort item = m.ReadU16(self + ParamsOffset);
        if ((item & Persistent) != 0 || !FromDespawnSpot(m, item)) return;
        if (m.ReadU8(self + TimerOffset) < FullTimer) m.WriteU8(self + TimerOffset, FullTimer);
    }

    // Whether a despawn spot in this stage drops this id: the id at the spot's item-id address, which
    // holds the seed's item (or the AP placeholder for another player's) once the stage is placed.
    // Another drop of the same item nearby (an enemy's) is kept too, which does no harm.
    static bool FromDespawnSpot(IMemory m, ushort item)
    {
        int stage = m.ReadU8(Game.StageIdAddr);
        foreach (long id in LocationData.Despawn)
        {
            var place = LocationData.Get(id)?.Place;
            if (place == null) continue;
            foreach (var at in place.Addresses)
                if (at.Stage == stage && m.ReadU16(at.Addr) == item) return true;
            foreach (var at in place.ItemTable)
                if (at.Stage == stage && m.ReadU16(at.Addr) == item) return true;
        }
        return false;
    }
}
