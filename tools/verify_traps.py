"""Check the trap options in generated worlds (no game).

For each option set below, with several seeds:
  - traps are on by default; with trap_percentage 0, no traps; otherwise that percentage of the filler is traps, of the chosen types, the pool
    is the same size, and only filler was replaced;
  - the world still fills and can be beaten, its game data can be written (own traps can land on every kind of
    spot on their own in a solo seed), and own traps are placed like other players' items, never as tiles of their own;
  - the logic export doesn't mention them.

Usage: ref/archipelago/.venv/Scripts/python.exe tools/verify_traps.py [seeds per option set, default 10]
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "ref", "archipelago")
GAME = "Symphony of the Night (Recomp)"
OPTION_SETS = [
    {},
    {"trap_percentage": 0},
    {"trap_percentage": 10},
    {"trap_percentage": 100},
    {"trap_percentage": 100, "trap_types": ["Poison"]},
    {"trap_percentage": 50, "trap_types": ["Stone", "Curse"]},
    {"trap_percentage": 100, "trap_types": []},
    {"trap_percentage": 100, "item_pool": 0},
    {"trap_percentage": 100, "boss_drops": 0},
    {"trap_percentage": 100, "item_pool": 2, "boss_drops": 1},
    {"trap_percentage": 100, "boss_drops": 1, "enemysanity": 1},
    {"trap_percentage": 100, "goal": 1, "boss_drops": 1, "enemysanity": 1},
    {"trap_percentage": 100, "randomize_other_items": 1, "powerful_items": 1},
]
TRAPS = {"Poison trap", "Stone trap", "Curse trap"}


def main():
    seeds = int(sys.argv[1]) if len(sys.argv) > 1 else 10
    os.chdir(AP)
    sys.path.insert(0, AP)
    from worlds.AutoWorld import AutoWorldRegister, call_all
    from BaseClasses import ItemClassification
    from Fill import distribute_items_restrictive, balance_multiworld_progression
    from test.general import setup_multiworld

    world_type = AutoWorldRegister.world_types[GAME]
    package = world_type.__module__
    logic = sys.modules[f"{package}.Logic"]
    assert TRAPS <= set(world_type.item_name_to_id)
    default = sys.modules[f"{package}.Options"].TrapPercentage.default

    problems = []

    def check(ok, what):
        if not ok:
            problems.append(what)
            print(f"  PROBLEM: {what}")

    check(default > 0, "traps are off by default")
    for options in OPTION_SETS:
        counts = []
        for seed in range(seeds):
            base_options = {k: v for k, v in options.items() if not k.startswith("trap_")}
            base = setup_multiworld(world_type, seed=seed + 2000, options={**base_options, "trap_percentage": 0})
            made = setup_multiworld(world_type, seed=seed + 2000, options=options)
            world = made.worlds[1]
            base_pool = [item for item in base.itempool]
            pool = [item.name for item in made.itempool]
            traps = [name for name in pool if name in TRAPS]
            counts.append(len(traps))

            check(len(pool) == len(base_pool), f"{options}: pool size changed")
            filler = sum(1 for item in base_pool if item.classification == ItemClassification.filler)
            kinds = options.get("trap_types", ["Poison", "Stone", "Curse"])
            want = filler * options.get("trap_percentage", default) // 100 if kinds else 0
            check(len(traps) == want, f"{options}: {len(traps)} traps, expected {want} of {filler} filler")
            check({name.split()[0] for name in traps} <= set(kinds), f"{options}: wrong kinds {set(traps)}")
            kept = [item.name for item in base_pool if item.classification != ItemClassification.filler]
            for name in set(kept):
                check(pool.count(name) >= kept.count(name), f"{options}: lost {name}")
            check(not any(i in TRAPS for sets in logic.export_logic(made, 1)["locations"].values()
                          for s in sets[1] for i in s), f"{options}: the logic mentions a trap")

            distribute_items_restrictive(made)
            call_all(made, "post_fill")
            balance_multiworld_progression(made)
            check(made.can_beat_game(), f"{options} seed {seed}: can't be beaten")
            world.pre_output()   # writes the game data, placing own traps
            slot_data = world.fill_slot_data()
            check("recomp" in slot_data, f"{options}: no payload")
        print(f"{options}: traps in the pool {min(counts)}-{max(counts)}, {seeds} seeds filled and written")

    print(f"{len(OPTION_SETS)} option sets, {len(problems)} problems")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
