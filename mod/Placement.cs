#nullable enable
using System.Collections.Generic;

namespace SotnArchipelago;

// Size is 2 (u16) or 1 (byte). 32-bit values are written as two u16 halves.
public readonly record struct RamWrite(int Stage, uint Addr, ushort Value, byte Size = 2);

// Works out the RAM writes that put a location's item in the game. A straight port of the
// per-location part of the AP world's Rom.py write_tokens (b08161, lines 800-1013), writing
// to RAM on stage load instead of to the disc image. Pure: no game access, so it can be
// checked offline against the patch the AP generator produces (tools/verify_placement.py).
// How other players' items look in the castle.
public enum Look
{
    ApItem,  // the "----" accessory (item 226), drawn with the AP icon and named by ApLook
    Bags,    // what the AP world's patch uses: money bags, Secret boots (tools/verify_placement.py)
}

public static class Placement
{
    const int TileIdOffset = 0x80;

    // "----" (accessory 57, the empty-accessory entry): never in the pool, never an enemy or shop
    // item, so a pickup of it is always one of ours. ApLook swaps its icon and name at pickup.
    public const long PlaceholderItem = 226;
    public static readonly ItemInfo Placeholder = new(PlaceholderItem, "AP item", ItemType.Accessory);
    const ushort RelicEntity = 0x000B;
    const ushort ItemEntity = 0x000C;

    // Money bags stand in for other players' items at normal item spots, coloured by importance.
    const ushort BlueBag = 0x07;   // progression
    const ushort RedBag = 0x03;    // useful
    const ushort YellowBag = 0x04; // anything else

    static readonly HashSet<string> VladRelics = ["Heart of vlad", "Tooth of vlad", "Rib of vlad", "Ring of vlad", "Eye of vlad"];

    public sealed class Result
    {
        public readonly List<RamWrite> Writes = [];
        public string? Unsupported;
        // Set where the mod places the item differently from the AP patch on purpose (the checker
        // doesn't compare these bytes).
        public string? DiffersFromPatch;
    }

    // Always applied, whatever the seed: the Librarian sells Jewel of Open for 10 gold (Rom.py 1016).
    public static readonly RamWrite[] Always = [new(0x02, 0x80181350, 10)];

    public static Result Compute(LocationInfo loc, NetworkItem scout, int slot, Look look = Look.ApItem)
    {
        var r = new Result();
        if (loc.Name.StartsWith("Enemysanity")) return r; // granted by the client, nothing placed

        var p = loc.Place;
        if (scout.Player == slot) Own(loc, p, scout, r);
        else Remote(loc, p, scout, r, look);
        return r;
    }

    static void Own(LocationInfo loc, Place p, NetworkItem scout, Result r)
    {
        var item = ItemData.Get(scout.Item);
        if (item == null) { r.Unsupported = $"unknown item id {scout.Item}"; return; }
        if (item.Type == ItemType.Event) return;
        ushort id = TileValue(item);
        string vanilla = loc.VanillaItem;

        if (loc.IsRelicSpot)
        {
            if (item.Type == ItemType.Relic)
            {
                if (vanilla == "Jewel of open")
                {
                    Special.ShopRelicWithRelic(r, p, id);
                    return;
                }
                if (vanilla is "Bat card" or "Skill of wolf")
                {
                    Put(r, p.Addresses, id);
                    return;
                }
                Entity(r, p, state: id);
                if (VladRelics.Contains(vanilla))
                {
                    if (vanilla == "Ring of vlad") Put(r, p.RingIds, id);
                    else Put(r, p.Reward, id);
                }
                return;
            }

            if (vanilla == "Jewel of open") { Special.ShopEntryWithItem(r, item); return; }
            if (vanilla == "Ring of vlad") { Special.RingOfVladWithItem(r, loc, item); return; }
            if (VladRelics.Contains(vanilla)) { Special.BossRelicWithItem(r, loc, item, Special.VladEntry[vanilla]); return; }
            RelicSpotAsItem(r, p, id);
            return;
        }

        if (p.NoOffset)
        {
            if (item.Type == ItemType.Relic) Entity(r, p, id: RelicEntity, state: id, x: p.AsRelicX, y: p.AsRelicY);
            else if (item.Type == ItemType.Powerup) Put(r, p.Addresses, 0);
            else Put(r, p.Addresses, (ushort)item.Id);
            return;
        }

        if (vanilla == "Holy glasses")
        {
            if (item.Type == ItemType.Relic) Special.HolyGlassesWithRelic(r, loc, id);
            else Put(r, p.Addresses, (ushort)item.Id);
            return;
        }

        if (p.Trio)
        {
            if (item.Type == ItemType.Relic) Special.TrioWithRelic(r, p, id);
            else Special.TrioWithItem(r, loc, item);
            return;
        }

        if (p.Index >= 0)
        {
            if (vanilla == "Gold ring")
            {
                if (item.Type == ItemType.Relic) { Special.GoldRingWithRelic(r, p, id); return; }
                Put(r, p.Addresses, id);
                return;
            }
            if (item.Type == ItemType.Relic) Entity(r, p, id: RelicEntity, state: id, x: p.AsRelicX, y: p.AsRelicY);
            else Put(r, p.ItemTable, id);
            return;
        }

        if (loc.Id == 40) Put(r, p.Addresses, id);         // CHI turkey wall is a normal tile id
        else if (p.BossDrop.Length > 0) Put(r, p.BossDrop, id);
        else r.Unsupported = "no known way to place an item here";
    }

    static void Remote(LocationInfo loc, Place p, NetworkItem scout, Result r, Look look)
    {
        // Rom.py uses a money bag coloured by importance at item-table spots, Secret boots elsewhere.
        bool ap = look == Look.ApItem;
        ushort bag = ap ? (ushort)(PlaceholderItem + TileIdOffset) : scout.Progression ? BlueBag : scout.Useful ? RedBag : YellowBag;
        ushort boots = (ushort)((ap ? PlaceholderItem : ItemData.SecretBoots) + TileIdOffset);
        ushort rawBoots = (ushort)(ap ? PlaceholderItem : ItemData.SecretBoots);
        string vanilla = loc.VanillaItem;

        var bootsItem = ap ? Placeholder : ItemData.Get(ItemData.SecretBoots)!;
        if (loc.IsRelicSpot)
        {
            if (vanilla == "Jewel of open") { Special.ShopEntryWithItem(r, ap ? Placeholder : bootsItem); return; }
            if (vanilla == "Ring of vlad") { Special.RingOfVladWithItem(r, loc, bootsItem); return; }
            if (VladRelics.Contains(vanilla)) { Special.BossRelicWithItem(r, loc, bootsItem, Special.VladEntry[vanilla]); return; }
            RelicSpotAsItem(r, p, bag);
            return;
        }

        if (p.NoOffset || vanilla == "Holy glasses")
        {
            Put(r, p.Addresses, rawBoots);
            return;
        }

        if (p.Trio) { Special.TrioWithItem(r, loc, bootsItem); return; }

        if (p.Index >= 0)
        {
            Put(r, vanilla == "Gold ring" ? p.Addresses : p.ItemTable, bag);
            return;
        }

        if (loc.Id == 40) Put(r, p.Addresses, boots);
        else if (p.BossDrop.Length > 0) Put(r, p.BossDrop, boots);
        else r.Unsupported = "no known way to place an item here";
    }

    // A relic's spot turned into an item pickup: item entity pointing at the item table slot.
    static void RelicSpotAsItem(Result r, Place p, ushort tile)
    {
        if (p.Index < 0) { r.Unsupported = "relic spot without an item table slot"; return; }
        Entity(r, p, id: ItemEntity, state: (ushort)p.Index, x: p.AsItemX, y: p.AsItemY);
        Put(r, p.ItemTable, tile);
    }

    // Tile value as the stage item tables store it (Rom.py tile_value).
    public static ushort TileValue(ItemInfo item) => item.Type switch
    {
        ItemType.Relic => (ushort)(item.Id - ItemData.FirstRelic),
        ItemType.Powerup => (ushort)(item.Id - 400),
        _ => (ushort)(item.Id + TileIdOffset),
    };

    // Layout entry fields: x +0, y +2, id +4, state +8 (Rom.py write_entity).
    static void Entity(Result r, Place p, ushort? id = null, ushort? state = null, int x = -1, int y = -1)
    {
        foreach (var e in p.Entities)
        {
            if (x >= 0) r.Writes.Add(new(e.Stage, e.Addr + 0x0, (ushort)x));
            if (y >= 0) r.Writes.Add(new(e.Stage, e.Addr + 0x2, (ushort)y));
            if (id is { } i) r.Writes.Add(new(e.Stage, e.Addr + 0x4, i));
            if (state is { } s) r.Writes.Add(new(e.Stage, e.Addr + 0x8, s));
        }
    }

    static void Put(Result r, StageAddr[] where, ushort value)
    {
        foreach (var a in where) r.Writes.Add(new(a.Stage, a.Addr, value));
    }
}
