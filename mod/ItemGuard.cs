#nullable enable
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// Keeps an item number the game has no item for from reaching AddToInventory. The inventory has 169 hand
// items (kind 0) and 90 body items (any other kind). Given anything else, AddToInventory bumps a count
// outside the count table and, for a first copy, searches the order list for the number byte by byte
// with no end: a number over 255 never matches and the search runs off the end of RAM (the game
// crashes). A drop with such a number also shows a garbage icon, read from outside the item tables.
// Refusing it keeps the game running; the log says where the number came from.
static class ItemGuard
{
    const int HandItems = 169, BodyItems = 90;
    const uint CurrentEntityAddr = 0x8006C3B8;

    public static bool Allow(CpuContext c, IMemory m)
    {
        int limit = c.A1 == 0 ? HandItems : BodyItems;
        if (c.A0 < limit) return true;

        uint entity = m.ReadU32(CurrentEntityAddr);
        string source = entity >= 0x80000000 && entity < 0x80200000
            ? $"entity 0x{entity:X8}: id 0x{m.ReadU16(entity + 0x26):X}, update 0x{m.ReadU32(entity + 0x28):X8}, "
              + $"params 0x{m.ReadU16(entity + 0x30):X4}, step {m.ReadU16(entity + 0x2C)}, "
              + $"x {m.ReadU16(entity + 0x02)}, y {m.ReadU16(entity + 0x06)}, from 0x{m.ReadU32(entity + 0x94):X}"
            : "no current entity";
        Log.Error($"blocked an item the game doesn't have: AddToInventory({c.A0}, kind {c.A1}) from 0x{c.RA:X8} "
                  + $"in stage 0x{m.ReadU8(Game.StageIdAddr):X2}; {source}. Please report this with the log.");
        ApClient.ShowToast("Archipelago", $"Blocked a broken item (number {c.A0}) that would have crashed the game. The log has details.");

        // A drop being picked up (EquipItemDrop) shows the item's name next, from the name pointer it read
        // out of the same item table slot, now garbage: give it a real one.
        try
        {
            bool hand = c.A1 == 0;
            uint defs = m.ReadU32(hand ? HandDefsPtr : BodyDefsPtr);
            if (c.S1 == m.ReadU32(defs + c.A0 * (hand ? HandDefSize : BodyDefSize))) c.S1 = ApLook.CornerText(m, "Broken item");
        }
        catch (System.InvalidOperationException)
        {
            // the slot is outside RAM: the caller didn't read a name from it
        }
        return false;
    }

    const uint HandDefsPtr = 0x8003C830, BodyDefsPtr = 0x8003C834;
    const uint HandDefSize = 0x34, BodyDefSize = 0x20;
}
