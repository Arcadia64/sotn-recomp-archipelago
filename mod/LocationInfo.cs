#nullable enable
using System.Collections.Generic;

namespace SotnArchipelago;

// How a location is detected as collected. Mirrors the BizHawk client's order of checks.
public enum Detect
{
    Loot,       // stage pickup bit: Addresses[i] bit Bit, for each zone the location is in
    Break,      // breakable wall / Holy glasses castle flag: Addresses[0] & Mask
    KillTime,   // boss time-attack record (u16) at Addresses[0] is non-zero
    Enemy,      // bestiary bit (enemysanity): Addresses[0] bit Bit
    Librarian,  // Jewel of Open shop slot, handled separately
}

// A RAM address inside a stage or boss overlay; only valid while that stage (g_StageId) is loaded.
public readonly record struct StageAddr(int Stage, uint Addr);

// Where a location's item lives, from the AP world's Locations.py (see Placement.cs).
public sealed record Place(
    int Index,                // slot in the stage item table, or -1
    StageAddr[] ItemTable,    // u16 tile id per zone the location is in
    StageAddr[] Entities,     // room layout entries (x-sorted and y-sorted list per zone)
    int AsRelicX, int AsRelicY,
    int AsItemX, int AsItemY,
    bool NoOffset,            // breakable wall: raw item id, no tile offset
    StageAddr[] Addresses,    // extra item-id spots (walls, Holy glasses, Gold ring, NZ0 globes, CHI turkey)
    StageAddr[] BossDrop,     // boss overlay drop tile
    StageAddr[] Reward,       // Vlad relic boss reward table slot
    StageAddr[] RingIds,      // Ring of Vlad relic id spots
    bool Trio,
    uint[] PickupFlags,       // stage pickup flag byte for the item-table slot, per zone; empty if none
    int PickupBit);

public sealed record LocationInfo(
    long Id,
    string Name,
    Detect Kind,
    uint[] Addresses,
    int Bit,
    byte Mask,
    int[] Stages,
    string VanillaItem,
    bool IsRelicSpot,         // vanilla item is a relic
    Place Place);

static partial class LocationData
{
    static Dictionary<long, LocationInfo>? _byId;

    public static LocationInfo? Get(long id)
    {
        if (_byId == null)
        {
            var map = new Dictionary<long, LocationInfo>();
            foreach (var loc in All) map[loc.Id] = loc;
            _byId = map;
        }
        return _byId.TryGetValue(id, out var info) ? info : null;
    }
}

public enum ItemType { Usable, Weapon1, Weapon2, Shield, Helmet, Armor, Cloak, Accessory, Relic, Powerup, Event }

public sealed record ItemInfo(long Id, string Name, ItemType Type);

static partial class ItemData
{
    public const long FirstRelic = 300;
    public const long LastRelic = 329;
    public const long HeartVessel = 412;
    public const long LifeVessel = 423;
    public const long SecretBoots = 257;

    static Dictionary<long, ItemInfo>? _byId;

    public static ItemInfo? Get(long id)
    {
        if (_byId == null)
        {
            var map = new Dictionary<long, ItemInfo>();
            foreach (var item in All) map[item.Id] = item;
            _byId = map;
        }
        return _byId.TryGetValue(id, out var info) ? info : null;
    }
}
