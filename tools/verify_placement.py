"""Check the mod's item placement against the patch the AP generator made for the same seed.

Usage (with the Archipelago venv, since the seed's multidata needs Archipelago to load):
  ref/archipelago/.venv/Scripts/python.exe tools/verify_placement.py [seed zip] [slot]

1. Reads the seed zip: the multidata (what is at each location) and the slot's .apsotn patch.
2. Runs tools/placement-check (the mod's Placement.cs) on those placements.
3. Converts every write in the .apsotn to (stage, RAM address) and compares:
   - every write the mod makes must match the patch byte for byte;
   - every patch write at an address some location's placement uses must be made by the mod,
     unless the mod reports that location as unsupported.
"""
import glob
import json
import os
import subprocess
import sys
import tempfile
import zipfile
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AP_DIR = os.path.join(ROOT, "ref", "archipelago")
sys.path.insert(0, AP_DIR)
sys.path.insert(0, os.path.join(ROOT, "tools"))

import gen_location_data as gen  # noqa: E402


def load_seed(zip_path, slot):
    with zipfile.ZipFile(zip_path) as z:
        multidata_name = next(n for n in z.namelist() if n.endswith(".archipelago"))
        raw = z.read(multidata_name)
        patch_name = next(n for n in z.namelist() if n.endswith(".apsotn") and f"_P{slot}_" in n)
        patch = z.read(patch_name)

    import Utils
    multidata = Utils.restricted_loads(zlib.decompress(raw[1:]))
    placements = multidata["locations"][slot]
    scouts = [[loc, item, player, flags] for loc, (item, player, flags) in placements.items()]

    with zipfile.ZipFile(tempfile.SpooledTemporaryFile()) if False else zipfile.ZipFile(_bytes_io(patch)) as p:
        tokens = p.read("token_data.bin")
    return scouts, tokens, patch_name


def _bytes_io(data):
    import io
    return io.BytesIO(data)


def decode_tokens(data):
    count = int.from_bytes(data[0:4], "little")
    pos = 4
    out = []
    for _ in range(count):
        kind = data[pos]
        offset = int.from_bytes(data[pos + 1:pos + 5], "little")
        size = int.from_bytes(data[pos + 5:pos + 9], "little")
        payload = data[pos + 9:pos + 9 + size]
        pos += 9 + size
        if kind != 0:
            sys.exit(f"token type {kind} at 0x{offset:X} not handled")
        out.append((offset, payload))
    return out


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    zip_path = args[0] if args else max(glob.glob(os.path.join(AP_DIR, "output", "AP_*.zip")), key=os.path.getmtime)
    slot = int(args[1]) if len(args) > 1 else 1
    scouts, tokens, patch_name = load_seed(zip_path, slot)
    print(f"seed {os.path.basename(zip_path)}, slot {slot}: {len(scouts)} locations, patch {patch_name}")

    # What the patch writes, per stage RAM byte.
    zones_mod, _, _, _ = gen.load_world(gen.DEFAULT_WORLD)
    disc = gen.DiscMap(zones_mod)
    expected = {}
    unmapped = 0
    for offset, payload in decode_tokens(tokens):
        for i, b in enumerate(payload):
            where = disc.to_ram(offset + i)
            if where is None:
                unmapped += 1
                continue
            expected[where] = b
    print(f"patch: {len(expected)} bytes in stage files, {unmapped} bytes elsewhere (DRA and other files, not compared)")

    # What the mod writes.
    with tempfile.TemporaryDirectory() as tmp:
        scouts_path = os.path.join(tmp, "scouts.json")
        out_path = os.path.join(tmp, "out.json")
        with open(scouts_path, "w") as f:
            json.dump({"slot": slot, "scouts": scouts}, f)
        subprocess.run(["dotnet", "run", "--project", os.path.join(ROOT, "tools", "placement-check"), "--",
                        scouts_path, out_path], check=True, capture_output=True)
        with open(out_path) as f:
            result = json.load(f)

    unsupported = {loc: why for loc, why in result["unsupported"]}
    mismatches = []
    produced = set()
    for loc, stage, addr, value, size in result["writes"]:
        for i, b in enumerate(value.to_bytes(size, "little")):
            key = (stage, addr + i)
            produced.add(key)
            if expected.get(key) != b:
                mismatches.append(f"loc {loc}: stage 0x{stage:02X} 0x{addr + i:08X} mod=0x{b:02X} "
                                  f"patch={'none' if key not in expected else f'0x{expected[key]:02X}'}")

    missing = []
    for loc, stage, addr in result["universe"]:
        if loc in unsupported:
            continue
        for i in range(2):
            key = (stage, addr + i)
            if key in expected and key not in produced:
                missing.append(f"loc {loc}: stage 0x{stage:02X} 0x{addr + i:08X} patch=0x{expected[key]:02X} not written by the mod")

    if "--other" in sys.argv:
        # Patch bytes in stage files that no placement write explains: option or always-on patches
        # (accessibility fixes, dialog skips...) to review separately.
        other = sorted(k for k in expected if k not in produced)
        runs, start, prev = [], None, None
        for stage, addr in other:
            if start and stage == start[0] and addr == prev[1] + 1:
                prev = (stage, addr)
                continue
            if start:
                runs.append((start, prev))
            start = prev = (stage, addr)
        if start:
            runs.append((start, prev))
        print(f"patch bytes not written by the mod: {len(other)} in {len(runs)} runs")
        for (st, a), (_, b) in runs:
            print(f"  stage 0x{st:02X} 0x{a:08X}-0x{b:08X} ({b - a + 1} bytes)")

    print(f"mod: {len(result['writes'])} writes, {len(unsupported)} unsupported locations")
    for loc, why in sorted(unsupported.items()):
        print(f"  unsupported {loc}: {why}")
    print(f"mismatches: {len(mismatches)}")
    for m in mismatches[:40]:
        print("  " + m)
    print(f"missing: {len(missing)}")
    for m in sorted(set(missing))[:40]:
        print("  " + m)
    sys.exit(1 if mismatches or missing else 0)


if __name__ == "__main__":
    main()
