"""This slot's logic as data, for the mod's map: which items open each region connection and location.

The rules in Rules.py and Regions.py are Python lambdas that only ever ask state.has(item, player) (no
counts, no negation, no other regions), so each one is a monotone function of a few items: having more
never hurts. Each is written out as its minimal item sets, e.g. [["Soul of bat"], ["Gravity boots",
"Leap stone"]] = bat, or boots and leap stone; [[]] = always; [] = never. The mod then finds what's
reachable with a plain search from the start region, so its map follows exactly the rules this world
generated with, options included, without a second copy of them to keep in sync. As in Archipelago,
only progression items count: sets that need anything else are left out.

    {"start": "Menu",
     "regions": {"<region>": [["<connected region>", sets], ...]},
     "locations": {"<location id>": ["<region>", sets]}}

tools/verify_logic.py checks the export against Archipelago's own reachability on random item sets.
"""
from itertools import combinations
from typing import Callable, Dict, FrozenSet, List, Set

from BaseClasses import MultiWorld

# More items than this in one rule would mean the export needs another approach; fail loudly.
MAX_ITEMS_PER_RULE = 16


class _Probe:
    """Stands in for CollectionState: answers has() from a fixed item set and records what was asked."""

    def __init__(self, player: int, have: FrozenSet[str]):
        self.player = player
        self.have = have
        self.asked: Set[str] = set()

    def has(self, item: str, player: int, count: int = 1) -> bool:
        if count != 1 or player != self.player:
            raise ValueError(f"rule asks has({item!r}, {player}, {count}); the logic export only handles has(item)")
        self.asked.add(item)
        return item in self.have

    def __getattr__(self, name):
        raise AttributeError(f"rule uses state.{name}; the logic export only handles state.has")


def minimal_sets(rule: Callable, player: int) -> List[List[str]]:
    """The item sets that satisfy the rule with nothing left over, smallest first."""
    def run(have):
        probe = _Probe(player, frozenset(have))
        result = bool(rule(probe))
        return result, probe.asked

    # Find every item the rule can ask about: evaluate under every combination of the items seen so far
    # until no new ones show up (short-circuiting hides items behind others).
    items: Set[str] = set()
    while True:
        seen = set(items)
        for size in range(len(items) + 1):
            for combo in combinations(sorted(items), size):
                seen |= run(combo)[1]
        if seen == items:
            break
        items = seen
        if len(items) > MAX_ITEMS_PER_RULE:
            raise ValueError(f"rule depends on {len(items)} items: {sorted(items)}")

    found: List[FrozenSet[str]] = []
    for size in range(len(items) + 1):
        for combo in combinations(sorted(items), size):
            if any(f <= set(combo) for f in found):
                continue
            if run(combo)[0]:
                found.append(frozenset(combo))
    return [sorted(f) for f in found]


def export_logic(multiworld: MultiWorld, player: int) -> Dict:
    world = multiworld.worlds[player]
    cache: Dict[int, List[List[str]]] = {}
    progression: Dict[str, bool] = {}

    def is_progression(name: str) -> bool:
        # Archipelago only counts progression items in a state, so a rule's other items never help
        # (Power of wolf, for one). Per slot: Faerie scroll is progression only with enemy_scroll.
        if name not in progression:
            progression[name] = world.create_item(name).advancement
        return progression[name]

    def sets(rule):
        key = id(rule)
        if key not in cache:
            cache[key] = [s for s in minimal_sets(rule, player) if all(is_progression(i) for i in s)]
        return cache[key]

    regions = {}
    locations = {}
    for region in multiworld.get_regions(player):
        regions[region.name] = [[exit_.connected_region.name, sets(exit_.access_rule)]
                                for exit_ in region.exits if exit_.connected_region is not None]
        for location in region.locations:
            if location.address is None:
                continue  # event (Victory)
            locations[str(location.address)] = [region.name, sets(location.access_rule)]
    return {"start": multiworld.get_region("Menu", player).name, "regions": regions, "locations": locations}
