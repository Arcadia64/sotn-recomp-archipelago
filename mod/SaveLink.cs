#nullable enable
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Ties a save file to one seed and slot, and keeps the count of items already given, both in
// castle flag bytes the game saves with the file. The same bytes the BizHawk client uses; none
// of them is a stage pickup or wall flag in the AP world's tables.
static class SaveLink
{
    const int SeedFlag = 0xF6;          // u32: hash of seed name + slot, 0 = not linked yet
    const int ReceivedFlag = 0x118;     // u16: how many received items have been given
    const int PastIntroFlag = 0x34;

    public enum Status { NotLinked, ThisSeed, OtherSeed }

    // Set when a save that was played without a connection gets linked; the check tracker clears it
    // after giving the seed's items for spots already collected (they gave their vanilla items).
    public static bool JustLinked;

    static uint Addr(int flag) => Progress.CastleFlagsAddr + (uint)flag;

    public static uint ExpectedHash()
    {
        // FNV-1a over "seed:slot"; never 0 so 0 can mean "not linked".
        uint h = 2166136261;
        foreach (char ch in $"{ApClient.SeedName}:{ApClient.Slot}")
        {
            h ^= ch;
            h *= 16777619;
        }
        return h == 0 ? 1 : h;
    }

    // The seed fingerprint the save in RAM holds (0 = not linked).
    public static uint StoredHash(IMemory m) => m.ReadU32(Addr(SeedFlag));

    public static Status Check(IMemory m)
    {
        uint stored = m.ReadU32(Addr(SeedFlag));
        if (stored == 0) return Status.NotLinked;
        return stored == ExpectedHash() ? Status.ThisSeed : Status.OtherSeed;
    }

    // Link the save in play to this seed once Alucard's game has started.
    public static Status Claim(IMemory m)
    {
        var status = Check(m);
        if (status != Status.NotLinked) return status;
        if (m.ReadU8(Addr(PastIntroFlag)) != 1) return status;
        m.WriteU32(Addr(SeedFlag), ExpectedHash());
        Log.Info($"this save is now linked to seed {ApClient.SeedName}, slot {ApClient.Slot}");
        JustLinked = true;
        return Status.ThisSeed;
    }

    public static int ReceivedCount(IMemory m) => m.ReadU16(Addr(ReceivedFlag));

    public static void SetReceivedCount(IMemory m, int count) => m.WriteU16(Addr(ReceivedFlag), (ushort)count);
}
