#nullable enable
using System.Collections.Generic;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Reads the game's flags for every location in the slot and sends newly collected ones.
static class CheckTracker
{
    const int ScanInterval = 10; // frames
    const uint RelicBase = 0x80097964;
    const int FaerieScrollRelic = 15;

    // Librarian (Jewel of Open slot): see IsCollectedAsVanilla.
    const long LibrarianLocation = 70;
    const int LibraryStage = 0x02;
    const ushort LibrarianRoom = 0x9470;
    const uint RoomFingerprintAddr = 0x80073084; // low half of g_Tilemap, as the BizHawk client reads it

    // Killing Doppleganger 40 also counts the Doppleganger 10 fight if it was skipped (BizHawk client);
    // its item is then given by OwedItems.
    const long Dopp40Location = 397;
    const long Dopp10Location = 388;

    // Goal: Dracula dead in his room, Alucard alive, not leaving by Library card.
    const uint DraculaHpAddr = 0x80076ED6;
    const uint AlucardHpAddr = 0x80097BA0;
    const uint DemoTimerAddr = 0x80072EFC;
    const ushort DraculaRoom = 0x6CE0;
    const int ShaftDraculaStage = 0x38;

    static bool _goalSent;
    static bool _warnedOtherSeed;
    static long _otherSeedWarnedAt;
    const long OtherSeedWarningEvery = 60 * 60;
    const float OtherSeedWarningSeconds = 15f;

    public static void Reset()
    {
        _goalSent = false;
        _warnedOtherSeed = false;
    }

    public static void Tick(IMemory m, long frame)
    {
        if (!ApClient.HasSeed || !SeedPlan.Ready) return; // offline: recorded, sent once connected
        if (frame % ScanInterval != 0) return;
        if (m.ReadU8(Game.GameStateAddr) != (byte)GameState.Play) return;

        var link = SaveLink.Claim(m);
        if (link == SaveLink.Status.OtherSeed)
        {
            // Shown again every minute while it lasts.
            if (!_warnedOtherSeed || frame - _otherSeedWarnedAt >= OtherSeedWarningEvery)
            {
                if (!_warnedOtherSeed)
                    Log.Error("this save belongs to a different seed or slot than the one connected: nothing is placed, sent or received.");
                _warnedOtherSeed = true;
                _otherSeedWarnedAt = frame;
                ApClient.ShowToast("Archipelago: different seed",
                    "This save is from a different seed than the server's, and its seed isn't on this PC: nothing is placed, sent or received until you connect to this save's server.",
                    OtherSeedWarningSeconds);
            }
            return;
        }
        if (link != SaveLink.Status.ThisSeed) return; // Alucard's game hasn't started yet

        int slot = ApClient.Slot;
        bool playedUnlinked = SaveLink.JustLinked;
        SaveLink.JustLinked = false;
        var found = new List<long>();
        foreach (var loc in LocationData.All)
        {
            if (!ApClient.IsMissing(loc.Id)) continue;
            if (IsCollected(m, loc, slot)) found.Add(loc.Id);
        }
        if (found.Contains(Dopp40Location) && ApClient.IsMissing(Dopp10Location) && !found.Contains(Dopp10Location))
            found.Add(Dopp10Location);

        HideSentItems(m, slot);

        if (found.Count > 0)
        {
            foreach (var id in found)
            {
                var loc = LocationData.Get(id);
                Log.Info($"checked: {loc?.Name ?? id.ToString()}");
                // Spots collected before this save was linked gave their vanilla item, so give the seed's.
                // (Special spots and enemysanity are covered by OwedItems either way.)
                if (playedUnlinked && loc != null && loc.Kind != Detect.Enemy && !SeedPlan.IsUnsupported(id)
                    && ApClient.TryGetScout(id, out var scout) && scout.Player == slot)
                    ItemGiver.QueueDirect(scout.Item, loc.Name);
            }
            ApClient.SendChecks(found);
        }

        CheckGoal(m);
    }

    // Another player's item that has already been sent (picked up before a death or reload, or taken
    // with !collect) is set as picked up in this save, so it doesn't appear again. Only floor items with
    // an item-table slot, where that is one pickup flag. Our own items stay: the server doesn't send
    // them back, so after a reload they have to be picked up again.
    static void HideSentItems(IMemory m, int slot)
    {
        foreach (var loc in LocationData.All)
        {
            if (loc.Kind != Detect.Loot || loc.Place.PickupFlags.Length == 0) continue;
            if (!ApClient.IsChecked(loc.Id) || SeedPlan.IsUnsupported(loc.Id)) continue;
            if (!ApClient.TryGetScout(loc.Id, out var scout) || scout.Player == slot) continue;
            byte bit = (byte)(1 << loc.Place.PickupBit);
            foreach (var flag in loc.Place.PickupFlags)
                if ((m.ReadU8(flag) & bit) == 0) m.WriteU8(flag, (byte)(m.ReadU8(flag) | bit));
        }
    }

    static bool IsCollected(IMemory m, LocationInfo loc, int slot)
    {
        // A spot holding one of our own relics spawns a relic, which sets no pickup bit; it counts
        // once that relic is owned. Not for spots the mod couldn't change: those still hold the vanilla item.
        if (loc.Kind == Detect.Loot && !SeedPlan.IsUnsupported(loc.Id) && ApClient.TryGetScout(loc.Id, out var scout)
            && scout.Player == slot && scout.Item >= ItemData.FirstRelic && scout.Item <= ItemData.LastRelic)
        {
            return (m.ReadU8(RelicBase + (uint)(scout.Item - ItemData.FirstRelic)) & 1) != 0;
        }
        return IsCollectedAsVanilla(m, loc);
    }

    // Whether the spot's own flag says it was collected, whatever item it held.
    public static bool IsCollectedAsVanilla(IMemory m, LocationInfo loc)
    {
        if (loc.Id == LibrarianLocation)
        {
            // One of our relics: sold as that relic, so bought once it's owned. An item: sold as a normal
            // shop entry, and ApLook records the purchase. Neither (not placed): visiting him counts.
            if (ApClient.TryGetScout(loc.Id, out var s) && s.Player == ApClient.Slot
                && s.Item >= ItemData.FirstRelic && s.Item <= ItemData.LastRelic)
                return (m.ReadU8(RelicBase + (uint)(s.Item - ItemData.FirstRelic)) & 1) != 0;
            if (SeedPlan.LibrarianSellsItem) return ApLook.LibrarianBought(m);
            return m.ReadU8(Game.StageIdAddr) == LibraryStage && m.ReadU16(RoomFingerprintAddr) == LibrarianRoom;
        }

        switch (loc.Kind)
        {
            case Detect.Loot:
                foreach (var addr in loc.Addresses)
                    if ((m.ReadU8(addr) & (1 << loc.Bit)) != 0) return true;
                return false;
            case Detect.Break:
                return (m.ReadU8(loc.Addresses[0]) & loc.Mask) != 0;
            case Detect.KillTime:
                return m.ReadU16(loc.Addresses[0]) != 0;
            case Detect.Enemy:
                if (ApClient.OptionInt("enemysanity") == 0) return false;
                if (ApClient.OptionInt("enemy_scroll") != 0 && (m.ReadU8(RelicBase + FaerieScrollRelic) & 1) == 0) return false;
                return (m.ReadU8(loc.Addresses[0]) & (1 << loc.Bit)) != 0;
            default:
                return false;
        }
    }

    static void CheckGoal(IMemory m)
    {
        if (_goalSent) return;
        if (m.ReadU8(Game.StageIdAddr) != ShaftDraculaStage) return;
        if (m.ReadU16(RoomFingerprintAddr) != DraculaRoom) return;
        ushort draculaHp = m.ReadU16(DraculaHpAddr);
        if (draculaHp != 0 && draculaHp <= 60000) return;
        if (m.ReadU32(AlucardHpAddr) == 0) return;
        if (m.ReadU8(DemoTimerAddr) == 4) return;

        _goalSent = true;
        Log.Info("goal reached: Dracula defeated");
        ApClient.SendGoal();
    }
}
