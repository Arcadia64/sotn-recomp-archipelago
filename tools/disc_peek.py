"""Read original bytes from a stage/boss file on the disc image, by AP zone key and file offset.

Usage: py -3.12 tools/disc_peek.py ZONE OFFSET LENGTH [u16|u32|bytes]
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_location_data as g  # noqa: E402

DISC = os.path.join(g.ROOT, "ref", "SymphonyRecomp", "disc", "Castlevania - Symphony of the Night (USA) (Track 1).bin")


def read(zone, offset, length):
    out = bytearray()
    with open(DISC, "rb") as f:
        for i in range(length):
            a = offset + i
            f.seek(zone["pos"] + a + (a // 0x800) * 0x130)
            out += f.read(1)
    return bytes(out)


if __name__ == "__main__":
    zones_mod, _, _, _ = g.load_world(g.DEFAULT_WORLD)
    zone = zones_mod.zones[zones_mod.ZONE[sys.argv[1]]]
    off, n = int(sys.argv[2], 0), int(sys.argv[3], 0)
    kind = sys.argv[4] if len(sys.argv) > 4 else "u16"
    data = read(zone, off, n)
    if kind == "u16":
        print(" ".join(f"{v:04X}" for v in struct.unpack(f"<{n // 2}H", data)))
    elif kind == "u32":
        print(" ".join(f"{v:08X}" for v in struct.unpack(f"<{n // 4}I", data)))
    else:
        print(data.hex())
