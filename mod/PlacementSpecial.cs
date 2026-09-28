#nullable enable
using System.Collections.Generic;

namespace SotnArchipelago;

// Ports of the AP world's Rom.py special-spot replacements (b08161): the same bytes, written to RAM
// on stage load instead of to the disc. The recomp's own rewrites (patches/rando/RandoPatch.cs) read
// several of these back from RAM (boss reward index, Ring of Vlad entity words, Gold ring jump word,
// Trio erase word, Holy glasses erase word, Librarian relic offset). The injected MIPS routines are
// ported byte for byte so tools/verify_placement.py can check everything; the recomp never executes
// them, and their behaviour is done in C# where needed (see SpecialSpots.cs).
static class Special
{
    const uint OverlayBase = 0x80180000;
    const int EquipIdOffset = -0xA9;        // Constants.py equip_id_offset
    const int EquipInvIdOffset = 0x798A;    // Constants.py equip_inv_id_offset

    // Rom.py write_tokens: stage Update entry point and injection offset for each Vlad relic boss.
    public static readonly Dictionary<string, (int Entry, int Inj)> VladEntry = new()
    {
        ["Heart of vlad"] = (0x034950, 0x047900),
        ["Tooth of vlad"] = (0x029fc0, 0x037500),
        ["Rib of vlad"] = (0x037014, 0x04bf00),
        ["Eye of vlad"] = (0x01af18, 0x02a000),
    };
    static readonly (int Entry, int Inj) TrioEntry = (0x026e64, 0x038a00);

    // Constants.py slots (MainRAM offsets of the equipped-item slots), in Rom.py item_slots order.
    static uint[] ItemSlots(ItemInfo item) => item.Type switch
    {
        ItemType.Weapon1 or ItemType.Weapon2 or ItemType.Shield or ItemType.Usable => [0x097C04, 0x097C00],
        ItemType.Helmet => [0x097C08],
        ItemType.Armor => [0x097C0C],
        ItemType.Cloak => [0x097C10],
        ItemType.Accessory => [0x097C14, 0x097C18],
        _ => [],
    };

    // replace_boss_relic_with_item
    public static void BossRelicWithItem(Placement.Result r, LocationInfo loc, ItemInfo item, (int Entry, int Inj) at)
    {
        var p = loc.Place;
        int stage = loc.Stages[0];
        var zone = ZoneFor(stage);
        int bossStage = p.Reward[0].Stage;
        var boss = ZoneFor(bossStage);
        int index = p.Index;
        ushort tile = Placement.TileValue(item);

        W16(r, stage, File(zone.Items + 2 * index), tile);
        foreach (var e in p.Entities)
        {
            if (p.AsItemX >= 0) W16(r, stage, e.Addr + 0, p.AsItemX);
            if (p.AsItemY >= 0) W16(r, stage, e.Addr + 2, p.AsItemY);
            W16(r, stage, e.Addr + 4, 0x000C);
            W16(r, stage, e.Addr + 8, index);
        }
        var erase = SpecialData.Erase[loc.Id][0];
        W32(r, erase.At.Stage, erase.At.Addr, erase.Instruction);
        W16(r, bossStage, File(boss.Rewards), tile);

        W32(r, stage, File(at.Entry), 0x08060000 + ((uint)at.Inj >> 2)); // j inj
        W32(r, stage, File(at.Entry + 4), 0x00041400);                     // sll v0, a0, 10

        uint off = File(at.Inj);
        W32(r, stage, ref off, (uint)(0x34090000 + item.Id + EquipIdOffset));
        var slots = ItemSlots(item);
        for (int i = 0; i < slots.Length; i++)
        {
            W32(r, stage, ref off, 0x3C080000 + (slots[i] >> 16));
            W32(r, stage, ref off, 0x91080000 + (slots[i] & 0xFFFF));
            W32(r, stage, ref off, 0x00000000);
            W32(r, stage, ref off, (uint)(0x11090000 + 5 + 5 * (slots.Length - i - 1)));
            W32(r, stage, ref off, 0x00000000);
        }
        W32(r, stage, ref off, 0x3C088009);
        W32(r, stage, ref off, (uint)(0x91080000 + item.Id + EquipInvIdOffset));
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x11000004);
        W32(r, stage, ref off, 0x3409000F);
        W32(r, stage, ref off, 0x3C088018);
        foreach (var e in p.Entities)
            W32(r, stage, ref off, 0xA5090000 + (e.Addr - OverlayBase) + 4);
        W32(r, stage, ref off, 0x03E00008);
        W32(r, stage, ref off, 0x00000000);
    }

    // replace_ring_of_vlad_with_item
    public static void RingOfVladWithItem(Placement.Result r, LocationInfo loc, ItemInfo item)
    {
        var p = loc.Place;
        int stage = loc.Stages[0];
        var zone = ZoneFor(stage);
        int index = p.Index;
        ushort tile = Placement.TileValue(item);

        var erase = SpecialData.Erase[loc.Id][0];
        W32(r, erase.At.Stage, erase.At.Addr, erase.Instruction);
        W32(r, SpecialData.RingOfVladEntityId.Stage, SpecialData.RingOfVladEntityId.Addr, 0x3402000C);
        W32(r, SpecialData.RingOfVladUpdateLow.Stage, SpecialData.RingOfVladUpdateLow.Addr, 0x24423A54);
        W16(r, SpecialData.RingOfVladIndex.Stage, SpecialData.RingOfVladIndex.Addr, index);
        W16(r, stage, File(0x2DD6), index);
        W16(r, stage, File(zone.Rewards), tile);
        W16(r, stage, File(zone.Items + 2 * index), tile);
        W32(r, stage, File(0x02C860), 0x0806FBB4); // j 0x801beed0
        W32(r, stage, File(0x02C868), 0x00000000);

        uint off = File(0x3EED0);
        W32(r, stage, ref off, 0x3C020003);
        W32(r, stage, ref off, 0x3442CA78);
        W32(r, stage, ref off, 0x8C420000);
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x10400005);
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x3C088018);
        W32(r, stage, ref off, (uint)(0x34090000 + p.AsItemY));
        foreach (var e in p.Entities)
            W32(r, stage, ref off, 0xA5090000 + (e.Addr - OverlayBase) + 2);
        W32(r, stage, ref off, (uint)(0x34020000 + item.Id + EquipIdOffset));
        var slots = ItemSlots(item);
        for (int i = 0; i < slots.Length; i++)
        {
            W32(r, stage, ref off, 0x3C108000 + (slots[i] >> 16));
            W32(r, stage, ref off, 0x92100000 + (slots[i] & 0xFFFF));
            W32(r, stage, ref off, 0x00000000);
            W32(r, stage, ref off, (uint)(0x12020000 + 5 + 5 * (slots.Length - i - 1)));
            W32(r, stage, ref off, 0x00000000);
        }
        W32(r, stage, ref off, 0x3C108009);
        W32(r, stage, ref off, (uint)(0x92100000 + item.Id + EquipInvIdOffset));
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x12000002);
        W32(r, stage, ref off, 0x3C108007);
        W32(r, stage, ref off, 0xAE0065F0);
        W32(r, stage, ref off, 0x0806B21A); // j 0x801ac868
        W32(r, stage, ref off, 0x00000000);
    }

    // replace_gold_ring_with_relic
    public static void GoldRingWithRelic(Placement.Result r, Place p, ushort relic)
    {
        int stage = p.Entities[0].Stage;
        foreach (var e in p.Entities) W16(r, e.Stage, e.Addr + 8, relic);
        W32(r, stage, File(0x04C590), 0x08077AED); // j 0x801debb4 (the recomp checks for this word)

        uint off = File(0x05EBB4);
        W32(r, stage, ref off, 0x10400003);
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x08073166);
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x3C020003);
        W32(r, stage, ref off, 0x3442CA4C);
        W32(r, stage, ref off, 0x8C420000);
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x10400006);
        W32(r, stage, ref off, 0x00000000);
        W32(r, stage, ref off, 0x3403000B);
        W32(r, stage, ref off, 0x3C028018);
        foreach (var e in p.Entities)
            W32(r, stage, ref off, 0xA4430000 + (e.Addr - OverlayBase) + 4);
        W32(r, stage, ref off, 0x34020000);
        W32(r, stage, ref off, 0x0807316F);
        W32(r, stage, ref off, 0x00000000);
    }

    // replace_trio_with_relic
    public static void TrioWithRelic(Placement.Result r, Place p, ushort relic)
    {
        var reward = p.Reward[0];
        W16(r, reward.Stage, reward.Addr, relic);
        W32(r, reward.Stage, File(0x026088), 0x34020000); // the recomp checks for this word
        foreach (var e in p.Entities)
        {
            W16(r, e.Stage, e.Addr + 4, 0x000B);
            W16(r, e.Stage, e.Addr + 6, 0x0010);
            W16(r, e.Stage, e.Addr + 8, relic);
        }
    }

    // replace_trio_relic_with_item
    public static void TrioWithItem(Placement.Result r, LocationInfo loc, ItemInfo item)
    {
        BossRelicWithItem(r, loc, item, TrioEntry);
        foreach (var e in loc.Place.Entities) W16(r, e.Stage, e.Addr + 6, 0x0010);
    }

    // replace_holy_glasses_with_relic
    public static void HolyGlassesWithRelic(Placement.Result r, LocationInfo loc, ushort relic)
    {
        var erase = SpecialData.Erase[loc.Id][0];
        W32(r, erase.At.Stage, erase.At.Addr, erase.Instruction);
        int stage = erase.At.Stage; // CEN
        foreach (int a in new[] { 0x1328, 0x13BE })
        {
            W16(r, stage, File(a + 0), 0x0180);
            W16(r, stage, File(a + 2), 0x022C);
            W16(r, stage, File(a + 4), 0x000B);
            W16(r, stage, File(a + 6), 0x0000);
            W16(r, stage, File(a + 8), relic);
        }
    }

    // replace_shop_relic_with_relic
    public static void ShopRelicWithRelic(Placement.Result r, Place p, ushort relic)
    {
        foreach (var a in p.Addresses) W8(r, a.Stage, a.Addr, relic);
        var off = SpecialData.JewelRelicOffset;
        W8(r, off.Stage, off.Addr, relic + 0x64);

        // Menu name: the relic's name with 0x20 subtracted per character, 0xFF 0x00 after it, zero padded.
        string name = ItemData.Get(ItemData.FirstRelic + relic)?.Name ?? "";
        var bytes = new byte[16];
        for (int i = 0; i < 16; i++)
            bytes[i] = i < name.Length ? (byte)(name[i] - 0x20) : i == name.Length ? (byte)0xFF : (byte)0x00;
        if (name.Length < 16) bytes[name.Length] = 0xFF;
        if (name.Length + 1 < 16) bytes[name.Length + 1] = 0x00;
        var at = SpecialData.JewelName;
        for (int i = 0; i < 16; i++) W8(r, at.Stage, at.Addr + (uint)i, bytes[i]);
    }

    // The Librarian's first shop entry (Jewel of Open in vanilla) as a normal item entry: type,
    // availability 0 (always), item id. The shop's own code then lists, previews and sells it like any
    // other item; ApLook turns a purchase of it into the check. Rom.py instead keeps a relic-style
    // entry and edits 14 instructions and injects routines (replace_shop_relic_with_item), which the
    // recomp can't run and which overwrite the relic-owned offset its rewrite reads (0x801B2B08).
    // Stock: always available, like the AP patch ("so the item is always on shop"), at 10 gold.
    const uint ShopEntry0 = 0x8018134C;   // u8 type, u8 availability, u16 id, u32 price
    const int LibraryStage = 0x02;

    public static void ShopEntryWithItem(Placement.Result r, ItemInfo item)
    {
        int type = item.Type switch
        {
            ItemType.Helmet => 1, ItemType.Armor => 2, ItemType.Cloak => 3, ItemType.Accessory => 4, _ => 0,
        };
        int id = type == 0 ? (int)item.Id : (int)item.Id - 169; // hand id, or body index
        W8(r, LibraryStage, ShopEntry0 + 0, type);
        W8(r, LibraryStage, ShopEntry0 + 1, 0x00);
        W16(r, LibraryStage, ShopEntry0 + 2, id);
        r.DiffersFromPatch = "sold as a normal shop entry instead of Rom.py's relic-slot code patch";
    }

    // ---- helpers ----

    static (int Stage, int Items, int Rewards) ZoneFor(int stage)
    {
        foreach (var z in SpecialData.Zones.Values)
            if (z.Stage == stage) return z;
        throw new KeyNotFoundException($"no zone for stage 0x{stage:X2}");
    }

    static uint File(int fileOffset) => OverlayBase + (uint)fileOffset;

    static void W8(Placement.Result r, int stage, uint addr, int value) => r.Writes.Add(new(stage, addr, (byte)value, 1));

    static void W16(Placement.Result r, int stage, uint addr, int value) => r.Writes.Add(new(stage, addr, (ushort)value));

    static void W32(Placement.Result r, int stage, uint addr, uint value)
    {
        W16(r, stage, addr, (ushort)(value & 0xFFFF));
        W16(r, stage, addr + 2, (ushort)(value >> 16));
    }

    static void W32(Placement.Result r, int stage, ref uint addr, uint value)
    {
        W32(r, stage, addr, value);
        addr += 4;
    }
}
