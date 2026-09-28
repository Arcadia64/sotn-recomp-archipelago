"""Zip apworld/sotn into dist/sotn.apworld (what Archipelago's "Install APWorld" takes).

Usage: py -3.12 tools/build_apworld.py [output path]
"""
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "apworld", "sotn")


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "dist", "sotn.apworld")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(SRC):
            dirs[:] = [d for d in dirs if d != "__pycache__"]
            for f in sorted(files):
                if f.endswith(".pyc"):
                    continue
                path = os.path.join(root, f)
                z.write(path, os.path.join("sotn", os.path.relpath(path, SRC)))
                count += 1
        z.write(os.path.join(ROOT, "apworld", "LICENSE"), os.path.join("sotn", "LICENSE"))
        count += 1
    print(f"wrote {count} files to {out}")


if __name__ == "__main__":
    main()
