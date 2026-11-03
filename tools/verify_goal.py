"""Check the goal option's logic in generated worlds (no fill, no game).

For goal defeat_dracula, the world must keep what it always had. For goal defeat_richter, on each option set below:
  - nothing of the inverted castle is in the seed: no location, no region, none of its relics in the pool;
  - the pool fits: one item for each location, each key item of the first castle exactly as often as with the
    default goal (twice where enemysanity adds copies);
  - Richter's event is the goal, and the Holy glasses aren't progression;
  - with every item, Richter and every location can be reached; with nothing, Richter can't;
  - Richter takes exactly a way to fly (and what's on the way to the Keep, which is less than a way to fly);
  - starts in the inverted castle turn into starts in the castle.

Usage: ref/archipelago/.venv/Scripts/python.exe tools/verify_goal.py
"""
import itertools
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "ref", "archipelago")
GAME = "Symphony of the Night (Recomp)"
RICHTER = {"goal": 1}
OPTION_SETS = [
    {},
    {"item_pool": 0},
    {"item_pool": 1},
    {"item_pool": 2},
    {"item_pool": 0, "randomize_other_items": 1},
    {"boss_drops": 0},
    {"powerful_items": 1},
    {"caverns_back_door": 2, "colosseum_back_door": 1},
    {"caverns_back_door": 1, "open_shortcuts": 1},
    {"enemysanity": 1, "difficulty": 0},
    {"enemysanity": 1, "difficulty": 1, "boss_drops": 1},
    {"enemysanity": 1, "difficulty": 2, "enemysanity_needs_faerie_scroll": 1},
    {"enemysanity": 1, "difficulty": 3, "item_pool": 0},
    {"starting_area": 1},
    {"starting_area": 2},
    {"starting_area": 3},
    {"no_logic": 1},
    {"no_logic": 1, "enemysanity": 1, "boss_drops": 1},
]
FLIGHT = [{"Soul of bat"}, {"Form of mist", "Power of mist"}, {"Gravity boots", "Leap stone"}]


def main():
    os.chdir(AP)
    sys.path.insert(0, AP)
    from BaseClasses import CollectionState, ItemClassification
    from test.general import setup_multiworld
    from worlds.AutoWorld import AutoWorldRegister
    from Fill import FillError, distribute_items_restrictive

    world_type = AutoWorldRegister.world_types[GAME]
    package = world_type.__module__
    locations_module = sys.modules[f"{package}.Locations"]
    items_module = sys.modules[f"{package}.Items"]
    inverted = locations_module.INVERTED_LOCATIONS
    inverted_relics = locations_module.INVERTED_RELICS
    richter, dracula = locations_module.DEFEAT_RICHTER, locations_module.KILL_DRACULA

    def build(options):
        return setup_multiworld(world_type, seed=1, options=options)

    def state_with(multiworld, world, names):
        state = CollectionState(multiworld)
        for name in names:
            state.collect(world.create_item(name), prevent_sweep=True)
        return state

    problems = []

    def check(ok, what):
        if not ok:
            problems.append(what)
            print(f"  PROBLEM: {what}")

    for options in OPTION_SETS:
        print(f"{options or 'defaults'}")
        base = build(options)
        made = build({**options, **RICHTER})
        world = made.worlds[1]
        names = {location.name for location in made.get_locations(1)}
        regions = {region.name for region in made.get_regions(1)}
        pool = [item.name for item in made.itempool]
        base_pool = [item.name for item in base.itempool]
        logic = not options.get("no_logic")

        # Dracula's game hasn't changed.
        base_names = {location.name for location in base.get_locations(1)}
        check(dracula in base_names and richter not in base_names, "defeat_dracula has the wrong goal event")
        check(base.get_location(dracula, 1).item.name == "Victory", "defeat_dracula's event isn't Victory")
        check(all(r in base_pool for r in inverted_relics), "defeat_dracula lost relics")

        # Nothing of the inverted castle.
        check(not names & inverted, f"inverted locations in the seed: {sorted(names & inverted)[:5]}")
        check(dracula not in names, "Dracula's event is in the seed")
        check(not regions & {"Reverse Castle Keep", "Reverse Center Cube", "Anti-Chapel", "Cave"},
              "inverted regions in the seed")
        check(not set(pool) & inverted_relics, f"inverted relics in the pool: {sorted(set(pool) & inverted_relics)}")
        check(base_names - inverted - {dracula} == names - {richter}, "first castle locations differ from defeat_dracula's")

        # The pool fits, with the first castle's key items.
        check(len(pool) == len(names) - 1, f"{len(pool)} items for {len(names) - 1} locations")
        copies = 1
        if options.get("enemysanity") and options.get("difficulty") == 0:
            copies = 2
        for relic in set(items_module.relic_table) - inverted_relics:
            check(pool.count(relic) == copies, f"{relic}: {pool.count(relic)} in the pool, expected {copies}")
        for item in ("Spike breaker", "Gold ring", "Silver ring", "Holy glasses"):
            check(pool.count(item) >= 1, f"{item} isn't in the pool")

        # The goal.
        goal = made.get_location(richter, 1)
        check(goal.item is not None and goal.item.name == "Victory" and goal.locked, "Richter's event isn't Victory")
        check(goal.parent_region.name == "Upper Castle Keep", f"Richter is in {goal.parent_region.name}")
        check(world.create_item("Holy glasses").classification & ItemClassification.progression == 0,
              "Holy glasses are progression")

        # Everything can be reached with everything; the goal not with nothing.
        everything = sorted({item.name for item in made.itempool if item.advancement})
        full = state_with(made, world, everything)
        unreachable = [location.name for location in made.get_locations(1) if not location.can_reach(full)]
        check(not unreachable, f"unreachable with every item: {unreachable[:5]}")
        check(made.completion_condition[1](full) or goal.can_reach(full), "the goal can't be reached with every item")
        if logic:
            check(not goal.can_reach(state_with(made, world, [])), "Richter can be reached with nothing")
            # Which sets of the movement items open the way to him: exactly the ways to fly.
            movement = sorted({"Soul of bat", "Form of mist", "Power of mist", "Gravity boots", "Leap stone",
                               "Jewel of open"})
            for size in range(len(movement) + 1):
                for combo in itertools.combinations(movement, size):
                    have = set(combo)
                    can_fly = any(way <= have for way in FLIGHT)
                    reached = goal.can_reach(state_with(made, world, have))
                    check(reached == can_fly, f"with {sorted(have)}: Richter {'reached' if reached else 'not reached'}")
            # The Stopwatch's rooms can't count on an inverted castle.
            alucart = locations_module.ABREV_TO_LOCATION["NO0_Alucart sword_7"]
            if alucart in names:
                glasses_fly = state_with(made, world, ["Holy glasses", "Soul of bat"])
                check(not made.get_location(alucart, 1).can_reach(glasses_fly),
                      "the Alucart rooms count on the inverted castle")

        area = options.get("starting_area", 0)
        check(world.options.starting_area.value == (1 if area in (1, 2, 3) else 0),
              f"starting_area {area} became {world.options.starting_area.value}")

    # The tightest fill there is: a solo key_items seed, where only the Life Vessels can go on the boss drops.
    for goal in (0, 1):
        for seed in range(300):
            try:
                distribute_items_restrictive(setup_multiworld(world_type, seed=seed, options={"item_pool": 0, "goal": goal}))
            except FillError as error:
                check(False, f"solo key_items, goal {goal}, seed {seed}: {str(error).splitlines()[0]}")
    print("solo key_items: 300 seeds of each goal filled")

    print(f"{len(OPTION_SETS)} option sets, {len(problems)} problems")
    sys.exit(1 if problems else 0)


if __name__ == "__main__":
    main()
