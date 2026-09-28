"""Build the release files into dist/:
  dist/archipelago.zip  - the SymphonyRecomp mod. Players drop the zip as-is into the recomp's mods
                          folder (the mod loader reads mod.json, *.cs and mod-icon.png from a zip).
  dist/sotn.apworld     - the AP world, for whoever generates the multiworld.

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

    files = sorted(glob.glob(os.path.join(MOD, "*.cs"))) + [os.path.join(MOD, "mod.json")]
    icon = os.path.join(MOD, "mod-icon.png")
    if os.path.exists(icon):
        files.append(icon)
    files.append(os.path.join(ROOT, "LICENSE"))
    out = os.path.join(DIST, "archipelago.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for path in files:
            z.write(path, os.path.basename(path))
    print(f"wrote {os.path.relpath(out, ROOT)}: mod {info['id']} v{info['version']}, {len(files)} files")

    subprocess.run([sys.executable, os.path.join(ROOT, "tools", "build_apworld.py")], check=True)


if __name__ == "__main__":
    main()
