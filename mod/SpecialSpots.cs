#nullable enable
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// Rewards that exist twice when a boss's relic becomes an item: the boss drops it (boss reward
// table) and the normal stage has a copy in the boss room (an item-table pickup, for when the drop
// was missed). Relics never spawn once owned; items need the pickup flag instead. The AP patch
// hid the room copy once the item was "owned" (inventory or equipped), which the recomp doesn't run
// and which misfires for consumables or a second copy. Here both copies share the room copy's
// pickup flag, so the reward is collected exactly once and is never lost.
static class SpecialSpots
{
    // ---- Vlad relic bosses and Trio: boss drop marks the room copy collected ----

    // Called for every item pickup from stage code (ApLook's AddToInventory hook).
    public static void OnPickup(IMemory m, long item)
    {
        int stage = m.ReadU8(Game.StageIdAddr);
        int slot = ApClient.Slot;
        foreach (var loc in LocationData.All)
        {
            var p = loc.Place;
            if (loc.Kind != Detect.KillTime || p.Reward.Length == 0 || p.PickupFlags.Length == 0) continue;
            if (p.Reward[0].Stage != stage) continue;                       // in this boss's own stage
            if (m.ReadU16(loc.Addresses[0]) == 0) continue;                 // boss not beaten yet
            if (!ApClient.TryGetScout(loc.Id, out var scout)) continue;
            long placed = scout.Player == slot ? scout.Item : Placement.PlaceholderItem;
            if (placed != item || SeedPlan.IsUnsupported(loc.Id)) continue;

            foreach (var flag in p.PickupFlags)
                m.WriteU8(flag, (byte)(m.ReadU8(flag) | 1 << p.PickupBit));
            Log.Info($"{loc.Name}: boss drop collected, room copy marked collected");
        }
    }

    // ---- Darkwing Bat (Ring of Vlad spot holding an item) ----

    // With the AP patch's item words in RAM (0x801AC84C = 0x24423A54), the recomp's rewrite of the
    // RNZ1 reward orb (RandoPatch.func_801BE578) turns the death drop into an item-table pickup with
    // item-table slot 4, a fixed guess ("Seems to use Equipment List Entry 4...?"). The AP patch
    // keeps the item in slot 12 (0x801AC85C, also used for the "already dead" spawn). Slot 4 is
    // another location's: its item would show and its pickup flag would be set. Use the patch's slot.
    const uint ItemPatchWord = 0x801AC84C;
    const uint ItemPatchValue = 0x24423A54;
    const uint RingIndexAddr = 0x801AC85C;
    const uint HeartDropUpdate = 0x801B3A54;
    const int RecompGuessedIndex = 4;

    static uint _orb;

    [PreHook("rnz1", "func_801BE578")]
    static void BeforeRewardOrb(CpuContext c, IMemory m) => _orb = c.A0;

    [PostHook("rnz1", "func_801BE578")]
    static void AfterRewardOrb(CpuContext c, IMemory m)
    {
        uint e = _orb;
        _orb = 0;
        if (e == 0 || m.ReadU32(ItemPatchWord) != ItemPatchValue) return;
        if (m.ReadU32(e + 0x28) != HeartDropUpdate || m.ReadU16(e + 0x30) != RecompGuessedIndex) return;
        m.WriteU16(e + 0x30, m.ReadU16(RingIndexAddr));
    }
}
