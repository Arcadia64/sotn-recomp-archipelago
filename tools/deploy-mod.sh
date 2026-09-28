#!/usr/bin/env bash
# Copy the mod sources into the recomp's mods folder so the game's file watcher
# sees the change and rebuilds the mod. Usage: tools/deploy-mod.sh [recomp dir]
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
recomp="${1:-$root/ref/SymphonyRecomp}"
dest="$recomp/mods/archipelago"
mkdir -p "$dest"
# Remove files that no longer exist in the source, then copy the current set.
for f in "$dest"/*.cs "$dest"/mod.json "$dest"/mod-icon.png; do
  [ -e "$f" ] || continue
  [ -e "$root/mod/$(basename "$f")" ] || rm -f "$f"
done
cp -f "$root"/mod/*.cs "$root"/mod/mod.json "$dest"/
[ -e "$root/mod/mod-icon.png" ] && cp -f "$root/mod/mod-icon.png" "$dest"/
echo "deployed $(ls "$dest"/*.cs | wc -l) source file(s) to $dest"
