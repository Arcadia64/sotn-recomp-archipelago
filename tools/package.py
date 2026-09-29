"""Build the release files into dist/:
  dist/archipelago.zip  - the SymphonyRecomp mod. Players drop the zip as-is into the recomp's mods
                          folder (the mod loader reads mod.json, mod-icon.png and every *.cs from a zip).
                          Laid out like the recomp's bundled mods: mod.json, mod-icon.png, source/*.cs.
  dist/sotn_recomp.apworld - the AP world ("Symphony of the Night (Recomp)"), for whoever generates.

Usage: py -3.12 tools/package.py
"""
import glob
import json
import os
import subprocess
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MOD = os.path.join(ROOT, "mod")
DIST = os.path.join(ROOT, "dist")


def main():
    os.makedirs(DIST, exist_ok=True)
    info = json.load(open(os.path.join(MOD, "mod.json"), encoding="utf-8"))

    files = [(path, "source/" + os.path.basename(path)) for path in sorted(glob.glob(os.path.join(MOD, "*.cs")))]
    files.append((os.path.join(MOD, "mod.json"), "mod.json"))
    icon = os.path.join(MOD, "mod-icon.png")
    if os.path.exists(icon):
        files.append((icon, "mod-icon.png"))
    files.append((os.path.join(ROOT, "LICENSE"), "LICENSE"))
    out = os.path.join(DIST, "archipelago.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for path, name in files:
            z.write(path, name)
    print(f"wrote {os.path.relpath(out, ROOT)}: mod {info['id']} v{info['version']}, {len(files)} files")

    subprocess.run([sys.executable, os.path.join(ROOT, "tools", "build_apworld.py")], check=True)


if __name__ == "__main__":
    main()
