"""Draw mod/mod-icon.png (shown in the game's Mods window) from the in-game AP badge art in
mod/ApLook.cs, scaled up with hard pixel edges, in the progression (gold) colours.

Usage (needs Pillow, which the Archipelago venv has):
  ref/archipelago/.venv/Scripts/python.exe tools/make_icon.py
"""
import os
import re

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "mod", "ApLook.cs")
OUT = os.path.join(ROOT, "mod", "mod-icon.png")
SCALE = 8  # 16x16 -> 128x128


def mix(c, to, percent):
    return tuple(a + (b - a) * percent // 100 for a, b in zip(c, to))


def main():
    src = open(SOURCE, encoding="utf-8").read()
    block = re.search(r"static readonly string\[\] Art =\s*\[(.*?)\];", src, re.S).group(1)
    art = re.findall(r'"([^"]{16})"', block)
    assert len(art) == 16, f"expected 16 rows of art, found {len(art)}"

    # Same derivation as ApLook.WritePalette, for the progression class colour.
    base = (0xF0, 0xB0, 0x20)
    colours = {
        "o": (0x10, 0x10, 0x20),
        "b": base,
        "h": mix(base, (255, 255, 255), 45),
        "s": mix(base, (0, 0, 0), 40),
        "W": (0xFF, 0xFF, 0xFF),
        "d": (0x18, 0x18, 0x30),
    }

    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    for y, row in enumerate(art):
        for x, ch in enumerate(row):
            if ch in colours:
                img.putpixel((x, y), colours[ch] + (255,))
    img = img.resize((16 * SCALE, 16 * SCALE), Image.NEAREST)
    img.save(OUT)
    print(f"wrote {os.path.relpath(OUT, ROOT)} ({img.width}x{img.height})")


if __name__ == "__main__":
    main()
