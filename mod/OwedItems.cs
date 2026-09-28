#nullable enable
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Own items the game can't hand out itself, worked out from the save every time rather than
// queued, so a crash or loading an older save can't lose one:
// - spots the mod can't place yet (the vanilla item is still there), once collected;
// - enemysanity kills (there is no pickup at all);
// - the Doppleganger 10 fight when Doppleganger 40 was beaten first.
// Each has a "given" bit in castle flags that the BizHawk client doesn't use for anything else.
static class OwedItems
{
    // Enemysanity "given" bits, same bytes as the BizHawk client: CF[0xE3] for bestiary ids 0-7,
    // CF[0xE4 + id / 8] for the rest (CF[0xE4] itself is the second clock room door).
    const int EnemyGivenLow = 0xE3;
    const int EnemyGivenBase = 0xE4;

    // One bit per special spot, in CF[0xFA..0xFB] (the BizHawk client's seed area; we only use 0xF6-0xF9).
    const int SpecialGivenBase = 0xFA;
    static readonly long[] SpecialSpots =
    [
        70,   // Long Library - Librarian Shop Item
        85,   // Marble Gallery - Item Given by Maria (Holy glasses)
        130,  // Underground Caverns Succubus Side - Succubus item (Gold ring)
        240,  // Cave - Death Item (Eye of Vlad)
        257,  // Medusa (Heart of Vlad)
        288,  // Creature (Tooth of Vlad)
        301,  // Akmodan II (Rib of Vlad)
        366,  // Darkwing Bat (Ring of Vlad)
        386,  // Trio
        95,   // Outer Wall breakable wall (NO1)
        287,  // Reverse Outer Wall breakable wall (RNO1)
        312,  // Reverse Entrance big rock (RNO3)
        388,  // Doppleganger 10, when skipped
    ];

    const long Dopp40Location = 397;
    const long Dopp10Location = 388;

    public readonly record struct Owed(long Item, string Why, uint FlagAddr, byte Mask);

    // The next own item the save is owed, or null.
    public static Owed? Next(IMemory m)
    {
        int slot = ApClient.Slot;
        for (int i = 0; i < SpecialSpots.Length; i++)
        {
            long id = SpecialSpots[i];
            var loc = LocationData.Get(id);
            if (loc == null || !OwnItem(id, slot, out long item)) continue;

            bool owed = id == Dopp10Location
                ? IsKilled(m, LocationData.Get(Dopp40Location)) && !IsKilled(m, loc)
                : SeedPlan.IsUnsupported(id) && CheckTracker.IsCollectedAsVanilla(m, loc);
            if (!owed) continue;

            uint addr = Progress.CastleFlagsAddr + (uint)(SpecialGivenBase + i / 8);
            byte mask = (byte)(1 << (i % 8));
            if ((m.ReadU8(addr) & mask) == 0) return new Owed(item, loc.Name, addr, mask);
        }

        if (ApClient.OptionInt("enemysanity") == 0) return null;
        foreach (var loc in LocationData.All)
        {
            if (loc.Kind != Detect.Enemy || !OwnItem(loc.Id, slot, out long item)) continue;
            if (!CheckTracker.IsCollectedAsVanilla(m, loc)) continue;

            // Bestiary bit for this enemy: Addresses[0] is CF[0x190 + id / 8], Bit is id % 8.
            int bestiaryByte = (int)(loc.Addresses[0] - (Progress.CastleFlagsAddr + 0x190));
            int flag = bestiaryByte == 0 ? EnemyGivenLow : EnemyGivenBase + bestiaryByte;
            uint addr = Progress.CastleFlagsAddr + (uint)flag;
            byte mask = (byte)(1 << loc.Bit);
            if ((m.ReadU8(addr) & mask) == 0) return new Owed(item, loc.Name, addr, mask);
        }
        return null;
    }

    public static void MarkGiven(IMemory m, Owed owed) => m.WriteU8(owed.FlagAddr, (byte)(m.ReadU8(owed.FlagAddr) | owed.Mask));

    static bool OwnItem(long location, int slot, out long item)
    {
        item = 0;
        if (!ApClient.TryGetScout(location, out var scout) || scout.Player != slot) return false;
        item = scout.Item;
        return true;
    }

    static bool IsKilled(IMemory m, LocationInfo? loc) =>
        loc != null && loc.Kind == Detect.KillTime && m.ReadU16(loc.Addresses[0]) != 0;
}
