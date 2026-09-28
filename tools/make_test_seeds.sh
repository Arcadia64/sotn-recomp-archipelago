#!/usr/bin/env bash
# Generate the seeds the offline checks use, into ref/archipelago/test-output/{stock,fork}/.
#   stock: the unmodified AP world 0.8.16.1 (ref/ap-world/apworld-b08161/sotn.apworld), for
#          tools/verify_placement.py (mod placement vs the official patch)
#   fork:  this repo's apworld/, for tools/verify_payload.py (slot_data payload vs its patch)
# Leaves the fork world installed in ref/archipelago/custom_worlds.
# Usage (from the project folder): tools/make_test_seeds.sh [seed ...]   (default: 11 22 33)
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
ap="$root/ref/archipelago"
players="$ap/test-players"
out="$ap/test-output"
seeds=("${@:-11 22 33}")
seeds=(${seeds[@]})

mkdir -p "$players" "$out/stock" "$out/fork"
cat > "$players/a.yaml" <<'EOF'
name: SlotA
game: Symphony of the Night
Symphony of the Night:
  item_pool: full
  boss_locations: true
EOF
cat > "$players/b.yaml" <<'EOF'
name: SlotB
game: Symphony of the Night
Symphony of the Night:
  item_pool: equipment
  boss_locations: true
EOF
cat > "$players/c.yaml" <<'EOF'
name: SlotC
game: Symphony of the Night
Symphony of the Night:
  item_pool: relic_prog
EOF
cat > "$players/d.yaml" <<'EOF'
name: SlotD
game: Symphony of the Night
Symphony of the Night:
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

generate() { # $1 = output folder
  rm -f "$1"/*
  for s in "${seeds[@]}"; do
    (cd "$ap" && PYTHONUNBUFFERED=1 .venv/Scripts/python Generate.py --skip_prog_balancing \
        --player_files_path "$players" --outputpath "$1" --seed "$s" < /dev/null 2>&1 \
      | grep -E "Traceback|Error" || true)
  done
  echo "$(ls "$1" | wc -l) seed(s) in $1"
}

cp "$root/ref/ap-world/apworld-b08161/sotn.apworld" "$ap/custom_worlds/sotn.apworld"
generate "$out/stock"

py -3.12 "$root/tools/build_apworld.py" > /dev/null
cp "$root/dist/sotn.apworld" "$ap/custom_worlds/sotn.apworld"
generate "$out/fork"
