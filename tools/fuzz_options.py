"""Generate seeds that between them use every value of every option of this world, and check each one.

Seed k gives option i its value number (k + i) mod (its number of values), so every value of every
option comes up at least once, in varied combinations; more random combinations can be added. Each seed has
the tested slot and a default second slot (so other players' items are in the mix). For each seed:
  - it must generate (a failed check_drop in Rom.py, or any other error, fails it);
  - tools/verify_payload.py: the slot_data payload must equal the world's own writes;
  - every value written into something the game drops must be something it can drop: enemy drop fields
    (every enemy definition in DRA), the per-stage global drop tables, candles (read as params & 0xFFF), the
    two scripted Bone Scimitar drops (must be items), and starting equipment (in range for its slot).
Values are decoded from the world's disc writes (written next to the seed with SOTN_RECOMP_DEBUG_TOKENS).

Usage: ref/archipelago/.venv/Scripts/python.exe tools/fuzz_options.py [random combinations, default 4]
"""
import importlib
import os
import random
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "ref", "archipelago")
GAME = "Symphony of the Night (Recomp)"
sys.path.insert(0, os.path.join(ROOT, "tools"))
from verify_placement import decode_tokens  # noqa: E402

SECTOR, HEADER, DATA = 2352, 24, 0x800
DRA_LBA = 299
ENEMY_DEFS, ENEMY_DEF_SIZE, ENEMY_COUNT = 0x8900, 0x28, 400
DROP_FIELDS = (0x1A, 0x1C)
BONE_SCIMITAR_DROPS = {0x9982, 0x9984}          # read by the recomp's scripted Bone Scimitars: must be items
HAND_ITEMS, BODY_ITEMS = 169, 90
START_GEAR = [(0x11A0D0 + 12 * i, i >= 2) for i in range(6)]  # weapon, shield, helmet, armor, cloak, other


def dra_disc(offset):
    sector, within = divmod(offset, DATA)
    return (DRA_LBA + sector) * SECTOR + HEADER + within


def option_values(cls):
    from Options import Toggle, Choice, Range, NamedRange
    if issubclass(cls, Choice):
        return sorted(cls.name_lookup)
    if issubclass(cls, Toggle):
        return [0, 1]
    if issubclass(cls, Range):
        values = sorted({cls.range_start, cls.default, cls.range_end})
        # Named values ("use_difficulty") as the template writes them, not only numbers.
        if issubclass(cls, NamedRange):
            values += sorted(cls.special_range_names)
        return values
    return None


def yaml_value(cls, value):
    from Options import Toggle, Choice
    if issubclass(cls, Choice):
        return cls.name_lookup[value]
    if issubclass(cls, Toggle):
        return "true" if value else "false"
    return str(value)


def check_drops(tokens, io, zones, rom_offset):
    """Problems with anything the seed makes the game drop."""
    writes = {}
    for offset, data in tokens:
        for i, b in enumerate(data):
            writes[offset + i] = b

    def u16(disc):
        lo, hi = writes.get(disc), writes.get(disc + 1)
        return None if lo is None else lo | (hi if hi is not None else 0) << 8

    prizes = {i["id"] for i in io.io_items if i.get("type") in ("HEART", "GOLD", "SUBWEAPON", "POWERUP")}

    def bad_drop(value, item_only=False):
        if value >= 0x80:
            return value - 0x80 >= HAND_ITEMS + BODY_ITEMS
        return item_only or value not in prizes

    problems = []
    for enemy in range(ENEMY_COUNT):
        for field in DROP_FIELDS:
            offset = ENEMY_DEFS + enemy * ENEMY_DEF_SIZE + field
            value = u16(dra_disc(offset))
            if value is not None and bad_drop(value, offset in BONE_SCIMITAR_DROPS):
                problems.append(f"enemy {enemy} drop +0x{field:X} = 0x{value:X}")
    for item in io.io_items:
        for tile in item.get("tiles", []):
            if "noOffset" in tile:
                continue
            if tile.get("enemy") == "GLOBAL_DROP":
                for address in tile["addresses"]:
                    value = u16(address)
                    if value is not None and bad_drop(value):
                        problems.append(f"global drop at 0x{address:X} = 0x{value:X}")
            if "candle" in tile and tile["zones"][0] != "ST0":
                for i, entity in enumerate(tile["entities"]):
                    name = tile["zones"][1] if i >= 2 else tile["zones"][0]
                    value = u16(rom_offset(zones.zones[zones.ZONE[name]], entity + 0x08))
                    if value is None:
                        continue
                    if value >> 12 != tile["candle"] >> 4 or bad_drop(value & 0xFFF):
                        problems.append(f"candle {name} 0x{entity:X} = 0x{value:X} (candle 0x{tile['candle']:X})")
    for address, body in START_GEAR:
        value = u16(address)
        if value is None:
            continue
        if not value < (BODY_ITEMS if body else HAND_ITEMS):  # body items as their own index (equip_id_offset)
            problems.append(f"starting equipment at 0x{address:X} = {value}")
    return problems


def main():
    extra = int(sys.argv[1]) if len(sys.argv) > 1 else 4
    subprocess.run(["py", "-3.12", os.path.join(ROOT, "tools", "build_apworld.py")], check=True, stdout=subprocess.DEVNULL)
    shutil.copy(os.path.join(ROOT, "dist", "sotn_recomp.apworld"), os.path.join(AP, "custom_worlds"))

    os.chdir(AP)
    sys.path.insert(0, AP)
    from worlds.AutoWorld import AutoWorldRegister
    world_type = AutoWorldRegister.world_types[GAME]
    io = importlib.import_module(world_type.__module__ + ".data.io_items")
    zones = importlib.import_module(world_type.__module__ + ".data.Zones")
    rom_offset = importlib.import_module(world_type.__module__ + ".Rom").rom_offset
    own = world_type.__module__ + ".Options"
    options = [(name, cls, option_values(cls)) for name, cls in world_type.options_dataclass.type_hints.items()
               if cls.__module__ == own and option_values(cls)]

    rng = random.Random(7)
    combos = [{name: values[(k + i) % len(values)] for i, (name, _, values) in enumerate(options)}
              for k in range(max(len(v) for _, _, v in options))]
    combos += [{name: rng.choice(values) for name, _, values in options} for _ in range(extra)]
    print(f"{len(options)} options, {len(combos)} seeds")

    failures = 0
    work = tempfile.mkdtemp(prefix="sotn_fuzz_")
    for k, combo in enumerate(combos):
        players, output = os.path.join(work, f"p{k}"), os.path.join(work, f"o{k}")
        os.makedirs(players)
        os.makedirs(output)
        lines = ["name: Fuzz", f"game: {GAME}", f"{GAME}:"]
        lines += [f"  {name}: {yaml_value(cls, combo[name])}" for name, cls, _ in options]
        open(os.path.join(players, "a.yaml"), "w").write("\n".join(lines) + "\n")
        open(os.path.join(players, "b.yaml"), "w").write(f"name: Other\ngame: {GAME}\n{GAME}: {{}}\n")
        env = dict(os.environ, SOTN_RECOMP_DEBUG_TOKENS="1", PYTHONUNBUFFERED="1")
        run = subprocess.run([sys.executable, "Generate.py", "--skip_prog_balancing", "--player_files_path", players,
                              "--outputpath", output, "--seed", str(1000 + k)], env=env, capture_output=True, text=True,
                             stdin=subprocess.DEVNULL)
        zips = [f for f in os.listdir(output) if f.endswith(".zip")]
        label = ", ".join(f"{n}={yaml_value(c, combo[n])}" for n, c, _ in options)
        if run.returncode != 0 or not zips:
            failures += 1
            # The first exception (Archipelago then waits for Enter, which fails with EOFError).
            lines = (run.stdout + run.stderr).splitlines()
            error = next((line for line in lines if ("Error" in line or "Exception" in line) and "EOFError" not in line), None)
            where = [line.strip() for line in lines if line.strip().startswith("File ") and "sotn_recomp" in line]
            print(f"seed {k}: GENERATION FAILED: {error or run.returncode}" + (f" at {where[-1]}" if where else "")
                  + f"\n  {label}")
            continue
        seed = os.path.join(output, zips[0])
        payload = subprocess.run([sys.executable, os.path.join(ROOT, "tools", "verify_payload.py"), seed],
                                 capture_output=True, text=True)
        problems = [] if payload.returncode == 0 else ["payload differs from the world's writes"]
        with zipfile.ZipFile(seed) as z:
            for name in z.namelist():
                if name.endswith(".sotn_tokens"):
                    problems += [f"{name.split('_P')[1].split('_')[0]}: {p}" for p in check_drops(decode_tokens(z.read(name)), io, zones, rom_offset)]
        if problems:
            failures += 1
            print(f"seed {k}: {len(problems)} problem(s): {problems[:6]}\n  {label}")
        else:
            print(f"seed {k}: ok")
    shutil.rmtree(work, ignore_errors=True)
    print(f"{len(combos) - failures}/{len(combos)} seeds ok")
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
