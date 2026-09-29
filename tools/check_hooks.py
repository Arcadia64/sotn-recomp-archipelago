"""Check that every [PreHook]/[PostHook]/[Replace] in mod/ names a function the game can hook.

The recomp resolves hooks by overlay and function name through each overlay's dispatch table
(SymbolRegistry); a name that isn't there is only reported at run time ("function not found").
By default this reads the tables from ref/SymphonyRecomp/generated/<overlay>.cs. With --game, it
reads them from a built game instead (a release download, say), through tools/dispatch-dump.

Usage: py -3.12 tools/check_hooks.py [--game <folder with sotn.dll>]
"""
import collections
import glob
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GENERATED = os.path.join(ROOT, "ref", "SymphonyRecomp", "generated")
HOOK = re.compile(r'\[(PreHook|PostHook|Replace)\("(\w+)",\s*"(\w+)"\)\]')
ENTRY = re.compile(r"\[0x[0-9A-F]{8}u\] = SoTN\.(\w+),")


def tables_from_source(overlay):
    gen = os.path.join(GENERATED, f"{overlay}.cs")
    if not os.path.exists(gen):
        sys.exit(f"{gen} missing: build the recomp first")
    return set(ENTRY.findall(open(gen, encoding="utf-8").read()))


def tables_from_game(folder):
    out = subprocess.run(["dotnet", "run", "--project", os.path.join(ROOT, "tools", "dispatch-dump"), "--", folder],
                         capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"dispatch-dump failed:\n{out.stderr}")
    tables = collections.defaultdict(set)
    for line in out.stdout.splitlines():
        overlay, name = line.split()
        tables[overlay].add(name)
    return tables


def main():
    args = sys.argv[1:]
    game = args[args.index("--game") + 1] if "--game" in args else None
    tables = tables_from_game(game) if game else {}
    missing = []
    count = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "mod", "*.cs"))):
        for kind, overlay, fn in HOOK.findall(open(path, encoding="utf-8").read()):
            count += 1
            if overlay not in tables and not game:
                tables[overlay] = tables_from_source(overlay)
            if fn not in tables.get(overlay, ()):
                missing.append(f"{os.path.basename(path)}: {kind} {overlay}/{fn}")
    print(f"{count} hooks checked against {game or 'ref/SymphonyRecomp'}, {len(missing)} not found")
    for line in missing:
        print("  " + line)
    sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
