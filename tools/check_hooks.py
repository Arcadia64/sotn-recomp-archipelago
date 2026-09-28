"""Check that every [PreHook]/[PostHook]/[Replace] in mod/ names a function the game can hook.

The recomp resolves hooks by overlay and function name through each overlay's dispatch table
(SymbolRegistry); a name that isn't there is only reported at run time ("function not found").
This reads the same tables from ref/SymphonyRecomp/generated/<overlay>.cs.

Usage: py -3.12 tools/check_hooks.py
"""
import glob
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GENERATED = os.path.join(ROOT, "ref", "SymphonyRecomp", "generated")
HOOK = re.compile(r'\[(PreHook|PostHook|Replace)\("(\w+)",\s*"(\w+)"\)\]')
ENTRY = re.compile(r"\[0x[0-9A-F]{8}u\] = SoTN\.(\w+),")


def main():
    tables = {}
    missing = []
    count = 0
    for path in sorted(glob.glob(os.path.join(ROOT, "mod", "*.cs"))):
        for kind, overlay, fn in HOOK.findall(open(path, encoding="utf-8").read()):
            count += 1
            if overlay not in tables:
                gen = os.path.join(GENERATED, f"{overlay}.cs")
                if not os.path.exists(gen):
                    sys.exit(f"{gen} missing: build the recomp first")
                tables[overlay] = set(ENTRY.findall(open(gen, encoding="utf-8").read()))
            if fn not in tables[overlay]:
                missing.append(f"{os.path.basename(path)}: {kind} {overlay}/{fn}")
    print(f"{count} hooks checked, {len(missing)} not found")
    for line in missing:
        print("  " + line)
    sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
