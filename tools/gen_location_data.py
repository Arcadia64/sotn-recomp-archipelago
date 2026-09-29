"""Generate the mod's data tables from the SotN AP world.

Writes:
  mod/LocationData.g.cs - every AP location: how to detect it, and where its item lives in RAM
  mod/ItemData.g.cs     - every AP item: id, name and type
  mod/ZoneData.g.cs     - g_StageId -> the AP world's zone name (sent to the tracker)
  mod/SpecialData.g.cs  - extra addresses the special-spot ports need (erase instructions, zone tables)

Usage: py -3.12 tools/gen_location_data.py [path to the apworld's sotn folder]

The AP world addresses things as offsets into the raw disc image (2352-byte sectors). Stage
and boss overlays all load at 0x80180000, so a disc offset inside a zone's file maps to
0x80180000 + its offset in that file. The AP world numbers castle areas its own way (ZONE);
the game and the recomp use g_StageId. STAGE_IDS maps one to the other and is checked
against the recomp's item-table addresses (Randomizer.cs StageItemListOffset).
"""
import glob
import importlib
import math
import os
import re
import sys
import types

import map_positions

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_WORLD = os.path.join(ROOT, "ref", "ap-world", "apworld-b08161", "sotn")
RECOMP_RANDOMIZER = os.path.join(ROOT, "ref", "SymphonyRecomp", "patches", "rando", "Randomizer.cs")
OUT_LOCATIONS = os.path.join(ROOT, "mod", "LocationData.g.cs")
OUT_ITEMS = os.path.join(ROOT, "mod", "ItemData.g.cs")
OUT_ZONES = os.path.join(ROOT, "mod", "ZoneData.g.cs")
OUT_SPECIAL = os.path.join(ROOT, "mod", "SpecialData.g.cs")
OUT_OPTIONS = os.path.join(ROOT, "mod", "OptionData.g.cs")
OUT_MAP = os.path.join(ROOT, "mod", "MapData.g.cs")
GENERATED = os.path.join(ROOT, "ref", "SymphonyRecomp", "generated")  # the recomp's recompiled code
DISC = os.path.join(ROOT, "ref", "SymphonyRecomp", "disc", "Castlevania - Symphony of the Night (USA) (Track 1).bin")

# Raw disc offsets the AP world's Rom.py writes to directly (not via rom_offset), mapped to RAM.
SPECIAL_BIN = {
    "RingOfVladEntityId": 0x059ee2c8,    # replace_ring_of_vlad_with_item: ori v0, r0, 0x000c
    "RingOfVladUpdateLow": 0x059ee2d4,   # addiu v0, v0, 0x3a54 (recomp checks this word)
    "RingOfVladIndex": 0x059ee2e4,       # item table index
    "JewelRelicOffset": 0x047dbde0,      # replace_shop_relic_with_relic: relic id + 0x64
    "JewelName": 0x047d5650,             # shop menu name, 16 bytes
}

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


# Stage files Rom.py writes to that aren't AP zones: key -> (LBA, size, g_StageId).
EXTRA_STAGE_FILES = {
    "MAR": (45043, 110448, 0x17),  # /BOSS/MAR/MAR.BIN, the Clock Room cutscene
}


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
        for key, (lba, size, stage) in EXTRA_STAGE_FILES.items():
            pos = lba * SECTOR + 24
            self.spans.append((pos, pos + math.ceil(size / SECTOR_DATA) * SECTOR, stage, key))

    def contains(self, offset):
        return any(start <= offset < end for start, end, _, _ in self.spans)

    def key_of(self, stage):
        # NO3 and NP3 are both Castle Entrance; the stage id tells them apart.
        for _, _, s, key in self.spans:
            if s == stage:
                return key
        return None

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

    # The stage pickup flag for the location's item-table slot (set when an item pickup there is
    # collected), in every zone the location is listed for. Also for spots detected another way
    # (boss kills), whose converted pickups use the same slot.
    pickup_flags = []
    if index >= 0:
        for z in zone_nums:
            flag = zones_mod.zones[z].get("loot_flag")
            if flag is not None and RAM + flag + index // 8 not in pickup_flags:
                pickup_flags.append(RAM + flag + index // 8)
    flags = "[" + ", ".join(f"0x{a:08X}" for a in pickup_flags) + "]"

    return (f"new({index}, {cs_addrs(item_table)}, {cs_addrs(entities)}, {cs_xy(loc.get('as_relic'))}, "
            f"{cs_xy(loc.get('as_item'))}, {'true' if loc.get('no_offset') else 'false'}, {cs_addrs(addresses)}, "
            f"{cs_addrs(boss)}, {cs_addrs(reward)}, {cs_addrs(ring_ids)}, {'true' if loc.get('trio') else 'false'}, "
            f"{flags}, {index % 8 if index >= 0 else 0})")


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
    lines += ["    ];", ""]

    # Locations.py "despawn": the item falls out (of a wall, a vase) and vanishes if not picked up in time.
    despawn = sorted(r for r in rows if loc_mod.locations[r[1]].get("despawn"))
    lines += ["    // Spots whose item falls out and would vanish if not picked up in time (Locations.py \"despawn\")",
              "    public static readonly long[] Despawn = [" + ", ".join(str(r[0]) for r in despawn) + "];",
              "}", ""]
    despawn_stages = {stage for r in despawn for stage in r[6]}
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

    write_special(zones_mod, loc_mod, disc, world_dir)
    write_options(constants, disc, world_dir, despawn_stages, prize_tables(zones_mod))
    write_map(zones_mod, loc_mod, world_dir)

    counts = {}
    for r in rows:
        counts[r[2]] = counts.get(r[2], 0) + 1
    print(f"wrote {len(rows)} locations ({counts}) and {len(items_mod.items)} items")


class RecompCode:
    """Function names in the recomp's generated code (ref/SymphonyRecomp/generated/<overlay>.cs)."""
    DEF = re.compile(r"public static void (\w+)\(CpuContext c, IMemory m\)")

    def __init__(self):
        self._cache = {}

    def _load(self, overlay):
        if overlay not in self._cache:
            path = os.path.join(GENERATED, f"{overlay}.cs")
            if not os.path.exists(path):
                sys.exit(f"{path} missing: build the recomp first (see PLAN.md)")
            self._cache[overlay] = open(path, encoding="utf-8").read().splitlines()
        return self._cache[overlay]

    def names(self, overlay):
        return [m.group(1) for line in self._load(overlay) if (m := self.DEF.search(line))]

    def functions(self, overlay):
        """{function start address: name} from the overlay's dispatch table."""
        return {int(m.group(1), 16): m.group(2) for line in self._load(overlay) if (m := self.ENTRY.search(line))}

    def bodies(self, overlay):
        """{function name: its source up to the next function}, for the whole overlay."""
        key = ("bodies", overlay)
        if key not in self._cache:
            out, name, start = {}, None, 0
            lines = self._load(overlay)
            for i, line in enumerate(lines):
                if m := self.DEF.search(line):
                    if name:
                        out[name] = "\n".join(lines[start:i])
                    name, start = m.group(1), i
            if name:
                out[name] = "\n".join(lines[start:])
            self._cache[key] = out
        return self._cache[key]

    def hookable_with(self, overlay, needles):
        """Hookable functions whose code (or their _Impl's) contains every needle."""
        bodies = self.bodies(overlay)
        return [n for n in self.names(overlay) if not n.endswith("_Impl")
                and all(s in bodies.get(f"{n}_Impl", bodies[n]) for s in needles)]

    ENTRY = re.compile(r"\[0x([0-9A-F]{8})u\] = SoTN\.(\w+),")

    def function_at(self, overlay, addr):
        """(hookable function containing the instruction at addr, whether the recomp replaced it by hand).
        Found from the overlay's dispatch table (function start addresses): the function with the
        highest start at or below addr. Replaced functions are wrappers without address comments."""
        lines = self._load(overlay)
        starts = sorted((int(m.group(1), 16), m.group(2)) for line in lines if (m := self.ENTRY.search(line)))
        name = None
        for start, fn in starts:
            if start > addr:
                break
            name = fn
        if name is None:
            return None, False
        tag = f"/* 0x{addr:08X} "
        recompiled = any(tag in line for line in lines)
        return name, not recompiled


def disc_byte(offset):
    with open(DISC, "rb") as f:
        f.seek(offset)
        return f.read(1)[0]


# A stage's prize table: what an enemy drops when it drops neither of its items (hearts, gold...), indexed
# by the drop roll. Every stage has the same 32 entries; randomize_drop's "global" options change them.
VANILLA_PRIZE_TABLE = bytes().join(v.to_bytes(2, "little") for v in (
    0x03, 0x00, 0x02, 0x03, 0x03, 0x03, 0x03, 0x03, 0x03, 0x04, 0x04, 0x04, 0x04, 0x05, 0x05, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x02, 0x06, 0x07, 0xC6))


def prize_tables(zones_mod):
    """{g_StageId: RAM address of the stage's prize table}, found by its vanilla contents."""
    tables = {}
    for key, stage in STAGE_IDS.items():
        zone = zones_mod.zones.get(zones_mod.ZONE.get(key))
        if not zone or "pos" not in zone or "len" not in zone:
            continue
        data = map_positions.StageFile(DISC, zone).data
        at = data.find(VANILLA_PRIZE_TABLE)
        if at >= 0 and at % 2 == 0 and data.find(VANILLA_PRIZE_TABLE, at + 1) < 0:
            tables[stage] = OVERLAY_BASE + at
    return tables


def recomp_patch_reads():
    """RAM addresses the recomp's hand-written patches (ref/SymphonyRecomp/patches) mention: where its
    rewrites read the patched bytes back, as `m.ReadU8(0x801ACA08)`."""
    found = set()
    for path in glob.glob(os.path.join(ROOT, "ref", "SymphonyRecomp", "patches", "**", "*.cs"), recursive=True):
        for m in re.finditer(r"0x(8[0-9A-Fa-f]{7})", open(path, encoding="utf-8", errors="ignore").read()):
            found.add(int(m.group(1), 16))
    return found


def write_options(constants, disc, world_dir, despawn_stages, prize_table_addrs):
    code = RecompCode()
    lines = [
        "// Generated by tools/gen_location_data.py from the SotN AP world and the recomp's code. Do not edit by hand.",
        f"// Source: {os.path.relpath(world_dir, ROOT).replace(os.sep, '/')}",
        "using System.Collections.Generic;",
        "using RecompOne.Runtime.Context;",
        "using RecompOne.Runtime.Memory;",
        "using RecompOne.Runtime.Modding;",
        "",
        "namespace SotnArchipelago;",
        "",
        "// RecompReads: the recomp's own rewrite of the function reads this site back (it plays the seed's song",
        "// already); the mod leaves those alone, or it would remap them a second time.",
        "public readonly record struct MusicSite(int Stage, uint Addr, byte Vanilla, bool RecompReads);",
        "",
        "static class OptionData",
        "{",
        "    // enemy_stats: the HitDetection instruction Rom.py turns into 'li v0,3' (Faerie scroll owned), per stage",
        "    public static readonly Dictionary<int, uint> FaerieForceSites = new()",
        "    {",
    ]
    faerie = {}
    for off in constants.faerie_scroll_force_addresses:
        stage, ram = disc.to_ram(off)
        faerie[stage] = ram
    for stage, ram in sorted(faerie.items()):
        lines.append(f"        [0x{stage:02X}] = 0x{ram:08X}, // {disc.key_of(stage)}")
    lines += ["    };", "",
              "    // random_music: song request immediates ('ori vX,zero,0x3NN') in boss and cutscene code, with the",
              "    // song they request on the disc",
              "    public static readonly MusicSite[] MusicSites =", "    ["]
    music = []
    for area, addrs in constants.music_by_area.items():
        for off in addrs:
            if not disc.contains(off):
                continue  # DRA stage music table: data, applied as it is
            stage, ram = disc.to_ram(off)
            music.append((stage, ram, disc_byte(off), area))
    read_by_recomp = recomp_patch_reads()
    for stage, ram, vanilla, area in sorted(music):
        reads = ram in read_by_recomp
        lines.append(f"        new(0x{stage:02X}, 0x{ram:08X}, 0x{vanilla:02X}, {'true' if reads else 'false'}), // {area}")
    lines += ["    ];", "",
              "    // Each stage's prize table (32 u16: what an enemy drops when not one of its items), for the extended",
              "    // widescreen copy of HitDetection, which has the vanilla table built in (OptionHooks.SyncPrizeTable)",
              "    public static readonly Dictionary<int, uint> PrizeTables = new()", "    {"]
    for stage, ram in sorted(prize_table_addrs.items()):
        lines.append(f"        [0x{stage:02X}] = 0x{ram:08X},")
    lines += ["    };", "}", "",
              "// Hooks for the option fixes in OptionHooks.cs, one pair per overlay function.",
              "static class OptionHookSites", "{"]

    for stage in sorted(faerie):
        overlay = disc.key_of(stage).lower()
        name = next((n for n in code.names(overlay) if "HitDetection" in n and not n.endswith("_Impl")), None)
        if name is None:
            sys.exit(f"no HitDetection function found in {overlay}.cs")
        tag = f"Faerie_{overlay}"
        lines += [f'    [PreHook("{overlay}", "{name}")] static void {tag}In(CpuContext c, IMemory m) => OptionHooks.FaerieIn(m);',
                  f'    [PostHook("{overlay}", "{name}")] static void {tag}Out(CpuContext c, IMemory m) => OptionHooks.FaerieOut(m);']

    functions = set()
    replaced = []
    # Drops from despawn spots (DespawnDrops.cs): the item-drop entity of each stage those spots are in,
    # found by what it does (not every stage names it): its resting step counts a timer at +0x80 down from
    # 0xF0 (0x50 for the blink after) unless params (+0x30) has bit 15, and it loads the item's icon.
    drop_code = ("m.ReadU16((c.S0 + 0x30u))", "c.V0 & 0x8000u", "m.WriteU8((c.S0 + 0x80u), (byte)c.V0)",
                 "0u | 0x00F0u", "0u | 0x0050u", "m.WriteU8((c.S0 + 0x80u), (byte)c.V1)")
    for stage in sorted(despawn_stages):
        overlay = disc.key_of(stage).lower()
        found = code.hookable_with(overlay, drop_code)
        if len(found) != 1:
            sys.exit(f"{overlay}: expected one item-drop entity, found {found}")
        lines.append(f'    [PreHook("{overlay}", "{found[0]}")] static void Drop_{overlay}(CpuContext c, IMemory m) => '
                     f'DespawnDrops.Keep(c, m);')

    for stage, ram, _, area in music:
        if ram in read_by_recomp:
            continue  # the recomp's rewrite plays the seed's song itself
        overlay = disc.key_of(stage).lower()
        fn, by_hand = code.function_at(overlay, ram)
        if fn is None:
            sys.exit(f"no function contains music site {overlay} 0x{ram:08X}")
        if by_hand:
            replaced.append(f"{overlay} {fn}")
        functions.add((overlay, fn))
    skipped = sorted(set(replaced))
    for overlay, fn in sorted(functions):
        tag = f"Music_{overlay}_{fn}"
        lines += [f'    [PreHook("{overlay}", "{fn}")] static void {tag}In(CpuContext c, IMemory m) => OptionHooks.MusicIn(m);',
                  f'    [PostHook("{overlay}", "{fn}")] static void {tag}Out(CpuContext c, IMemory m) => OptionHooks.MusicOut(m);']
    lines += ["}", ""]
    write(OUT_OPTIONS, lines)
    print(f"options: {len(faerie)} Faerie scroll sites, {len(music)} music sites in {len(functions)} functions, "
          f"{len(prize_table_addrs)} prize tables"
          + (f"; in functions the recomp rewrote: {', '.join(skipped)}" if skipped else ""))


def write_map(zones_mod, loc_mod, world_dir):
    code = RecompCode()
    cells = map_positions.location_cells(zones_mod, loc_mod, STAGE_IDS, DISC, code.functions)
    lines = [
        "// Generated by tools/gen_location_data.py (tools/map_positions.py) from the SotN AP world and the game's",
        "// stage data. Do not edit by hand.",
        f"// Source: {os.path.relpath(world_dir, ROOT).replace(os.sep, '/')}",
        "using System.Collections.Generic;",
        "",
        "namespace SotnArchipelago;",
        "",
        "static class MapData",
        "{",
        "    // Each location's cell on the 64x64 castle map (the pause-screen map's grid, one cell per screen) and",
        "    // the stage it's in (g_StageId; bit 0x20 = the reverse castle, which has its own map). Enemysanity",
        "    // locations have none.",
        "    public static readonly Dictionary<long, (byte X, byte Y, byte Stage)> Cells = new()",
        "    {",
    ]
    for ap_id, (x, y, stage) in sorted(cells.items()):
        lines.append(f"        [{ap_id}] = ({x}, {y}, 0x{stage:02X}),")
    lines += ["    };", "}", ""]
    write(OUT_MAP, lines)
    print(f"map: {len(cells)} locations placed")


def write_special(zones_mod, loc_mod, disc, world_dir):
    by_key = {v: k for k, v in zones_mod.ZONE.items()}
    lines = [
        "// Generated by tools/gen_location_data.py from the SotN AP world. Do not edit by hand.",
        f"// Source: {os.path.relpath(world_dir, ROOT).replace(os.sep, '/')}",
        "using System.Collections.Generic;",
        "",
        "namespace SotnArchipelago;",
        "",
        "static class SpecialData",
        "{",
        "    // AP zone key -> (g_StageId, item table offset, boss rewards offset) in the stage file; -1 = none",
        "    public static readonly Dictionary<string, (int Stage, int Items, int Rewards)> Zones = new()",
        "    {",
    ]
    for num, zone in zones_mod.zones.items():
        key = by_key[num]
        if key in STAGE_IDS:
            lines.append(f"        [{cs_string(key)}] = (0x{STAGE_IDS[key]:02X}, {hex(zone['items']) if 'items' in zone else -1}, "
                         f"{hex(zone['rewards']) if 'rewards' in zone else -1}),")
    lines += ["    };", "",
              "    // Fixed patch addresses Rom.py writes as raw disc offsets", ]
    for name, off in SPECIAL_BIN.items():
        stage, ram = disc.to_ram(off)
        lines.append(f"    public static readonly StageAddr {name} = new(0x{stage:02X}, 0x{ram:08X});")
    lines += ["", "    // Per location: the instructions Rom.py overwrites to stop a relic from loading (\"erase\")",
              "    public static readonly Dictionary<long, (StageAddr At, uint Instruction)[]> Erase = new()", "    {"]
    for name, loc in sorted(loc_mod.locations.items(), key=lambda kv: kv[1].get("ap_id") or 0):
        if "erase" not in loc or loc.get("ap_id") is None:
            continue
        parts = []
        for ins in loc["erase"]["instructions"]:
            for a in ins["addresses"]:
                stage, ram = disc.to_ram(a)
                parts.append(f"(new(0x{stage:02X}, 0x{ram:08X}), 0x{ins['instruction']:08X})")
        lines.append(f"        [{loc['ap_id']}] = [{', '.join(parts)}], // {name}")
    lines += ["    };", "}", ""]
    write(OUT_SPECIAL, lines)


def loc_mod_key(zones_mod, z):
    return {v: k for k, v in zones_mod.ZONE.items()}[z]


def write(path, lines):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


if __name__ == "__main__":
    main()
