#!/usr/bin/env bash
# Generate the seeds the offline checks use, into ref/archipelago/test-output/{stock,fork}/.
#   stock: fdelduque's unmodified AP world 0.8.16.1 (ref/ap-world/apworld-b08161/sotn.apworld), for
#          tools/verify_placement.py (the mod's placement code vs that world's official patch)
#   fork:  this repo's world (apworld/sotn_recomp), for tools/verify_payload.py (slot_data payload vs
#          the world's own disc writes, which it saves next to the seed when SOTN_RECOMP_DEBUG_TOKENS is set)
# The two worlds have different game names, so both stay installed in ref/archipelago/custom_worlds.
# The same four player files are used for both, under each world's game name.
# Usage (from the project folder): tools/make_test_seeds.sh [seed ...]   (default: 11 22 33)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
ap="$root/ref/archipelago"
players="$ap/test-players"
out="$ap/test-output"
seeds=("${@:-11 22 33}")
seeds=(${seeds[@]})

mkdir -p "$players/stock" "$players/fork" "$out/stock" "$out/fork"
rm -f "$players"/*.yaml

# Options per slot; GAME is replaced with each world's game name.
write_players() { # $1 = folder, $2 = game name
  cat > "$1/a.yaml" <<'EOF'
name: SlotA
game: GAME
GAME:
  item_pool: full
  boss_locations: true
EOF
  cat > "$1/b.yaml" <<'EOF'
name: SlotB
game: GAME
GAME:
  item_pool: equipment
  boss_locations: true
EOF
  cat > "$1/c.yaml" <<'EOF'
name: SlotC
game: GAME
GAME:
  item_pool: relic_prog
EOF
  cat > "$1/d.yaml" <<'EOF'
name: SlotD
game: GAME
GAME:
  item_pool: full
  boss_locations: true
  enemysanity: true
  enemy_scroll: true
  difficult: easy
  randomize_items: true
  powerful_items: true
  rng_start_gear: true
  remove_prologue: true
  magic_vessels: true
  anti_freeze: true
  my_purse: true
  fast_warp: true
  enemy_stats: true
  random_shop: on
  shop_prices: true
  starting_zone: any_castle
  reverse_library: true
  random_music: true
  skip_nz1: true
  color_randomizer: true
  randomize_drop: full_global
  randomize_candles: full
  infinite_wing_smash: true
  map_color: crimson
  alucard_palette: blue_danube
  relic_suprise: true
  death_link: true
  auto_heal: true
EOF
  for f in "$1"/*.yaml; do sed -i "s/GAME/$2/g" "$f"; done
}

generate() { # $1 = player files, $2 = output folder, then extra environment settings
  local players_dir="$1" output="$2"
  shift 2
  rm -f "$output"/*
  for s in "${seeds[@]}"; do
    (cd "$ap" && env PYTHONUNBUFFERED=1 "$@" .venv/Scripts/python Generate.py --skip_prog_balancing \
        --player_files_path "$players_dir" --outputpath "$output" --seed "$s" < /dev/null 2>&1 \
      | grep -E "Traceback|Error" || true)
  done
  echo "$(ls "$output"/*.zip | wc -l) seed(s) in $output"
}

write_players "$players/stock" "Symphony of the Night"
write_players "$players/fork" "Symphony of the Night (Recomp)"

cp "$root/ref/ap-world/apworld-b08161/sotn.apworld" "$ap/custom_worlds/sotn.apworld"
py -3.12 "$root/tools/build_apworld.py" > /dev/null
cp "$root/dist/sotn_recomp.apworld" "$ap/custom_worlds/sotn_recomp.apworld"

generate "$players/stock" "$out/stock"
generate "$players/fork" "$out/fork" SOTN_RECOMP_DEBUG_TOKENS=1
