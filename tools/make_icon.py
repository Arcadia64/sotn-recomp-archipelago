"""Draw the AP badge from its in-game art in mod/ApLook.cs, scaled up with hard pixel edges:
- mod/mod-icon.png (shown in the game's Mods window), in the progression (gold) colours;
- docs/images/ap-badge-<class>.png for the README, one per item class, in the colours the game shows (the
  class colours from ApLook's WritePalette calls, through the PlayStation's 15-bit colour).

Usage (needs Pillow, which the Archipelago venv has):
  ref/archipelago/.venv/Scripts/python.exe tools/make_icon.py
"""
import os
import re

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "mod", "ApLook.cs")
ICON = os.path.join(ROOT, "mod", "mod-icon.png")
BADGES = os.path.join(ROOT, "docs", "images")
ICON_SCALE = 8   # 16x16 -> 128x128
BADGE_SCALE = 4  # 16x16 -> 64x64


def mix(c, to, percent):
    return tuple(a + (b - a) * percent // 100 for a, b in zip(c, to))


def psx(c):
    """What the game shows for an RGB colour stored as PlayStation 15-bit colour (ApLook.Bgr)."""
    return tuple((v >> 3) * 255 // 31 for v in c)


def draw(art, base, scale, as_shown):
    # Same derivation as ApLook.WritePalette.
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
                img.putpixel((x, y), (psx(colours[ch]) if as_shown else colours[ch]) + (255,))
    return img.resize((16 * scale, 16 * scale), Image.NEAREST)


def main():
    src = open(SOURCE, encoding="utf-8").read()
    block = re.search(r"static readonly string\[\] Art =\s*\[(.*?)\];", src, re.S).group(1)
    art = re.findall(r'"([^"]{16})"', block)
    assert len(art) == 16, f"expected 16 rows of art, found {len(art)}"

    # The class colours, as ApLook sets them: WritePalette(m, <Class>Palette, r, g, b).
    classes = {name.lower(): tuple(int(v, 16) for v in rgb) for name, *rgb in re.findall(
        r"WritePalette\(m, (\w+)Palette, 0x(\w\w), 0x(\w\w), 0x(\w\w)\)", src)}
    assert set(classes) == {"progression", "useful", "filler", "trap"}, classes

    icon = draw(art, classes["progression"], ICON_SCALE, as_shown=False)
    icon.save(ICON)
    print(f"wrote {os.path.relpath(ICON, ROOT)} ({icon.width}x{icon.height})")

    os.makedirs(BADGES, exist_ok=True)
    for name, base in classes.items():
        path = os.path.join(BADGES, f"ap-badge-{name}.png")
        draw(art, base, BADGE_SCALE, as_shown=True).save(path)
        print(f"wrote {os.path.relpath(path, ROOT)}")


if __name__ == "__main__":
    main()
