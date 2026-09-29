#!/usr/bin/env bash
# Generate the seeds the offline checks use, into ref/archipelago/test-output/{stock,fork}/.
#   stock: fdelduque's unmodified AP world 0.8.16.1 (ref/ap-world/apworld-b08161/sotn.apworld), for
#          tools/verify_placement.py (the mod's placement code vs that world's official patch)
#   fork:  this repo's world (apworld/sotn_recomp), for tools/verify_payload.py (slot_data payload vs
#          the world's own disc writes, which it saves next to the seed when SOTN_RECOMP_DEBUG_TOKENS is set)
# The two worlds have different game names, so both stay installed in ref/archipelago/custom_worlds.
# The same four players for both, under each world's game name and option names.
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
write_players() { # $1 = folder, $2 = game name, $3 = "stock" for the upstream world's option names
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
  if [ "${3:-}" = stock ]; then
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
  else
  cat > "$1/d.yaml" <<'EOF'
name: SlotD
game: GAME
GAME:
  item_pool: everything
  boss_drops: true
  enemysanity: true
  enemysanity_needs_faerie_scroll: true
  difficulty: easy
  randomize_other_items: true
  powerful_items: true
  random_starting_equipment: true
  skip_prologue: true
  magic_vessels: true
  no_screen_freezes: true
  keep_starting_equipment: true
  fast_warp: true
  random_enemy_stats: true
  shop_stock: random_items
  random_shop_prices: true
  starting_area: random_anywhere
  inverted_library_card: true
  random_music: true
  skip_clock_tower_puzzle: true
  random_colors: true
  enemy_drops: any_item
  enemy_drops_include_hearts_and_gold: true
  candle_drops: any_item
  infinite_wing_smash: true
  map_color: crimson
  alucard_palette: blue_danube
  relic_surprise: true
  death_link: true
  heal_at_save_rooms: true
EOF
    # the fork's other slots, in its own names (the old ones are aliases or ignored)
    sed -i "s/boss_locations: true/boss_drops: true/; s/item_pool: full/item_pool: everything/; s/item_pool: relic_prog/item_pool: key_items/" "$1"/[abc].yaml
  fi
  for f in "$1"/*.yaml; do sed -i "s/GAME/$2/g" "$f"; done
}

# The fork's seeds go through progression balancing (on by default in Archipelago): its game data must come
# from the placement after it (pre_output).
generate() { # $1 = player files, $2 = output folder, $3 = extra Generate.py flags, then extra environment settings
  local players_dir="$1" output="$2" flags="$3"
  shift 3
  rm -f "$output"/*
  for s in "${seeds[@]}"; do
    (cd "$ap" && env PYTHONUNBUFFERED=1 "$@" .venv/Scripts/python Generate.py $flags \
        --player_files_path "$players_dir" --outputpath "$output" --seed "$s" < /dev/null 2>&1 \
      | grep -E "Traceback|Error" || true)
  done
  echo "$(ls "$output"/*.zip | wc -l) seed(s) in $output"
}

write_players "$players/stock" "Symphony of the Night" stock
write_players "$players/fork" "Symphony of the Night (Recomp)"

cp "$root/ref/ap-world/apworld-b08161/sotn.apworld" "$ap/custom_worlds/sotn.apworld"
py -3.12 "$root/tools/build_apworld.py" > /dev/null
cp "$root/dist/sotn_recomp.apworld" "$ap/custom_worlds/sotn_recomp.apworld"

generate "$players/stock" "$out/stock" --skip_prog_balancing
generate "$players/fork" "$out/fork" "" SOTN_RECOMP_DEBUG_TOKENS=1
