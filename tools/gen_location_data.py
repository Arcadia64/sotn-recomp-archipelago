"""Generate the mod's data tables from the SotN AP world.

Writes:
  mod/LocationData.g.cs - every AP location: how to detect it, and where its item lives in RAM
  mod/ItemData.g.cs     - every AP item: id, name and type
  mod/ZoneData.g.cs     - g_StageId -> the AP world's zone name (sent to the tracker)

Usage: py -3.12 tools/gen_location_data.py [path to the apworld's sotn folder]

The AP world addresses things as offsets into the raw disc image (2352-byte sectors). Stage
and boss overlays all load at 0x80180000, so a disc offset inside a zone's file maps to
0x80180000 + its offset in that file. The AP world numbers castle areas its own way (ZONE);
the game and the recomp use g_StageId. STAGE_IDS maps one to the other and is checked
against the recomp's item-table addresses (Randomizer.cs StageItemListOffset).
"""
import importlib
import math
import os
import re
import sys
import types

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_WORLD = os.path.join(ROOT, "ref", "ap-world", "apworld-b08161", "sotn")
RECOMP_RANDOMIZER = os.path.join(ROOT, "ref", "SymphonyRecomp", "patches", "rando", "Randomizer.cs")
OUT_LOCATIONS = os.path.join(ROOT, "mod", "LocationData.g.cs")
OUT_ITEMS = os.path.join(ROOT, "mod", "ItemData.g.cs")
OUT_ZONES = os.path.join(ROOT, "mod", "ZoneData.g.cs")

RAM = 0x80000000
OVERLAY_BASE = 0x80180000
BESTIARY = 0x8003BF7C
SECTOR = 2352
SECTOR_DATA = 0x800

# AP world zone key -> game g_StageId (names as in the decomp / recomp overlay list).
STAGE_IDS = {
    "NO0": 0x00, "NO1": 0x01, "LIB": 0x02, "CAT": 0x03, "NO2": 0x04, "CHI": 0x05,
    "DAI": 0x06, "NP3": 0x07, "CEN": 0x08, "NO4": 0x09, "ARE": 0x0A, "TOP": 0x0B,
    "NZ0": 0x0C, "NZ1": 0x0D, "WRP": 0x0E, "DRE": 0x12, "BO7": 0x16, "BO6": 0x18,
    "BO5": 0x19, "BO4": 0x1A, "BO3": 0x1B, "BO2": 0x1C, "BO1": 0x1D, "BO0": 0x1E,
    "ST0": 0x1F, "RNO0": 0x20, "RNO1": 0x21, "RLIB": 0x22, "RCAT": 0x23, "RNO2": 0x24,
    "RCHI": 0x25, "RDAI": 0x26, "RNO3": 0x27, "RCEN": 0x28, "RNO4": 0x29, "RARE": 0x2A,
    "RTOP": 0x2B, "RNZ0": 0x2C, "RNZ1": 0x2D, "RWRP": 0x2E, "RBO8": 0x36, "RBO7": 0x37,
    "RBO6": 0x38, "RBO5": 0x39, "RBO4": 0x3A, "RBO3": 0x3B, "RBO2": 0x3C, "RBO1": 0x3D,
    "RBO0": 0x3E, "NO3": 0x41,
}

ITEM_TYPES = {
    "USABLE": "Usable", "WEAPON1": "Weapon1", "WEAPON2": "Weapon2", "SHIELD": "Shield",
    "HELMET": "Helmet", "ARMOR": "Armor", "CLOAK": "Cloak", "ACCESSORY": "Accessory",
    "RELIC": "Relic", "POWERUP": "Powerup", "EVENT": "Event",
}


def load_world(world_dir):
    """Import the world's data modules without Archipelago installed."""
    base = types.ModuleType("BaseClasses")

    class Location:
        pass

    class Item:
        pass

    class ItemClassification:
        filler = 0
        progression = 1
        useful = 2
        trap = 4
        skip_balancing = 8
        progression_skip_balancing = 9

    base.Location = Location
    base.Item = Item
    base.ItemClassification = ItemClassification
    sys.modules["BaseClasses"] = base

    pkg = types.ModuleType("sotn")
    pkg.__path__ = [world_dir]
    sys.modules["sotn"] = pkg
    data = types.ModuleType("sotn.data")
    data.__path__ = [os.path.join(world_dir, "data")]
    sys.modules["sotn.data"] = data

    zones = importlib.import_module("sotn.data.Zones")
    constants = importlib.import_module("sotn.data.Constants")
    locations = importlib.import_module("sotn.Locations")
    items = importlib.import_module("sotn.Items")
    return zones, constants, locations, items


class DiscMap:
    """Maps a raw disc-image offset to (stage id, RAM address) for stage and boss overlays."""

    def __init__(self, zones_mod):
        by_key = {v: k for k, v in zones_mod.ZONE.items()}
        self.spans = []
        for num, zone in zones_mod.zones.items():
            key = by_key[num]
            if "pos" not in zone or "len" not in zone or key not in STAGE_IDS:
                continue
            span = math.ceil(zone["len"] / SECTOR_DATA) * SECTOR
            self.spans.append((zone["pos"], zone["pos"] + span, STAGE_IDS[key], key))

    def to_ram(self, offset):
        for start, end, stage, key in self.spans:
            if start <= offset < end:
                rel = offset - start
                sector, within = divmod(rel, SECTOR)
                if within >= SECTOR_DATA:
                    sys.exit(f"disc offset 0x{offset:X} in {key} falls in a sector's error-correction bytes")
                return stage, OVERLAY_BASE + sector * SECTOR_DATA + within
        return None


def check_stage_ids(zones_mod):
    if not os.path.exists(RECOMP_RANDOMIZER):
        print("recomp not found, skipping stage id check")
        return
    src = open(RECOMP_RANDOMIZER, encoding="utf-8").read()
    body = re.search(r"StageItemListOffset\s*=\s*\{(.*?)\};", src, re.S).group(1)
    offsets = [int(v, 0) for v in re.findall(r"0x[0-9A-Fa-f]+|\b\d+\b", body)]
    by_key = {v: k for k, v in zones_mod.ZONE.items()}
    checked = 0
    for zone_num, zone in zones_mod.zones.items():
        key = by_key[zone_num]
        items = zone.get("items")
        stage = STAGE_IDS.get(key)
        if items is None or stage is None or stage >= len(offsets) or offsets[stage] == 0:
            continue
        expected = OVERLAY_BASE + items
        if offsets[stage] != expected:
            sys.exit(f"stage id mismatch for {key}: recomp has 0x{offsets[stage]:08X} at stage 0x{stage:02X}, "
                     f"AP world items table is 0x{expected:08X}")
        checked += 1
    print(f"stage ids agree with the recomp for {checked} zones")


def cs_string(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def cs_addrs(pairs):
    return "[" + ", ".join(f"new(0x{s:02X}, 0x{a:08X})" for s, a in pairs) + "]"


def cs_xy(opts):
    opts = opts or {}
    return f"{opts.get('x', -1)}, {opts.get('y', -1)}"


def detection(name, loc, zones_mod):
    vanilla = loc.get("vanilla_item", "")
    zone_nums = loc.get("zones", [])
    # Same order of checks as the BizHawk client (client.py 392-493).
    if "bin_addresses" in loc or vanilla == "Holy glasses":
        return "Break", [RAM + loc["break_flag"]], 0, loc["break_mask"]
    if "kill_time" in loc:
        return "KillTime", [RAM + loc["kill_time"]], 0, 0
    if vanilla == "Jewel of open":
        return "Librarian", [], 0, 0
    if loc.get("enemy"):
        gid = loc["game_id"] - 1
        return "Enemy", [BESTIARY + gid // 8], gid % 8, 0
    # Stage pickup bit, little-endian across loot_size bytes, in every zone the location is listed for.
    index = loc["index"]
    addrs = []
    for z in zone_nums:
        flag = zones_mod.zones[z].get("loot_flag")
        if flag is not None and RAM + flag + index // 8 not in addrs:
            addrs.append(RAM + flag + index // 8)
    if not addrs:
        sys.exit(f"{name}: no loot flag for its zones")
    return "Loot", addrs, index % 8, 0


def placement(name, loc, zones_mod, disc):
    by_key = {v: k for k, v in zones_mod.ZONE.items()}
    zone_nums = loc.get("zones", [])

    def stage_of(z):
        return STAGE_IDS[by_key[z]]

    def mapped(offset):
        r = disc.to_ram(offset)
        if r is None:
            sys.exit(f"{name}: disc offset 0x{offset:X} is not inside a known stage file")
        return r

    index = loc.get("index", -1)
    item_table = []
    if index >= 0:
        for z in zone_nums:
            zone = zones_mod.zones[z]
            if "items" in zone:
                item_table.append((stage_of(z), OVERLAY_BASE + zone["items"] + 2 * index))

    entities = []
    for i, e in enumerate(loc.get("entities", [])):
        entities.append((stage_of(zone_nums[i >> 1]), OVERLAY_BASE + e))

    addresses = [mapped(a) for a in loc.get("addresses", [])]
    boss = [mapped(loc["bin_address"])] if loc.get("boss") else []

    reward = []
    if "reward" in loc:
        rz = loc["reward"]["zones"]
        reward.append((stage_of(rz), OVERLAY_BASE + zones_mod.zones[rz]["rewards"] + 2 * loc["reward"]["index"]))

    ring_ids = []
    for group in loc.get("ids", []):
        ring_ids += [mapped(a) for a in group["addresses"]]

    return (f"new({index}, {cs_addrs(item_table)}, {cs_addrs(entities)}, {cs_xy(loc.get('as_relic'))}, "
            f"{cs_xy(loc.get('as_item'))}, {'true' if loc.get('no_offset') else 'false'}, {cs_addrs(addresses)}, "
            f"{cs_addrs(boss)}, {cs_addrs(reward)}, {cs_addrs(ring_ids)}, {'true' if loc.get('trio') else 'false'})")


def main():
    world_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_WORLD
    zones_mod, constants, loc_mod, items_mod = load_world(world_dir)
    check_stage_ids(zones_mod)
    disc = DiscMap(zones_mod)
    relic_names = set(constants.RELIC_NAMES)

    rows = []
    for name, loc in loc_mod.locations.items():
        ap_id = loc.get("ap_id")
        if ap_id is None:
            continue  # event location (Kill Dracula)
        stages = [STAGE_IDS[k] for k in (loc_mod_key(zones_mod, z) for z in loc.get("zones", [])) if k in STAGE_IDS]
        kind, addrs, bit, mask = detection(name, loc, zones_mod)
        vanilla = loc.get("vanilla_item", "")
        rows.append((ap_id, name, kind, addrs, bit, mask, stages, vanilla, vanilla in relic_names,
                     placement(name, loc, zones_mod, disc)))

    rows.sort(key=lambda r: r[0])
    lines = [
        "// Generated by tools/gen_location_data.py from the SotN AP world. Do not edit by hand.",
        f"// Source: {os.path.relpath(world_dir, ROOT).replace(os.sep, '/')}",
        "namespace SotnArchipelago;",
        "",
        "static partial class LocationData",
        "{",
        "    public static readonly LocationInfo[] All =",
        "    [",
    ]
    for ap_id, name, kind, addrs, bit, mask, stages, vanilla, is_relic, place in rows:
        a = ", ".join(f"0x{x:08X}" for x in addrs)
        s = ", ".join(f"0x{x:02X}" for x in stages)
        lines.append(f"        new({ap_id}, {cs_string(name)}, Detect.{kind}, [{a}], {bit}, 0x{mask:02X}, [{s}], "
                     f"{cs_string(vanilla)}, {'true' if is_relic else 'false'},")
        lines.append(f"            {place}),")
    lines += ["    ];", "}", ""]
    write(OUT_LOCATIONS, lines)

    item_lines = [
        "// Generated by tools/gen_location_data.py from the SotN AP world. Do not edit by hand.",
        f"// Source: {os.path.relpath(world_dir, ROOT).replace(os.sep, '/')}",
        "namespace SotnArchipelago;",
        "",
        "static partial class ItemData",
        "{",
        "    public static readonly ItemInfo[] All =",
        "    [",
    ]
    for item_name, item in sorted(items_mod.items.items(), key=lambda kv: kv[1]["id"]):
        item_lines.append(f"        new({item['id']}, {cs_string(item_name)}, ItemType.{ITEM_TYPES[item['type']]}),")
    item_lines += ["    ];", "}", ""]
    write(OUT_ITEMS, item_lines)

    by_key = {v: k for k, v in zones_mod.ZONE.items()}
    zone_lines = [
        "// Generated by tools/gen_location_data.py from the SotN AP world. Do not edit by hand.",
        f"// Source: {os.path.relpath(world_dir, ROOT).replace(os.sep, '/')}",
        "using System.Collections.Generic;",
        "",
        "namespace SotnArchipelago;",
        "",
        "static class ZoneData",
        "{",
        "    // g_StageId -> zone name as the AP world and its tracker know it",
        "    public static readonly Dictionary<int, string> Names = new()",
        "    {",
    ]
    for num, zone in sorted(zones_mod.zones.items(), key=lambda kv: STAGE_IDS.get(by_key[kv[0]], 999)):
        key = by_key[num]
        if key in STAGE_IDS:
            zone_lines.append(f"        [0x{STAGE_IDS[key]:02X}] = {cs_string(zone['name'])}, // {key}")
    zone_lines += ["    };", "}", ""]
    write(OUT_ZONES, zone_lines)

    counts = {}
    for r in rows:
        counts[r[2]] = counts.get(r[2], 0) + 1
    print(f"wrote {len(rows)} locations ({counts}) and {len(items_mod.items)} items")


def loc_mod_key(zones_mod, z):
    return {v: k for k, v in zones_mod.ZONE.items()}[z]


def write(path, lines):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


if __name__ == "__main__":
    main()
