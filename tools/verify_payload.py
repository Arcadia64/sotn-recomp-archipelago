"""Check the forked world's slot_data["recomp"] payload against the .apsotn patch of the same seed.

Usage (Archipelago venv): ref/archipelago/.venv/Scripts/python.exe tools/verify_payload.py <seed zip> [slot ...]

Every byte the patch writes into a stage/boss file or DRA must be in the payload with the same value
(except DRA's seed block, which only the BizHawk client reads), and the payload must hold nothing else.
"""
import glob
import io
import json
import os
import sys
import zipfile
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "ref", "archipelago"))
sys.path.insert(0, os.path.join(ROOT, "tools"))

import gen_location_data as gen  # noqa: E402
from verify_placement import decode_tokens  # noqa: E402

SECTOR, HEADER, DATA = 2352, 24, 0x800
DRA_LBA, DRA_SIZE = 299, 1153136
SEED_BLOCK = (0x800DFAEC - 0x800A0000, 0x800DFD44 - 0x800A0000)


def main():
    zip_path = sys.argv[1]
    import Utils
    with zipfile.ZipFile(zip_path) as z:
        md = Utils.restricted_loads(zlib.decompress(z.read(next(n for n in z.namelist() if n.endswith(".archipelago")))[1:]))
        slots = [int(a) for a in sys.argv[2:]] or sorted(md["slot_data"])
        patches = {n: z.read(n) for n in z.namelist() if n.endswith(".apsotn")}

    zones_mod, _, _, _ = gen.load_world(os.path.join(ROOT, "apworld", "sotn"))
    by_key = {v: k for k, v in zones_mod.ZONE.items()}
    spans = [(zn["pos"], zn["pos"] + -(-zn["len"] // DATA) * SECTOR, by_key[num])
             for num, zn in zones_mod.zones.items() if "pos" in zn and "len" in zn]
    dra_start, dra_end = DRA_LBA * SECTOR, (DRA_LBA + -(-DRA_SIZE // DATA)) * SECTOR

    def to_file(off):
        for s, e, key in spans:
            if s <= off < e:
                sec, w = divmod(off - s, SECTOR)
                return (key, sec * DATA + w) if w < DATA else None
        if dra_start <= off < dra_end:
            sec, w = divmod(off - dra_start, SECTOR)
            w -= HEADER
            return ("DRA", sec * DATA + w) if 0 <= w < DATA else None
        return ("BIN", off)

    ok = True
    for slot in slots:
        payload = md["slot_data"][slot].get("recomp")
        name = next(n for n in patches if f"_P{slot}_" in n)
        with zipfile.ZipFile(io.BytesIO(patches[name])) as p:
            tokens = decode_tokens(p.read("token_data.bin"))
        expected = {}
        for off, data in tokens:
            for i, b in enumerate(data):
                where = to_file(off + i)
                if where is None or (where[0] == "DRA" and SEED_BLOCK[0] <= where[1] < SEED_BLOCK[1]):
                    continue
                expected[where] = b
        got = {}
        for key, runs in payload["files"].items():
            for off, hexbytes in runs:
                for i, b in enumerate(bytes.fromhex(hexbytes)):
                    got[(key, off + i)] = b
        wrong = [k for k in expected if got.get(k) != expected[k]]
        extra = [k for k in got if k not in expected]
        size = len(json.dumps(payload))
        per_file = {k: sum(len(h) // 2 for _, h in v) for k, v in payload["files"].items()}
        big = sorted(per_file.items(), key=lambda kv: -kv[1])[:6]
        print(f"slot {slot} ({name}): payload v{payload['version']}, {len(got)} bytes in {len(payload['files'])} files, "
              f"{size / 1024:.1f} KB JSON; wrong {len(wrong)}, extra {len(extra)}; largest {big}")
        ok &= not wrong and not extra
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
