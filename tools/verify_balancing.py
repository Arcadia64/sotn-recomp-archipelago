"""The world's game data must be built from the final placement: progression balancing (on by default) runs
after post_fill and moves items, and the mod takes the player's own items from that data (the server doesn't
send a player their own items). Generates a 4-slot game with balancing on, records every SotN slot's placement
when write_tokens runs, and compares it with the placement at the end.
Usage (from the project folder, with the Archipelago venv): verify_balancing.py [seed]"""
import builtins
import importlib
import os
import shutil
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP = os.path.join(ROOT, "ref", "archipelago")
GAME = "Symphony of the Night (Recomp)"


def main():
    seed = int(sys.argv[1]) if len(sys.argv) > 1 else 24
    base = tempfile.mkdtemp(prefix="sotn_balancing_")
    players, output = os.path.join(base, "p"), os.path.join(base, "o")
    os.makedirs(players)
    os.makedirs(output)
    # One slot with few locations, so it's behind and balancing has something to do.
    with open(os.path.join(players, "a.yaml"), "w") as f:
        f.write(f"name: Alucard\ngame: {GAME}\n{GAME}:\n  item_pool: relic_prog\n  progression_balancing: 99\n")
    for i in range(3):
        with open(os.path.join(players, f"b{i}.yaml"), "w") as f:
            f.write(f"name: Other{i}\ngame: {GAME}\n{GAME}:\n  progression_balancing: 99\n  boss_locations: true\n"
                    f"  powerful_items: true\n  randomize_items: true\n")

    os.chdir(AP)
    sys.path.insert(0, AP)
    builtins.input = lambda *a: ""
    from worlds.AutoWorld import AutoWorldRegister
    world_type = AutoWorldRegister.world_types[GAME]
    package = importlib.import_module(world_type.__module__)

    def placement(multiworld, player):
        return {loc.name: (loc.item.name, loc.item.player) for loc in multiworld.get_locations(player) if loc.item}

    at_post_fill, at_tokens = {}, {}
    original_post_fill, original_tokens = world_type.post_fill, package.write_tokens

    def post_fill(self):
        at_post_fill[self.player] = placement(self.multiworld, self.player)
        original_post_fill(self)

    def write_tokens(world, patch):
        at_tokens[world.player] = placement(world.multiworld, world.player)
        return original_tokens(world, patch)

    world_type.post_fill = post_fill
    package.write_tokens = write_tokens

    import Generate
    import Main
    sys.argv = ["Generate.py", "--player_files_path", players, "--outputpath", output, "--seed", str(seed)]
    args, seed_value = Generate.main()
    multiworld = Main.main(args, seed_value)
    shutil.rmtree(base, ignore_errors=True)

    def changed(before):
        return sum(1 for player, places in before.items() for name, item in places.items()
                   if (multiworld.get_location(name, player).item.name,
                       multiworld.get_location(name, player).item.player) != item)

    moved, stale = changed(at_post_fill), changed(at_tokens)
    print(f"{len(at_tokens)} slot(s); balancing moved {moved} item(s) after post_fill; "
          f"{stale} placement(s) differ from the game data")
    if len(at_tokens) != 4 or stale:
        sys.exit(1)
    if moved == 0:
        print("(balancing moved nothing with this seed: try another one to exercise it)")


if __name__ == "__main__":
    main()
