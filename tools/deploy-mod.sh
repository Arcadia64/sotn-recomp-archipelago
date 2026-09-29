#!/usr/bin/env bash
# Copy the mod into the recomp's mods folder so the game's file watcher sees the change and
# rebuilds the mod. Same layout as the recomp's bundled mods and our release zip: mod.json and
# mod-icon.png at the top, the code in source/. Usage: tools/deploy-mod.sh [recomp dir]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
recomp="${1:-$root/ref/SymphonyRecomp}"
dest="$recomp/mods/archipelago"
mkdir -p "$dest/source"
# Sources from the old flat layout, and ones deleted from mod/.
rm -f "$dest"/*.cs
for f in "$dest"/source/*.cs; do
  [ -e "$f" ] || continue
  [ -e "$root/mod/$(basename "$f")" ] || rm -f "$f"
done
cp -f "$root"/mod/*.cs "$dest"/source/
cp -f "$root"/mod/mod.json "$root"/LICENSE "$dest"/
[ -e "$root/mod/mod-icon.png" ] && cp -f "$root/mod/mod-icon.png" "$dest"/
echo "deployed $(ls "$dest"/source/*.cs | wc -l) source file(s) to $dest"
