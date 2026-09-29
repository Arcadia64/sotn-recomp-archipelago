"""Zip apworld/sotn_recomp into dist/sotn_recomp.apworld (what Archipelago's "Install APWorld" takes).

Usage: py -3.12 tools/build_apworld.py [output path]
"""
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACKAGE = "sotn_recomp"  # the world's module name; the .apworld and its folder must match it
SRC = os.path.join(ROOT, "apworld", PACKAGE)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "dist", f"{PACKAGE}.apworld")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(SRC):
            dirs[:] = [d for d in dirs if d != "__pycache__"]
            for f in sorted(files):
                if f.endswith(".pyc"):
                    continue
                path = os.path.join(root, f)
                z.write(path, os.path.join(PACKAGE, os.path.relpath(path, SRC)))
                count += 1
        z.write(os.path.join(ROOT, "apworld", "LICENSE"), os.path.join(PACKAGE, "LICENSE"))
        count += 1
    print(f"wrote {count} files to {out}")


if __name__ == "__main__":
    main()
