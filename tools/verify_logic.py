"""Check the world's logic export (apworld/sotn_recomp/Logic.py) against Archipelago's own reachability.

For several option sets it builds the world as generation does, exports the logic, then for random sets of
the items the logic mentions compares, for every location, Archipelago's location.can_reach(state) with a
plain search over the export (what the mod's map does). Installs a fresh build of the world first.

Usage: ref/archipelago/.venv/Scripts/python.exe tools/verify_logic.py [states per option set]
"""
import os
import random
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "ref", "archipelago")
GAME = "Symphony of the Night (Recomp)"

OPTION_SETS = [
    {},
    {"open_no4": 2, "open_are": 1},
    {"open_no4": 1, "item_pool": 3, "boss_locations": 1},
    {"enemysanity": 1, "enemy_scroll": 1, "boss_locations": 1, "item_pool": 3},
    {"difficult": 0, "unlocked_mode": 1},
    {"no_logic": 1},
]


def reachable(export, have):
    ok = lambda sets: any(all(i in have for i in s) for s in sets)
    regions = {export["start"]}
    changed = True
    while changed:
        changed = False
        for region in list(regions):
            for target, sets in export["regions"].get(region, []):
                if target not in regions and ok(sets):
                    regions.add(target)
                    changed = True
    return {int(loc) for loc, (region, sets) in export["locations"].items() if region in regions and ok(sets)}


def main():
    states = int(sys.argv[1]) if len(sys.argv) > 1 else 300
    subprocess.run(["py", "-3.12", os.path.join(ROOT, "tools", "build_apworld.py")],
                   check=True, stdout=subprocess.DEVNULL)
    shutil.copy(os.path.join(ROOT, "dist", "sotn_recomp.apworld"), os.path.join(AP, "custom_worlds"))

    os.chdir(AP)
    sys.path.insert(0, AP)
    from worlds.AutoWorld import AutoWorldRegister
    from BaseClasses import CollectionState
    from test.general import setup_multiworld
    world_type = AutoWorldRegister.world_types[GAME]
    logic_module = sys.modules[world_type.__module__ + ".Logic"]

    rng = random.Random(1)
    failures = 0
    for options in OPTION_SETS:
        multiworld = setup_multiworld(world_type, seed=rng.randrange(1 << 30), options=options)
        world = multiworld.worlds[1]
        export = logic_module.export_logic(multiworld, 1)
        items = sorted({i for _, sets in export["locations"].values() for s in sets for i in s}
                       | {i for edges in export["regions"].values() for _, sets in edges for s in sets for i in s})
        locations = [loc for loc in multiworld.get_locations(1) if loc.address is not None]
        bad = 0
        for n in range(states):
            p = rng.choice([0.2, 0.5, 0.8])
            have = {i for i in items if rng.random() < p}
            state = CollectionState(multiworld)
            for name in have:
                state.collect(world.create_item(name), prevent_sweep=True)
            expected = {loc.address for loc in locations if loc.can_reach(state)}
            got = reachable(export, have)
            if expected != got:
                bad += 1
                if bad <= 3:
                    print(f"  mismatch with {sorted(have)}: AP only {sorted(expected - got)[:8]}, export only {sorted(got - expected)[:8]}")
        rules = len(export["locations"]) + sum(len(e) for e in export["regions"].values())
        print(f"{options or 'defaults'}: {len(export['regions'])} regions, {len(export['locations'])} locations, "
              f"{rules} rules over {len(items)} items; {states} random states, {bad} mismatches")
        failures += bad
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
