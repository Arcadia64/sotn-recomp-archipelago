# SOTN Archipelago world (fdelduque fork): technical notes for a native (recomp) port

Researched 2026-09-28. Read-only study. Nothing in the world was changed.

## 0. Short version

- The world is `worlds/sotn` in https://github.com/fdelduque/Archipelago. The latest release is **Beta 0.8161** (tag `b08161`, world_version 0.8.16, `CURRENT_VERSION = 816`). I also downloaded alpha `a08142` to compare the two.
- It does **not** use or call sotn-randomizer (the JS project). Every sotn.io patch it needs has been rewritten in Python and inlined into `Rom.py`, `data/io_items.py`, `Enemies.py` and `data/Constants.py`.
- Output is an `.apsotn`, an `APProcedurePatch` that holds only `token_data.bin` and `options.json`. When the player opens it, it writes byte tokens straight into the raw Track 1 `.bin` (2352-byte sectors), then recomputes EDC/ECC, writes a `.cue`, and copies Track 2.
- The client is a `BizHawkClient` subclass. It uses `items_handling = 0b101`. Items for your own world are placed physically in the disc image and picked up normally. The server sends only other worlds' items plus starting inventory. The client "gives" items by poking g_Status directly: inventory counts, inventory order arrays, relic bytes, and HP/hearts.
- **The client never uses slot_data or LocationScouts.** It reads everything from the patched game itself. The seed, slot number, slot name, option bits, relic placement and enemysanity item table are hidden inside DRA.BIN's Time Attack label strings at RAM `0x800DFAEC`–`0x800DFD44`.
- Location checks come from the game's own flags:
  - stage item-pickup bits in `g_CastleFlags+0x100..`
  - breakable-wall castle flags
  - time-attack boss records at `0x8003CA28+4*i`
  - relic-owned bytes (`g_Status.relics`)
  - bestiary bits at `0x8003BF7C` (enemysanity)
- Two bugs are confirmed with a test script (section 7):
  - `difficult: easy` together with `drop_mod: normal` sets **every enemy drop rate to 0**.
  - `randomize_drop` edits a shared table in place, which makes `randomize_candles` silently skip about 93% of candles.

---

## 1. What was downloaded and where

| Path | What |
|---|---|
| `ref/ap-world/apworld-b08161/sotn.apworld` (+ unzipped `sotn/`) | Beta 0.8161 release asset (the main subject of these notes) |
| `ref/ap-world/apworld-a08142/sotn.apworld` (+ unzipped `sotn/`) | Alpha 0.8142 release asset |
| `ref/ap-world/repo-b08161/` | Partial sparse clone at tag b08161: `worlds/sotn`, `worlds/_bizhawk`, `data/lua/connector_bizhawk_generic.lua` (the old git needed a manual `core.sparseCheckout`). `worlds/sotn` matches the apworld byte for byte. |
| `ref/ap-world/releases.json` | GitHub release list (API dump) |

Unless a path says otherwise, every file path below is relative to `ref/ap-world/apworld-b08161/sotn/`.

Differences between a08142 and b08161:
- b08161 adds the `randomize_candles` option and function.
- It expands the `randomize_drop` choices (3–10).
- It replaces `disable_nz1_puzzle` with Forat Negre's `single_hit_gears`.
- It adds a Rules fix for `RNO0_Heart refresh_11`.
- It adds `archipelago.json` and `helper.py`.
- It bumps `CURRENT_VERSION` from 814 to 816.

---

## 2. Folder layout

| File | Lines | Role |
|---|---|---|
| `__init__.py` | 223 | `SotnWorld`. Settings for the Track 1 and Track 2 files (26-40); item pool (89-188); `fill_slot_data` (211-215); `generate_output` builds the patch (217-223). Imports `SotNClient` so the client registers (15). |
| `Items.py` | 2033 | `items` dict: AP id = game id, plus type, RAM address (used by the client when granting) and classification (11-2021). `tile_id_offset = 0x80` (3). Lookup tables (2023-2033). |
| `Locations.py` | 4774 | `locations` dict of 540 entries. Each has `ap_id`, `zones`, `index` (stage item-table index = pickup-flag bit), `entities` (layout-table offsets), `as_relic`/`as_item` x/y overrides, `vanilla_item`, plus special fields (`kill_time`, `break_flag/mask`, `addresses`, `no_offset`, `boss/bin_address`, `erase`, `reward`, `trio`, `enemy/game_id`). Builds `ZONE_LOCATIONS`, `ENEMY_LOCATIONS`, `AP_ID_TO_NAME`, `LOCATION_TO_ABREV`/`ABREV_TO_LOCATION` (4732-4770). |
| `Regions.py` | 527 | Two region graphs: `create_regions_no_logic` (9) and `create_regions` (268). Decides which locations exist from `item_pool` (EXTENSIONS), `boss_locations` and `enemysanity` (505-518). Adds the event location "Reverse Center Cube - Kill Dracula" (520-522). |
| `Rules.py` | 458 | Logic helpers such as flying and reverse castle (10-50). `set_no_logic_rules` (53) and `set_rules` (99). Also forbids items that the engine can't show at some spots (vessels or relics at breakable walls, Trio, Vlad bosses, and so on). |
| `Options.py` | 377 | 37 options plus option groups. |
| `Rom.py` | 3662 | Everything that is written to the disc: the patch class, accessibility patches, location writes, the tables for the client, and every cosmetic or gameplay patch. |
| `client.py` | 1798 | `SotNClient(BizHawkClient)`. |
| `data/Zones.py` | 549 | 50 zones (stages and bosses): disc `pos`, `items` table offset, boss `rewards` offset, `area_flag`, `loot_flag` address and size. |
| `data/Constants.py` | 1248 | `CURRENT_VERSION`, equipment offsets, shop table, start-room table, music tables, Faerie-scroll patch addresses, `RELIC_NAMES`, equip slot RAM addresses, `EXTENSIONS` (which locations each `item_pool` includes). |
| `data/io_items.py` | 9574 | Python port of sotn.io `items.js`: every item with its tiles (enemy drops, candles, shop, etc.) and the bin addresses and entity offsets for each. Used by `randomize_drop`/`randomize_candles`. |
| `Enemies.py` | 5357 | `enemy_dict` (drop addresses and rates, used by `modify_drop`), `enemy_stats_list` (EnemyDef offsets, used by `enemy_stat_rando`), attack and weakness type lists. `DropData`/`Global_drop` are **unused**. |
| `ErrorRecalc.py` | 258 | EDC/ECC recompute of the changed sectors after patching. Only matters for a disc image. |
| `Candles.py` | 1313 | `CandleData` table. **Unused**: nothing imports it. |
| `Traps.py` | 239 | BizHawk trap code (ice floor, fall damage, Axe Lord, etc.). **Unused and broken**: it imports `trap_table`, `base_item_id` and `item_table` from Items.py, which no longer exist. Nothing imports Traps.py. |
| `helper.py` | 550 | A duplicate of Zones plus `rom_offset`, with a stray top-level `print`. **Unused.** |
| `docs/*.md` | | Player docs. Some are out of date (see section 7). |
| `archipelago.json` | | `world_version 0.8.16`, `minimum_ap_version 0.6.0`. |

---

## 3. How the seed is applied to the game

### 3.1 Pipeline
- `SotnWorld.generate_output` (`__init__.py:217-223`) creates `SotnProcedurePatch(player, player_name)`, calls `write_tokens(self, patch)`, and saves `<base>.apsotn`.
- `SotnProcedurePatch` (`Rom.py:35-107`) sets `hash = USHASH = acbb3a2e4a8f865f363dc06df147afa2`, which is the MD5 of *Castlevania - Symphony of the Night (USA) (Track 1).bin*. Track 2's MD5 is `8f4b1df20c0173f7c2e6a30bd3109ac8` (`Rom.py:29-30`). The procedure is a single step, `apply_tokens(token_data.bin)`.
- When the player opens the patch (`patch()`, `Rom.py:50-107`):
  1. It checks that `options.json["version"] == CURRENT_VERSION` and exits with a message box otherwise.
  2. It applies the tokens to a copy of Track 1 and renames the result to `<name>.bin`.
  3. It copies Track 2 if missing and writes a 2-track `.cue`.
  4. It runs `ErrorRecalculator(calculate_form_2_edc=False)` against the base file to fix EDC/ECC.
  
  No PPF or xdelta is involved, and nothing is shelled out.
- **The generator never reads the ROM.** It is only read when the player patches (`get_source_data`, `get_base_rom_bytes`).
- sotn-randomizer (JS) is not used. The comments credit Wild Mouse (the sotn.io author), MottZilla, eldri7ch, Forat Negre, TalicZealot and CRAZY4BLADES. The code is Python ports of sotn.io functions such as `applyAccessibilityPatches`, `randoFuncMaster`, `replaceBossRelicWithItem`, start-room rando, colour rando and enemy-stat rando.

### 3.2 Addressing: disc offset to RAM (checked with a test script, section 8.5)
- For stage and boss overlays, `rom_offset(zone, addr) = zone.pos + addr + floor(addr/0x800)*0x130` (`Rom.py:193-194`). `addr` is the byte offset inside the stage `.BIN`, and **every stage and boss overlay is loaded at 0x80180000**, so the runtime address is simply `0x80180000 + addr`. Example: the Ring of Vlad routine's `j 0x801ac868` matches its file offset.
- Raw offsets below 0x160000 that are written directly are in **DRA.BIN**, which starts at LBA 299 (data at bin `0xABB28`) and loads at `0x800A0000`. RAM = `0x800A0000 + (sector-299)*2048 + (bin%2352 - 24)`. I checked this against three anchors in the code:
  - Joseph's-cloak routine: bin `0x158c98` = RAM `0x80136C00`, as the code says.
  - rando_func_master: `jal 0x800E2E98` points at its own bin `0xF87B0`.
  - Infinite wing smash "@ RAM 1173c8": bin `0x134990` = `0x801173C8`.
- Several writes are made twice, at bin X and at X-0x4298798. The low copy is DRA.BIN; for example bin `0xF4CE4` = RAM `0x800DFAEC`, which is what the client reads. The high copy, at bin `0x438xxxx`–`0x439xxxx` just before ARE, is another file that I couldn't identify without the disc, probably `ST/SEL/SEL.BIN`. The RAM copy the client uses is DRA.
- `0x3711A50` (MSF 05:29:26, LBA 24551) is an unused area of the disc. `rando_func_master` stores extra code there and a loader CD-reads it into RAM `0x800988B0` (14 sectors).
- A few features write to files I couldn't map because I had no disc: map colour, magic-vessel graphics, Hydro Storm colour, and parts of the Richter and Maria palettes.

### 3.3 What is written for each location (`write_tokens`, `Rom.py:762-1013`)
Locations whose item belongs to your world (`Rom.py:802-933`):

| Location kind | What is written |
|---|---|
| Normal floor or vase item (`index`) | u16 tile id at stage item table `0x80180000 + zone.items + 2*index` (`write_tile_id`, `Rom.py:265-269`). Tile value = id+0x80 for equipment and usables, id-300 for relics, id-400 for vessels (Heart=12, Life=23) (`tile_value`, `Rom.py:223-242`). |
| Item spot now holding a relic | Both layout entries of the entity (x-sorted and y-sorted lists): `id = 0x000B` (relic entity), `state = relic index`, plus `as_relic` x/y fix-ups (`write_entity`, `Rom.py:245-262`). |
| Relic spot now holding a relic | Entity `state = new relic index`. |
| Relic spot now holding an item | Entity `id = 0x000C` (item tile), `state = loc.index`, plus the tile id in the item table. |
| Bat card / Skill of wolf globes (NZ0) | u16 relic id at `addresses` (data at `0x80180FA0/2`). |
| Vlad relics, relic to relic | Entity state plus the boss overlay's `rewards` table (RBO2/3/4/7), `Rom.py:833-842`. Ring of Vlad instead writes to `ids.addresses` (RNZ1 `0x80182598` data, plus two **instruction immediates** at `0x801A72FC` and `0x801AC85C`). |
| Vlad relics, relic to item | `replace_boss_relic_with_item` (`Rom.py:502-582`) and `replace_ring_of_vlad_with_item` (`585-675`). These inject MIPS (section 8.3). |
| Librarian "Jewel of open" slot | relic to relic: `replace_shop_relic_with_relic` (`292-323`). Otherwise `replace_shop_relic_with_item` (`326-499`), which injects MIPS. The chosen item id is also saved at DRA `0x800DFD42` for the client (`792-793`). |
| Holy glasses (Maria, CEN) | item: u16 id at bin `0x456e368`, which is an **immediate inside `EntityPlatform`** (RAM `0x8018FEA0`). relic: `replace_holy_glasses_with_relic` (`272-289`). |
| Gold ring (Succubus, NO4) | item: u16 at `addresses`. relic: `replace_gold_ring_with_relic` (`678-727`), injected MIPS. |
| Trio (RARE/RBO0) | `replace_trio_with_relic` (`730-749`) / `replace_trio_relic_with_item` (`752-759`). |
| Breakable walls (`no_offset`) | Raw item id (no +0x80) at `addresses`. In NO1, NO3, NP3, RNO1 and RNO3 these are **immediates inside entity code** (e.g. `EntityMermanRockLeftSide`, `EntityStairwayPiece`). In NZ1 and RNZ1 they are a data table. Relics can't go here, and vessels are written as 0 (`Rom.py:875-889`). |
| CHI turkey (ap 40) | Normal +0x80 tile id at a data address (`Rom.py:925-927`). |
| Boss drops (13, `boss_locations`) | u16 tile id at `bin_address`, in the boss overlay's data (`929-931`). |
| Enemysanity | Nothing is written in the stage. Item ids go into the DRA table (3.5) and the client grants them. |

`randomize_items: true` also shuffles the vanilla items of every location that is **not** in the AP pool (`Rom.py:1290-1335`), using the world's RNG.

### 3.4 How items from other worlds look in game (`Rom.py:934-1013`)
The placeholder depends on the kind of location. The colour comes from the classification of the item being sent.

| Location kind | Placeholder | Tile value |
|---|---|---|
| Normal item-table spot, relic spot turned into an item tile, Gold ring | **money bag** | `0x07` = $400 blue bag if progression or progression_skip_balancing; `0x03` = $25 red bag if useful; `0x04` = $50 yellow bag otherwise. Picking it up also gives that much gold. |
| Breakable walls (no_offset) and Holy glasses | **Secret boots** (id 257) | raw `0x0101` |
| Boss drops, CHI turkey, Vlad-relic bosses, Ring of Vlad, Trio | **Secret boots** | `0x0181` (257+0x80) |
| Librarian slot | The shop sells Secret boots | |
| Enemysanity | Nothing | table entry `0xFFF` |

`docs/en_*.md` still says remote items are an "orange bag". The setup doc and the code say red, yellow or blue.

### 3.5 Data passed to the client through DRA's Time Attack label strings
DRA holds the Time Attack menu labels ("First Maria meeting", "Defeat Galamoth", and so on) at `0x800DFAEC..`. The world overwrites them with its own data and keeps the `FF 00` string terminators. Every write also goes to the second copy at DRA bin + `0x4298798`.

| RAM | Written by | Content |
|---|---|---|
| `0x800DFAEC` +0..9 | `write_seed` (`Rom.py:3481-3579`) | AP seed name (20 decimal digits) as BCD, 10 bytes |
| `0x800DFAF6` +10..11 | | slot number, big-endian u16 |
| `0x800DFAF8` +12 | | "sanity" bits: bit0 enemysanity, bit1 enemy_scroll, bit6 auto_heal, bit7 death_link (`Rom.py:1364-1372`) |
| `0x800DFB04` / `0x800DFB24` / `0x800DFB44` | | slot name in UTF-8 across 3 lines of 30, 30 and 20 bytes, ended by CR LF |
| `0x800DFB58`..`0x800DFC4B` | `Rom.py:1153-1288` | enemysanity item table. 141 entries in `ENEMY_LOCATIONS` order, **12-bit packed** (two ids per 3 bytes, `items_as_bytes`, `Rom.py:3582-3591`). Value = AP item id, or `0xFFF` if the item belongs to another world. Split across 12 label slots with `FF 00` between them. |
| `0x800DFCDC`..`0x800DFD3A` | `Rom.py:1017-1141` | relic placement. For relic index r (0..27, where r = id-300 if <23 else id-302, skipping the two JP-only cards) the value is the AP location id holding that relic in this world, or 0xFFF. It stores `local_relics` (first copy) and `copy1_relics` (second copy, used when easy enemysanity duplicates relics). |
| `0x800DFD34` | `Rom.py:1143-1151` | item at the Doppleganger 10 location (big-endian u16, 0xFFFF = other world) |
| `0x800DFD42` | `Rom.py:792-793` | item id at the librarian slot (big-endian in DRA, little-endian in the second copy, which doesn't match) |

A side effect is that the in-game Time Attack screen shows garbage text.

---

## 4. The BizHawk client (`client.py`)

### 4.1 Lifecycle
- `SotNClient(BizHawkClient)`: `game="Symphony of the Night"`, `system="PSX"`, `patch_suffix=".apsotn"` (`client.py:39-42`). It uses BizHawk's **MainRAM** domain, so the offsets below plus `0x80000000` give PSX addresses. The unused `play_sfx` and `patch_dracula_door` use "System Bus".
- `validate_rom` (`74-110`):
  1. Reads `0x80009334` for 11 bytes and expects `SLUS_000.67`.
  2. Reads 12 bytes at `0x800DFAEC`. All zero means the game isn't loaded yet. The encoded text "First Maria meeting" (`26 49 52 53 54 00 2D 41 52 49 47 00`) means a vanilla ROM, which is an error.
  3. Otherwise sets `items_handling = 0b101` and `want_slot_data = True`, adds the `/missing` command, calls `read_options`, and turns on DeathLink if bit 7 of the sanity byte is set.
- `read_options` (`1061-1119`) reads 108 bytes at `0x800DFAEC` and decodes the seed, slot number, sanity byte and slot name. It **sets `ctx.username`/`ctx.auth` from the name stored in the ROM**, so the player never types a slot name.
- `game_watcher` (`112-812`) runs every tick after authentication. `populate_once` (`828-1059`) runs once per game load, when the "past intro" flag is 1 and a zone is known.

### 4.2 Every RAM address the client uses

"CF" = `g_CastleFlags` (`0x8003BDEC`, 0x300 bytes, saved to the memory card). Symbol names come from the recomp's `config/funcmaps/*.json` and `wrapers/*.cs`.

| MainRAM | PSX | Size | R/W | Symbol / meaning | What the client does with it | client.py |
|---|---|---|---|---|---|---|
| 0x009334 | 0x80009334 | 11 | R | boot EXE name | must be `SLUS_000.67` | 81-84 |
| 0x0DFAEC | 0x800DFAEC | 12 / 108 | R | DRA Time Attack strings (AP data block) | vanilla or patched check; seed, slot, sanity, name | 88-107, 1061-1119 |
| 0x0DFB58 | 0x800DFB58 | 244 | R | ditto | enemysanity item table | 870-982 |
| 0x0DFCDC | 0x800DFCDC | 95 | R | ditto | relic placement tables | 828-868 |
| 0x0DFD34 | 0x800DFD34 | 2 BE | R | ditto | Dopp10 item | 404-408 |
| 0x0DFD42 | 0x800DFD42 | 2 BE | R | ditto | librarian-slot item | 369-371 |
| 0x03C774 | 0x8003C774 | 2 | R | `g_api` +0 (low half of `g_api.o.Update`) | **current zone** via `AREA_FLAG_TO_ZONE` (Appendix A) | 120, 125-127, 388 |
| 0x180000 | 0x80180000 | 2 | R | first u16 of the loaded stage overlay (same value) | death logic: equal to the died zone's `area_flag` means still in the stage (state was loaded); 0 means game over | 251-266 |
| 0x073084 | 0x80073084 | 2 | R | `g_Tilemap`+0 low half (pointer into the room layout) | **room fingerprint**. `0x9470` = librarian room, `0x9870`/`0x9C70` = rooms to the right, `0x6CE0` = Dracula's room | 119, 158-178, 305 |
| 0x03BE20 | 0x8003BE20 | 1 | R | CF[0x34] (not named in the recomp) | ==1 is treated as "past the intro". Gates `populate_once` and everything else | 121, 191 |
| 0x03BEE2 | 0x8003BEE2 | 10 | R/W | CF[0xF6..0xFF] (taken over by AP) | seed saved in the save file. Written on the first run in Castle Entrance, compared otherwise (a mismatch shows "Seed mismatch" and stops processing) | 200-220, 320-322 |
| 0x03BF04 | 0x8003BF04 | 2 LE | R/W | CF[0x118..0x119] (taken over by AP) | **received-item index** (`ITEM_SAVE`) | 36, 291-294, 352-354 |
| 0x03C9A4 | 0x8003C9A4 | 1 | R | `g_GameEngineStep` | ==1 means normal gameplay (required before granting items) | 222, 318 |
| 0x09794C | 0x8009794C | 1 | R | recomp map calls it `g_GpuUsage_env`; client calls it `pause_screen` | !=0 means not paused (required before granting items) | 223, 318 |
| 0x073404 | 0x80073404 | 1 | R/W | `PLAYER.step` (g_Entities[0]+0x2C) | 16 = dead, 17 = fairy revive. **Writes 0x10 to kill** for DeathLink | 224, 235, 239, 246-247 |
| 0x097BA0 | 0x80097BA0 | 4 | R/W | `g_Status.hp` | set to 0 for DeathLink; alive check; auto-heal; Life Vessel | 234, 254, 300, 800, 1742-1745 |
| 0x097BA4 | 0x80097BA4 | 4 | R/W | `hpMax` | auto-heal; Life Vessel +5 | 799, 1742-1745 |
| 0x097BA8/AC | 0x80097BA8/AC | 4+4 | R/W | `hearts`/`heartsMax` | Heart Vessel +5/+5 | 1733-1739 |
| 0x097BB0/B4 | 0x80097BB0/B4 | 4+4 | R/W | `mp`/`mpMax` | auto-heal | 801-802 |
| 0x03C708 | 0x8003C708 | 1 | R | `D_8003C708` (recomp `CanSaveAddr`) | `&0x20` means in a save room (auto_heal) | 796-798 |
| 0x076ED6 | 0x80076ED6 | 2 | R | `g_Entities[80].hitPoints` | Dracula HP: 0 or >60000 means dead (goal) | 298-299 |
| 0x072EFC | 0x80072EFC | 1 | R | `g_Player` demo timer (recomp `DemoTimerAddr`) | !=4 means the player didn't leave with the Library card (goal) | 301-304 |
| 0x03BE45 | 0x8003BE45 | 1 | **W** | CF[0x59] | writes 1 on entering Cave (RCHI), which breaks the demon wall | 308-315 |
| zone `loot_flag` | 0x8003BEEC… | 2–5 LE | R | CF[0x100+] per-stage item-pickup bits (Appendix A) | normal locations: bit `index` | 278-279, 492-493, 1005 |
| `break_flag` | 0x8003BDFE, BE04, BE1F, BE24, BE27, BE3D, BE8F, BE97 | 1 | R | CF bytes | breakable-wall locations (`& break_mask`) | 393-395 |
| 0x03BEC4 | 0x8003BEC4 | 1 | R | CF[0xD8] | Holy glasses given (`&1`) | 433-436 |
| `kill_time` | 0x8003CA2C–0x8003CA7C | 2 | R | `g_Settings.timeAttackRecords[i]` (base 0x8003CA28; i = TimeAttackEvent) | boss and Vlad-relic locations: !=0 means the boss is dead | 396-399, 1015-1018 |
| 0x097964+r | 0x80097964.. | 1 (30) | R/W | `g_Status.relics[r]` | value 1 or 3 means owned (relic checks). The grant writes 3, or **1 for the cards** (ids 318-322) | 1028, 1122-1137, 1726-1731, 1757-1764 |
| 0x09798A+id | 0x8009798A.. | 1 | R/W | hand counts (id 0-168) and body counts (169+) | grant = qty+1; librarian purchase detection | 374, 1747-1755 |
| 0x09798B | 0x8009798B | 258 | R/W | same array (whole) | `sort_inventory` | 1245-1280 |
| 0x097A8E | 0x80097A8E | 258 | R/W | hand/body **order** arrays | `sort_inventory` moves a new item into the first free visible slot | 1242-1280 |
| 0x097973 | 0x80097973 | 1 | R | `relics[15]` Faerie scroll | enemysanity gate | 504, 990 |
| 0x097A30 | 0x80097A30 | 1 | R | Library card count | soft-lock escape check | 1140 |
| 0x09796B/6C/70/71 | | 1 | R | Form of mist / Power of mist / Gravity boots / Leap stone | soft-lock escape check | 1127-1137 |
| 0x03BF7C | 0x8003BF7C | 19 | R/W | CF[0x190..] **bestiary** bits (bit = game_id-1) | enemysanity checks. **Erased once** when Faerie Scroll is picked up, keeping boss bits (`508-792`) | 468, 993 |
| 0x03BECF, 0x03BED1–0x03BEE1 | | 1 | R/W | CF[0xE3], CF[0xE5..0xF5] (taken over by AP; CF[0xE4] = SecondClockRoomDoor is skipped) | enemysanity "item already granted" bits | 469-481, 1047-1056, 516-791 |
| 0x097C30 | 0x80097C30 | 12 | R | `g_Status` play timer | only used by the unused trap code | 815-821 |

Unused client code:
- `process_traps`/`process_multiple_traps` (1282-1692) use `self.traps`, `apply_trap` and `restore_ram`, which are never defined. They touch `0x03BF21` (trap index), `0x1375BC`, `0x0978F8` (g_MenuStep), `0x03C8B8` (g_PauseAllowed) and `0x097C00/04/0C`.
- `play_sfx` uses System Bus `0x80139000`/`0x801390DC`.
- `check_talisman` reads `0x097C14`, `0x097C18` and `0x097A86`.
- `check_completion` uses old 127xxxxxx location ids.
- `patch_dracula_door` reads `0x03BED0` and writes System Bus `0x801C132C`.
- `elapse_time` is only used by the trap code.

### 4.3 Game-state detection
- **Zone**: `area_flag` u16 at `0x8003C774` looked up in `AREA_FLAG_TO_ZONE`. A KeyError means `cur_zone=None` (title screen, cutscene, etc.). The zone name is sent to data storage key `sotn_zone_{slot}` and the room fingerprint to `sotn_room_{slot}` (`128-170`, for trackers).
- **Room**: u16 at `0x80073084`.
- **In game**: `cur_zone` known **and** CF[0x34]==1 (`191`).
- **Safe to grant items**: also needs `g_GameEngineStep==1` and `0x8009794C != 0` (`318`).
- The recomp already has better sources: `g_StageId` `0x800974A0`, RoomX/Y `0x800730B0/B4`, `GameStateAddr 0x8003C734`, `MenuOpenAddr 0x800973EC` (`wrapers/Game.cs:55-70`, `Stage.cs:96-119`).

### 4.4 Location-check detection (`356-493`, and the one-time version in `populate_once` `984-1059`)
Each tick the client checks only the locations of the current zone (`ZONE_LOCATIONS[cur_zone]`), in this order:
1. `bin_addresses` (breakable walls): `CF byte & mask`.
2. `kill_time`: time-attack record != 0. This covers the 13 boss drops, the 4 Vlad-relic bosses, Ring of Vlad and Trio. Killing Dopp40 (397) also checks Dopp10 (388) and grants the Dopp10 item from `0x800DFD34` if Dopp10 was never checked (`400-426`).
3. Librarian slot: if the slot holds a relic, check that relic's byte. If it holds an item, watch the item's inventory count while in the librarian room (`0x9470`) and treat an increase as a purchase (`366-386`, `427-432`).
4. Holy glasses: CF `0x8003BEC4 & 1`.
5. Relic present in this world's relic table: `g_Status.relics[r]` is 1 or 3. This also checks the location of the second copy and any enemysanity location holding the same relic.
6. Enemysanity (only when `enemy_scroll` is off or Faerie Scroll is owned): the bestiary bit. If the item belongs to this world the client grants it and sets the "granted" bit (`461-490`).
7. Anything else: stage `loot_flag` bit `index`. Relic spots that now hold an item also use this, via the item tile.

It skips a location if the area changes mid-loop (`387-390`). While in the librarian room only the librarian locations are checked (`361-364`). After receiving a **relic from another world**, the client auto-checks the local location of that relic (and its copy and any enemysanity location), because a relic entity you already own won't spawn (`339-349`). `LocationChecks` with the full list is sent whenever the list changes (`495-499`).

### 4.5 How received items are given (`317-354`, `grant_item` `1694-1755`)
- Loop: for each `items_received[i]` with `i+1 > ITEM_SAVE`, queue it, then **write ITEM_SAVE = i+1 straight away**. The queued items are actually granted at the top of the *next* tick (`327-332`), and only when "safe" (4.3). Messages go to the BizHawk on-screen display.
- Items 1–258: `qty = [0x8009798A+id] + 1` (max 255). For a first copy (qty 0 to 1) the item goes into `new_items` and `sort_inventory` rewrites the count and order arrays so the item appears.
- Relics 300–329: `relics[id-300] = 3`, or `1` for cards 318-322, only if currently 0.
- Heart Vessel 412: hearts +5 and heartsMax +5. Life Vessel 423: hpMax +5 and hp = new max.
- Ids 330–369 (XP boosts, max/restore) are still handled in code, but those items no longer exist in `Items.py`.
- In the recomp, the game's own `g_api.AddToInventory(id, kind)` (`wrapers/GameApi.cs:43,92`) and `Inventory.AddHandItem`/`AddBodyItem` can replace the count and order poking.

### 4.6 Received-item index
It is a u16 at `0x8003BF04` (CF[0x118]), inside the castle flags, so it is **saved with the game** and restored on load. The client also compares it with its own copy every tick (`291-294`) to notice a load-state or reload.
Risk: the index is written before the item is granted. A disconnect or crash between those two steps loses the item.

### 4.7 DeathLink (`226-274`)
- **Receiving**: if `DeathLink` is in tags, `PLAYER.step != 16` and there is a new death timestamp, the client writes `hp=0` (`0x80097BA0`) and `PLAYER.step=0x10` (`0x80073404`).
- **Sending**: `step==16` sets a died flag. step 17 means the fairy revived the player (cancel). If `u16@0x80180000 == 0` (the overlay was unloaded, i.e. game over), the client sends a death with the cause "Alucard is not strong enough", unless the death came from DeathLink. If the value equals the died zone's `area_flag` and HP != 0, it assumes a load-state and resets.
- TODOs in the file: "Death link stuck at death", "Deathlink mid wingsmash softlock the game" (`16-21`).

### 4.8 Goal (`296-306`, `804-807`)
All of these must be true:
- zone is RBO6 "Shaft/Dracula"
- Dracula HP `0x80076ED6` is 0 or >60000
- Alucard HP != 0
- `0x80072EFC != 4`
- room `0x6CE0`

Then the client sends `StatusUpdate CLIENT_GOAL`. On the world side, completion needs the event location "Reverse Center Cube - Kill Dracula" (`sotn_has_dracula`: Holy glasses, flying, and all 5 Vlad relics).

### 4.9 Other behaviour
- **auto_heal** (bit 6): in a save room, set HP=max and MP=max (`795-802`).
- **Cave demon wall**: set CF 0x8003BE45 on entering RCHI (`308-315`).
- **Soft-lock escapes**: going from Underground Caverns to Abandoned Mine, or from Marble Gallery to Center Cube, without bat, mist+power, gravity+leap or a Library card gives a free Library card (`140-151`, `can_escape` `1121-1144`).
- **Faerie-scroll gate for enemysanity**: when Faerie scroll is first seen, the bestiary is erased except for boss and Warg bits, and the boss bits already set are checked and granted (`501-792`).
- **`/missing`** console command (`1776-1798`).

---

## 5. ID schemes

**Items** (`Items.py`). AP id = game id, with **no base offset** (Appendix B).
- 1–258: hand and body inventory items. Ids 169, 195, 217 and 226 are missing (the "empty" slot entries), which leaves 254 items. Tile value in game = id+0x80.
- 300–329: relics. 323 and 324 (JP-only cards) are missing, which leaves 28. Relic index = id-300, and the relic byte is at `0x80097964+(id-300)`.
- 400 Victory (event), 401 "Boss token" and 402 "Exploration token" (both unused).
- 412 Heart Vessel, 423 Life Vessel.

That is 287 names. The pool uses: 4 progression items (Spike breaker, Holy glasses, Gold ring, Silver ring), the 28 relics, then the vanilla items of the active locations. Filler is Orange, Apple, Banana, Grapes, Strawberry, Pineapple, Peanuts and Toadstool (`__init__.py:89-194`).

**Locations** (`Locations.py`). AP id = `ap_id`, with no base offset:
- **1–399**: normal locations (386 "full pool" locations plus the 13 boss drops 387–399).
- **400–540**: enemysanity (141).
- The event "Reverse Center Cube - Kill Dracula" has id None.

How many locations exist depends on the options:

| item_pool | Locations |
|---|---|
| `relic_prog` | 33 |
| `guarded` | 37 |
| `equipment` | 102 |
| `full` | 386 |

`boss_locations` adds 13 and `enemysanity` adds 141 (`Regions.py:505-518`, `Constants.py:1139-1248`).

Tables that map AP locations to in-game flags:
- `Locations.py` itself (Appendix C lists every entry with its detection method).
- `data/Zones.py` (Appendix A): per-stage `loot_flag` address and size, item-table offset, `area_flag`.
- Enemysanity uses `game_id`, which gives the bestiary bit.

---

## 6. Options (`Options.py`)
"Logic" means it only affects generation. "Game" means it writes to the disc or changes client behaviour.

| Option | Values | Effect |
|---|---|---|
| open_no4 | closed / open (after Alchemy Lab) / open_early | Logic and game. Patches the branch in NP3 (and NO3 for open_early) of `EntityCavernDoor`, i.e. the check on castle flag 0x30 (EntranceToCaverns) (`Rom.py:1347-1352`). |
| open_are | toggle | Logic and game. Patches the ARE `EntityCavernDoor` branch, flag 0xB1 ColosseumToChapel (`1354-1355`). |
| item_pool | relic_prog / guarded / equipment / full | Logic: which locations exist (EXTENSIONS). |
| infinite_wing_smash | toggle | Game: NOPs the wing-smash timer in DRA `ControlBatForm` `0x801173C8`. |
| randomize_items | toggle | Game: shuffles vanilla items of locations not in the pool (world RNG). |
| powerful_items | toggle | Pool: swaps up to 7 vanilla pool items for Duplicator, Crissaegrim, etc. Also used by randomize_items. |
| boss_locations | toggle | Logic: adds the 13 boss-drop locations. |
| enemysanity | toggle | Logic, plus a client bit. Adds 141 locations and extra pool items (`__init__.py:142-183`):<br>easy: second copy of all 28 relics and the 4 progression items, plus 50 random equipment/usable items (ids 1-258) and 50 vessels<br>normal: second copy of the 4 progression items, plus 35 + 35<br>hard: 15 + 15<br>Whatever space is left is filled with fruit filler. |
| enemy_scroll | toggle | Client bit: enemysanity checks only count once Faerie Scroll is owned (the item becomes progression). |
| difficult | easy / normal / hard / very_hard | Game. Enemy HP/ATK/DEF ×0.5/1/1.5/2, shop prices, and easy sets drop_mod=3 (**bugged**, section 7). Enemysanity extra-item counts. |
| enemy_mod | 24..200 (24 = off) | Game: HP/ATK/DEF multiplier that overrides difficult. |
| drop_mod | normal / increased / abundant / guaranteed | Game: drop rates 64/32 or 128/64, or guaranteed via code patch. |
| rng_start_gear | toggle | Game: random starting equipment (patches InitStatsAndGear immediates and the Death cutscene). |
| death_link | toggle | Client bit. |
| remove_prologue | toggle | Game: skips the Richter prologue and resets time-attack records so bosses spawn. |
| map_color | 10 choices | Game (cosmetic). |
| alucard_palette | 8 choices | Game (cosmetic). |
| alucard_liner | 5 choices | Game (cosmetic). Always written, even when left at the default. |
| magic_vessels | toggle | Game: Heart Vessels become MP vessels (code plus graphics). |
| anti_freeze | toggle | Game: no screen freeze on level-up. |
| my_purse | toggle | Game: Death doesn't take your gear. |
| fast_warp | toggle | Game: faster warp-room animation. |
| unlocked_mode | toggle | Game: opens 5 first-castle shortcuts and 1 in the second castle (may break logic). |
| relic_suprise | toggle | Game: every relic uses the same sprite and palette (note the spelling of the option key). |
| enemy_stats | toggle | Game: random enemy HP/ATK/DEF (25–200%), elements, weaknesses and resistances, plus bestiary text. |
| random_shop | off / on / on_lib / on_no_prog / on_no_prog_lib | Game: random librarian stock (the `_lib` choices force a Library card). |
| shop_prices | toggle | Game: prices 50–150%. |
| starting_zone | vanilla / normal_castle / reverse_castle / any_castle | Game: after the first Warg, teleports you to a random room (may break logic). |
| reverse_library | toggle | Game: hold Down while using a Library card to warp to the reverse library (after Richter is saved). |
| random_music | toggle | Game (cosmetic). |
| skip_nz1 | toggle | Game: one hit on any Clock Tower gear opens the door (both castles). |
| no_logic | toggle | Logic only. |
| auto_heal | toggle | Client bit: heal in save rooms. |
| color_randomizer | toggle | Game (cosmetic): capes, Joseph's cloak, grav boots, Hydro Storm, wing smash, Richter, Maria, Dracula's cape. |
| randomize_drop | 0..10 | Game: shuffles enemy drops (simple, type or full, with or without global drops, with or without progression items). |
| randomize_candles | 0..4 | Game: shuffles candle contents (Stopwatch candles and ST0 are left alone). |
| accessibility, start_inventory | | Standard AP options. |

There is no goal option. `goal` only shows up in commented-out code (`Rom.py:1361-1362`).

---

## 7. Known issues, soft-lock notes and TODOs

Bugs confirmed with the test script (`py -3.12` against the real `write_tokens`):
1. **Easy difficulty removes all enemy drops.** `write_tokens` sets a local `drop_mod = 3` for easy but then calls `modify_drop(options_dict["drop_mod"])`, which is 0. That takes the else branch and writes `64*0` and `32*0` to 220 drop-rate halfwords, 110 EnemyDefs in DRA from `0x800A931E` on (`Rom.py:1379-1404`, `2255-2272`). It only happens when the player leaves `drop_mod: normal`.
2. **`tile_filter` edits the shared `io_items` list** (`data/io_items.py:9509-9522`: `new_item = item; new_item["tiles"] = temp_tiles`). With `randomize_drop` on, `randomize_candles` then only sees 188 of 2666 candle writes. Because `io_items` is module-global, it also carries over between two SOTN slots generated in the same process (inferred from the code, not tested).
3. `randomize_drop`'s progression exclusion list uses "Spike breaker", "Gold ring" and "Silver ring", but `io_items` spells them "Spike Breaker", "Gold Ring" and "Silver Ring". Those three can still appear as drops (`Rom.py:3328`, `3356`). Holy glasses is spelled correctly.
4. `shop_item_data` entries 13 and 14 both use `priceAddress 0x047a3100`, so one shop slot is written twice and another never (`data/Constants.py:82-93`).
5. `Rules.py:413-416` builds a lambda inside a `for relic` loop, so all five rules test "Eye of vlad". This is harmless because 418-420 adds `sotn_has_dracula`.
6. The jewel-item u16 is big-endian in the DRA copy and little-endian in the second copy (`Rom.py:792-793`).
7. In the `rando_func_master` block loaded to `0x800988B0`, the function at `0x800988C8` restores `ra` with `lw ra,0xA(sp)` (`0x8FBF000A`) at `0x80098900`, an unaligned load after `sw ra,0x10(sp)`. That entry (`0x800988B8`) is never called in this world. Only the `reverse_library` entry `0x800988BC → 0x8009890C` is used, so it is harmless here.
8. `start_room_rando` mixes decimal and hex stage limits: `>= 20` for castle filtering but `>= 0x20` for the reverse-castle code (`Rom.py:2280-2290`).

TODOs and soft-lock notes in the code:
- `client.py:16-21`: visual glitches in Richter dialog; progression items (links to a Discord post); "Death link stuck at death"; stopping the wrong-version yaml; "Deathlink mid wingsmash softlock the game". Ideas: lock red doors, Chairsanity, no-logic rules.
- `client.py:228-230`: DeathLink doesn't check whether it is safe to die.
- `client.py:485`: "TODO This must be tested" (relic copy from enemysanity).
- `__init__.py:144`: TODO for an option to size the extra enemysanity locations.
- `Rom.py:886`, `909`: TODOs to add traps and boosts to no_offset and Gold ring locations.
- `Rom.py:1804`: TODO to rename the skip_nz1 option.
- `Rules.py:187`: "TODO Jewel might need some restrictions Green tea RCHI too".
- `docs/en_*.md`: TODOs for another goal, traps, more remote-item looks, and looted instead of instant items.

Soft-lock handling already in place:
- The patch always skips Maria's dialog after Hippogryph.
- The accessibility patches fix Clock Room, Alchemy Lab cutscene, Power of Sire flashing, Clock Tower gate, Olrox death, Scylla door, Minotaur/Werewolf, gold/silver ring softlock and "Clear Game" status (`Rom.py:114-190`).
- The client gives a free Library card when you could be trapped (4.9).
- The client breaks the Cave demon wall.
- `start_room_rando` fixes Abandoned Mine tiles (`Rom.py:2391-2452`).

Rules restrictions that exist because of engine limits:
- No relics on boss drops, no_offset walls or the CHI turkey.
- No vessels on Gold ring, Vlad-relic spots, Jewel, Trio, Holy glasses or no_offset walls.
- No progression items on `TOP_Turkey_1`, because the player can break it with a spell and lose it (`Rules.py:53-97`, `140-186`).
- Docs: "Progression items are banished from de-spawn spots…" and "I broke a wall and couldn't get the drop before it vanish" (the item is lost if it was yours).

---

## 8. Porting with no disc patch: what each change becomes at runtime

### 8.1 What `fill_slot_data` returns today
`__init__.py:211-215`:
```python
option_names = [n for n in self.options_dataclass.type_hints if n != "plando_items"]
return self.options.as_dict(*option_names)
```
That is **only option values** (`Option.value`; OptionSets come back as sorted lists). `type_hints` includes the fields inherited from AP's `CommonOptions`/`PerGameCommonOptions` (fork `Options.py:1387-1389`, `1714-1723`), so the keys are:
- AP common options: `progression_balancing`, `accessibility` (SOTN overrides it with ItemsAccessibility), `local_items`, `non_local_items`, `start_inventory` (overridden with StartInventoryPool), `start_hints`, `start_location_hints`, `exclude_locations`, `priority_locations`, `item_links`
- SOTN options: `open_no4`, `open_are`, `item_pool`, `infinite_wing_smash`, `randomize_items`, `powerful_items`, `boss_locations`, `enemysanity`, `enemy_scroll`, `difficult`, `enemy_mod`, `drop_mod`, `rng_start_gear`, `death_link`, `remove_prologue`, `map_color`, `alucard_palette`, `alucard_liner`, `magic_vessels`, `anti_freeze`, `my_purse`, `fast_warp`, `unlocked_mode`, `relic_suprise`, `enemy_stats`, `random_shop`, `shop_prices`, `starting_zone`, `reverse_library`, `random_music`, `skip_nz1`, `no_logic`, `auto_heal`, `color_randomizer`, `randomize_drop`, `randomize_candles`

(`plando_items` is left out on purpose.) It contains no seed, no placements and no RNG results. `write_tokens` builds the same dict and adds `seed`, `player`, `player_name` and `version` for `options.json` inside the patch (`Rom.py:763-773`, `1490-1492`). The BizHawk client ignores slot_data.

### 8.2 Can the world generate without producing the patch?
- **The ROM isn't needed to generate.** It is only read when the player opens the patch.
- **The patch is always produced.** `generate_output` always builds the `.apsotn`, and there is no option to skip it. AP's `--skip_output` skips the whole output stage, including the multidata and server file (`Main.py:216`), so it doesn't help for real games. A native client can simply ignore the `.apsotn`.
- **Where the rolls happen matters.** All the "B" rolls below are made inside `write_tokens`, i.e. during `generate_output`. In AP's `Main.py:236-362`, `write_multidata`, which calls `fill_slot_data`, is submitted to the **same thread pool as `generate_output`**, so the two run at the same time. To move those results into slot_data, make the rolls earlier (for example in `post_fill`, `Main.py:203`), store them on the world, and have both `write_tokens` and `fill_slot_data` read the stored results. Don't roll inside either one.

### 8.3 Classification of every change

A = can be rebuilt at runtime from the AP server alone: slot_data options + `LocationScouts` (item id, player and flags for each own location) + static tables copied from the apworld (Locations/Zones/io_items/Enemies/Constants).
B = needs generation-time data that isn't in slot_data today. The field to add is given.
C = MIPS code: an instruction edit, an immediate operand inside a function, or injected routines. In a static recomp this has to be rewritten as a C# hook, because writing MIPS into RAM does nothing to recompiled code.
"Data RAM" addresses were checked against the recomp's function maps. Anything outside a known function counts as data, **except** blocks noted as injected code placed in unused data space.

Always applied, on every seed:

| # | Change (Rom.py) | Where (RAM) | Class | Plain description / runtime replacement |
|---|---|---|---|---|
| 1 | Maria dialog skip after Hippogryph (`777`) | BO5 `0x801A4FD4` in `func_801A4E40` → `beq zero,zero,+0xB` | C | Forces the branch that skips Maria's conversation, which could trap the player. Hook: always take that path. |
| 2 | Item tile ids, own items (`write_tile_id`) | `0x80180000+zone.items+2*idx` on stage load | A | Tile id from the scouted item (the recomp's `Randomizer.cs` `StageItemListOffset` is the same table). |
| 3 | Entity layout edits (`write_entity`): relic↔item type, state, x/y | stage layout tables (2 entries per entity) on stage load | A | Uses scouts plus `entities`/`as_relic`/`as_item`/`index` from Locations.py. |
| 4 | Remote placeholders (money bag colour or Secret boots) | same tables and addresses as 2, 3, 5, 6 | A | Colour from the scout `flags` (progression/useful). |
| 5 | Boss drop tile (13) | e.g. NZ0 `0x8018285A`, LIB `0x801831CA`, BO0 `0x801824D4` … (Appendix C) | A | Data. |
| 6 | Breakable walls in NZ1/RNZ1 and CHI turkey | NZ1 `0x80181084-90`, RNZ1 `0x80181014-20`, CHI `0x801809EA` | A | Data. |
| 7 | Breakable walls in NO1/NO3/NP3/RNO1/RNO3 and Holy glasses (item) | immediates in `func_us_801BE880` (NO1 `0x801BEAB0`), `EntityMermanRockLeftSide` (NO3 `0x801BA7CC`, NP3 `0x801B506C`), `EntityStairwayPiece` (NO3 `0x801BB0A8`, NP3 `0x801B5948`), RNO1 `func_801A7B34` `0x801A7D64`, RNO3 `func_801B2BF0` `0x801B2F04`, CEN `EntityPlatform` `0x8018FEA0` | **C** (value is A) | The item id dropped by the rock, wall or Maria is a constant in code. Hook the spawn and substitute the id. |
| 8 | Bat card / Skill of wolf globe relic ids | NZ0 `0x80180FA0/2` | A | Data. |
| 9 | Vlad relic→relic (boss reward tables) | RBO2 `0x801817B2`, RBO3 `0x801812CA`, RBO4 `0x801813D8`, RBO7 `0x80181326` + entity state | A | Data. |
| 10 | Ring of Vlad relic→relic | RNZ1 `0x80182598` (data), `0x801A72FC` (`func_801A7254` imm), `0x801AC85C` (`func_801AC7CC` imm) | A + **C** | Two relic ids are constants in code. Hook them. |
| 11 | Vlad relic→item (`replace_boss_relic_with_item` ×4) | erase instruction in the boss overlay (RBO2 `0x8019F728`, RBO3 `0x80192CBC`, RBO4 `0x80198970`, RBO7 `0x8019400C`), hook in the stage `Update` (RCHI `0x8019AF18`, RDAI `0x801B4950`, RNO1 `0x801A9FC0`, RNO2 `0x801B7014`) → injected routine (RCHI `0x801AA000`, RDAI `0x801C7900`, RNO1 `0x801B7500`, RNO2 `0x801CBF00`) + data (item table, entities, reward table) | **C** | (a) The boss no longer spawns a relic (the relic-load instruction becomes `ori v0,r0,0`). (b) An item tile spawns at the reward spot instead. (c) The hook replaces the end of the stage's `Update` (per the recomp's function map) with a `j` to the routine, which ends in `jr ra`. So whenever that code runs, if the item is already equipped in a matching slot or its inventory count is non-zero, the layout entity's type becomes 0x000F and the pickup doesn't spawn. |
| 12 | Ring of Vlad→item (`replace_ring_of_vlad_with_item`) | 3 erase edits + hook `0x801AC840` in `func_801AC7CC` → routine RNZ1 `0x801BEED0` | **C** | Once Darkwing Bat's time-attack record (`0x8003CA78`) is non-zero, move the item entity to its y position and hide it if already owned. |
| 13 | Gold ring↔relic (`replace_gold_ring_with_relic`) | hook NO4 `0x801CC590` (`CreateEntitiesToTheRight`) → routine `0x801DEBB4` + entity state | **C** | Only turn the entity into a relic (type 0x000B) after Succubus's record `0x8003CA4C` is non-zero. |
| 14 | Trio→relic (`replace_trio_with_relic`) | RBO0 reward `0x8018198C` (data), RBO0 `0x801A6088` (`EntityLifeUpSpawn`: `ori v0,r0,0`), RARE entities | A + **C** | Removes the condition on the reward-tile spawn. |
| 15 | Trio→item (`replace_trio_relic_with_item`) | as row 11: RARE Update hook `0x801A6E64`, routine RARE `0x801B8A00`, RBO0 erase `0x8019465C`, plus RARE entity slot 0x10 | **C** | Same as row 11. |
| 16 | Holy glasses→relic | CEN erase instruction `0x8018FE98` (`EntityPlatform`) + 2 layout entries → relic entity at (0x180,0x22C) | A + **C** | Stops Maria's glasses handout and places a relic entity instead. |
| 17 | Jewel slot relic→relic | LIB `0x801814D4` (shop relic id), `0x801AD088..98` (shop name string), `0x801B2B08` (`func_us_801B29C4` imm: relic id + 0x64) | A + **C** | One immediate plus data. |
| 18 | Jewel slot→item, including remote (`replace_shop_relic_with_item`) | 14 code edits in LIB `func_us_801B29C4`/`801B2BE4`/`801B420C`/`801B4ED4`/`801B56E4` + 3 injected routines `0x801D4600`, `0x801D4680`, `0x801D4700` + data `0x8018134C/4E`, `0x801814D4` | **C** | Turns the librarian's relic entry into an equipment or usable entry (type byte + id). Skips the "already owned or equipped" checks so it is always for sale, and fixes the quantity and purchase logic. |
| 19 | Jewel of open price = 10 gold (`1016`) | LIB `0x80181350` | A | Constant. |
| 20 | Seed, slot, name, sanity; relic table; Dopp10 item; jewel item; enemysanity table | DRA `0x800DFAEC–0x800DFD44` (+ second copy) | A | **Not needed at all.** Only the BizHawk client reads these. Use RoomInfo/Connected, slot_data and LocationScouts. (Keep the Dopp40→Dopp10 fallback and the relic auto-check behaviour.) |
| 21 | Accessibility patches (`apply_acessibility_patches`, `114-190`) | DRA `0x800A2984` (1 byte, data); NZ0 `0x801B7EEE` (`NZ0_EntityCutscene` → upper half = beq); DRA `0x80118C28` (`func_80118C28` → `jr ra`); NZ1 `0x80182476` (data byte), NZ1 `0x801A8C24` (`EntitySecretAreaDoor`); BO0 `0x801B4E9C` (`func_801B365C`); BO3 `0x801A094C` (`func_801A07CC`: `andi v0,v0,0xFE`), `0x801A3514` (`func_801A2AEC`); BO2 hook `0x801A6F04` (`func_801A6EF8`) → routine `0x801B5CC8`; NO0 `0x801CD33C`/`0x801CD35C` (`EntityClockRoomController`) → routine `0x801AD91C`; unknown file bin `0x04397122` | **C** (2 data bytes) | Clock Room cutscene; skip the Alchemy Lab cutscene condition; stub Power of Sire flashing; Clock Tower puzzle gate; Olrox death; Scylla door; Minotaur/Werewolf (the hook runs the routine at `0x801B5CC8`: while `PLAYER.step` (`0x80073404`) is 5, 9, 0x18 or 0x19 it sets the calling entity's `step` (`+0x2C`) back to 1, so the end-of-fight sequence waits for Alucard to be in a normal state, then runs the two replaced instructions and returns); gold/silver ring in the Clock Room (clear a flag and jump past the soft-lock); "always have Clear Game status" (likely the file-select overlay). |
| 22 | `rando_func_master(0)` (`1921-2082`) | DRA `MainGame` `0x800E3B60` → `jal 0x800E2E98` (overwrites `DebugEditColorChannel`) = CD-read of 14 sectors from LBA 24551 into `0x800988B0` | **C** (inert here) | sotn.io's "extra code" loader. In this world the loaded block is only used by `reverse_library`. Drop it and write reverse_library as a hook. |
| 23 | `alucard_liner` (always, default gold) | DRA `0x800DB1F8-FE` | A | Palette data. |
| 24 | `randomize_shop` (always called; no-op unless shop options or difficulty change it) | see row 38 | | |

Option-dependent changes:

| # | Option → function | Where (RAM) | Class | Plain description / slot_data field needed |
|---|---|---|---|---|
| 25 | randomize_items (`1290-1335`) | stage item tables and wall immediates of **non-AP** locations | **B** | `nonpool_items: {ap_location_id: item_id}` (these locations aren't AP locations, so scouts can't return them). Walls in row 7 still need the C hook. |
| 26 | powerful_items | pool (A via scouts) and the row 25 roll | A / B | Covered by row 25. |
| 27 | open_no4 | NP3 `0x801B417C` (and NO3 `0x801B9920` for open_early) in `EntityCavernDoor` | C, **or A** | The patch makes the door's check never branch. At runtime it's simpler to **set castle flag 0x30 (EntranceToCaverns)**: after the Alchemy Lab visit for `open`, at start for `open_early`. The recomp has `Progress.SetShortcut`. |
| 28 | open_are | ARE `0x801B6F84` (`EntityCavernDoor`) | C, **or A** | Set castle flag 0xB1 (ColosseumToChapel). |
| 29 | difficult / enemy_mod scaling | DRA EnemyDefs `0x800A89F0..` (hp/atk/def) + bin `0x0b9c0e..` for id 379 | A | Deterministic multiply of vanilla stats. |
| 30 | enemy_stats (`enemy_stat_rando`) | DRA EnemyDefs (atk type, weak, resist, guard/absorb), name pointer, bestiary text at `newNameText` | **B** | `enemy_stats: {enemy_id: {hp, atk, def, atk_type, weak, resist, guard or absorb}}` (or send the text too). |
| 31 | enemy_stats: "Faerie scroll force" + name text | `HitDetection` in 51 overlays: `li v0,3; nop` (e.g. ARE `0x801B91C0`). Data: bestiary text at DRA `0x800E0CC8..0x800E0D05`, name pointers in EnemyDefs (`nameOffset`), boss-name strings at `0x800E0CE0` with pointers at `0x800AC500`/`0x800A9198` | **C** + B | Acts as if Faerie Scroll were owned, so the enemy name and stats box always shows. The shown "name" is replaced by a short stats text built from the random stats (B). |
| 32 | drop_mod increased / abundant | DRA EnemyDefs rare/uncommon drop rate = 64/32 or 128/64 | A | Deterministic. |
| 33 | drop_mod guaranteed | `HitDetection` in 27 overlays (nop the failure rolls + `blez r0` always-drop) + DRA `func_800FF494` `0x800FF4C0-F0` | **C** | Every enemy that has a drop always drops. Rare or uncommon alternates with `g_Status.killCount` (`0x80097BF4`) parity. |
| 34 | infinite_wing_smash | DRA `ControlBatForm` `0x801173C8` → nop | **C** | Don't count down the wing-smash timer. |
| 35 | rng_start_gear | DRA `InitStatsAndGear` immediates `0x801001A8..E4` (starting equipment) and `0x800FF800..9CC` (Death gear strip), + NO3 data `0x80181AD4..DE` (Death cutscene sprites) | **B** + C | `start_gear: {weapon, shield, helmet, armor, cloak, accessory}`. Runtime: write `g_Status.equipment` after a new game, and hook the Death gear-strip. |
| 36 | remove_prologue | DRA hook `0x800FFDA0` → routine `0x800FFBC0` (in `InitStatsAndGear`'s space) + unknown-file byte bin `0x04392B1C` | **C** | Start the new game in NO3 instead of the ST0 prologue, and clear the 0x1B time-attack records so bosses spawn. |
| 37 | map_color | unknown file (bin `0x3874848` Alucard, `0x38C0508` Richter; plus border words) | A (address TBD) | Map colour words. Where they sit in RAM still has to be found in the recomp. |
| 38 | alucard_palette | DRA `0x800DB1F6`, `0x800DB20A-14` | A | Palette data. |
| 39 | magic_vessels | DRA `func_800FE044` `0x800FE0E8-15C` + 112 graphics writes in an unknown file (bin `0x3868268..`) | **C** + data | Rewrites part of `func_800FE044` (the g_api stat-increase routine used by vessels). For Alucard (`g_PlayableCharacter 0x8003C9A0 == 0`) it now does: `heartsMax += 5`, `hearts += 5`, `mpMax += 3`, `mp = mpMax`, and increments the u32 at `0x80137964`. For Richter it returns 1 through `0x800FE39C`. It also redraws the vessel sprite (112 graphics writes). Decoded from the written words, not from the code comment, which only says "MP Vessels". |
| 40 | anti_freeze | DRA `EntityLevelUpAnimation` `0x80121B74` (byte immediate = 0) | **C** | No freeze frames on level-up, relic or vessel pickup. |
| 41 | my_purse | NO3 `NO3_EntityCutscene` `0x801BEFB0` → `blez r0,+6` | **C** | Skip the part where Death takes your gear. |
| 42 | fast_warp | WRP `EntityWarpRoom` `0x801878B8`, RWRP `EntityRWarpRoom` `0x8018972C` (byte immediates = 2) | **C** | Faster warp animation. |
| 43 | unlocked_mode | 23 `li v0,1; nop` pairs in NO3/NP3 (`EntityCavernDoorLever`, `EntityCavernDoor`, `EntityWeightsSwitch`, `EntityPathBlock*Weight`, `EntityHeartRoomSwitch/GoldDoor`), ARE `EntityCavernDoor`, DAI `EntityBlock` + RNO3 tile removal and entity y (data) | **C**, **or A** | Makes those doors, switches and blocks read as "already opened". At runtime, **set the matching shortcut castle flags** (the recomp's `Shortcut` enum) and write the RNO3 data. |
| 44 | relic_suprise | DRA relic defs `0x800A8728..` (30 × u32 sprite/palette) | A | Data. |
| 45 | shop prices and stock (`randomize_shop`) | LIB shop table `0x80181354..` (type byte, id, u32 price) | **B** | `shop: [{slot, type, item_id, price}]`. |
| 46 | starting_zone (`start_room_rando`) | DRA routine `0x800DB9B8` (injected into data space), hooks in `func_8010FDF8` `0x80110288` and `CheckGravityBootsInput` `0x801105D8`, TOP `TOP_EntityCutscene` `0x801AC1E8` (skip Richter), NO3 `EntityTrapDoor` `0x801BA3A4`, data NO3 `0x8018045C`/`0x80183CD4`, DRA destination `0x800A2844-4C`, Abandoned Mine tile fixes | **B** + **C** | `start_room: <start_room_data key>`. After the first Warg, teleport to the chosen room (destination x/y, room and stage words at DRA `0x800A2844-4C`). For reverse-castle rooms (stage ≥ 0x20) an extra routine is injected at `0x800DB9B8`. The two hook halfwords (`0x6E6E`) retarget a `jal IsRelicActive` in `func_8010FDF8` and in `CheckGravityBootsInput` to that routine. The routine returns 1 ("relic active") when `g_StageId & 0x20` (`0x800974A0`) is set and the bytes at `0x8006BBFB` and `0x8006BCC0` are both 0. Otherwise it tail-jumps to the real `IsRelicActive` (`0x800FE3A8`). I didn't work out exactly what those two bytes mean. The Richter cutscene skip is TOP `0x801AC1E8`, and the NO3 `EntityTrapDoor` patch (`0x801BA3A4`: `li a0,0x20; sw a0,g_StageId(0x800974A0); j 0x801BA4AC`) writes 0x20 into `g_StageId` at the hatch. The code comment says it is "required for 2nd to work". |
| 47 | reverse_library (`rlib_card`) | DRA `func_8010EDB8` `0x8010F26C` → `jal 0x800988BC` → routine `0x8009890C` (in the row 22 block) | **C** | When a Library card is used, the routine reads the pad byte at `0x8003925D` (inverted, `&0x40`, i.e. holding Down per the option text) and the byte at `0x8006BBFB` (the option text says "after Richter is saved"). If both pass it uses stage 0x22 and `0x88BE`, otherwise 0x02 and `0x7C0E`. It then writes the stage byte into **code**: the immediates at `0x800F1724` (in `func_800F16D0`) and, xor 0x20, `0x800F32A4` (in `RunMainEngine`). The halfword goes to data at `0x800A3C98`, and it jumps to `func_8010E42C(0)`. So it warps to the reverse library instead of the normal one. In a recomp, hook the stage id those two functions load. |
| 48 | random_music | DRA per-stage music table `0x800A3C58..` (30 bytes, data) + 51 byte immediates in boss and cutscene code | **B** + C | `music: {area: song_id}`. |
| 49 | color_randomizer: capes | DRA `0x800A37FC–0x800A3848` (cape lining/outer colour words: Cloth, Reverse/Inverted, Elven, Crystal, Royal, Blood, Twilight), BO4 `0x801..` / RBO5 Doppelganger capes (bin `0x627984c`, `0x6894054`) | **B** | `colors.capes`. |
| 50 | color_randomizer: Joseph's cloak | hook DRA `HandlePlay` `0x800E4BA4` (`jal 0x80136C00`) → routine `0x80136C00` (injected into DRA space the funcmap doesn't cover). It stores 6 bytes to `0x0003CAA8+4i`, i.e. `0x8003CAA8..BC` inside `g_Settings` (`0x8003C9F8`+0xB0..), which look like the custom-cloak colour settings | **B** + **C** | `colors.joseph: [6 values 0-31]`. Runtime: write those 6 bytes directly once in game. The patched routine does it every time `HandlePlay` reaches that call. |
| 51 | color_randomizer: grav boots | DRA `EntityGravityBootBeam` `0x8011E1AC/B0` (colour immediates) + 12 register-select bytes in `sb` instructions `0x8011E1C2..EE` | **B** + **C** | `colors.gravboots: {c1, c2, pick[12]}`. |
| 52 | color_randomizer: wing smash | DRA `0x800DB248` (outline colour, data), or the CLUT byte at bin `0x13CAA0` (code) | **B** (+C) | `colors.wingsmash`. |
| 53 | color_randomizer: Hydro Storm, Richter, Maria, Dracula's cape | unknown file (bin `0x3A19544..`), TOP/BO6/BO2/CEN/DAI/NZ0/BO5 palettes, ST0 `0x8019A762` | **B** | `colors.*`. Mostly palette data. |
| 54 | skip_nz1 (`single_hit_gears`) | NZ1 `EntityWallGear` `0x801A8A1C-30`, RNZ1 `func_801A81C8` `0x801A8300-14` | **C** | On any gear hit, set the puzzle state to "solved" (0x0F) and open the door. |
| 55 | randomize_drop | DRA EnemyDefs drop ids (`0x800A8A0A..0x800AC6FC`), per-overlay global-drop tables (32 each), 2 immediates in NO3 `EntityBoneScimitar` | **B** (+C for 2) | `enemy_drops: {enemy_def_index: [rare, uncommon]}`, `global_drops: {overlay: [32 tile ids]}`. |
| 56 | randomize_candles | stage layout `state` = `(candle_type<<8) OR item` | **B** | `candles: {zone: {entity_offset: value}}` (io_items order). |

Candles.py, Traps.py and helper.py: **no writes** (unused files). Enemies.py: data for rows 29–33 and 55 only (`DropData`/`Global_drop` unused). ErrorRecalc.py: disc-image EDC/ECC only, not needed.

### 8.4 slot_data additions (suggested)
Roll these in `post_fill`, store them on `self`, and use the stored values in both `write_tokens` and `fill_slot_data`:
- `nonpool_items`, `start_gear`, `enemy_stats`, `enemy_drops` + `global_drops`, `candles`, `shop`, `start_room`, `music`, `colors`.
- Alternatively, one generic `data_writes: {overlay: [[offset, hexbytes], ...]}` built by recording every **data** token that `write_tokens` produces, with overlay-relative offsets, and leaving out the C sites. The test script in 8.5 already does this recording. Size is about 7–8k writes with every option on, so the JSON is roughly tens of KB.
- Optional but useful: `seed_name` is already in RoomInfo, and slot name and number come from Connected. The bits of the "sanity" byte are already options.

### 8.5 How the RAM addresses were produced
The script is in the session scratch folder and wasn't copied into the repo:
1. It stubs AP (`BaseClasses`, `worlds.Files`, `settings`, `Utils`) and runs the real `Rom.write_tokens` under Python 3.12 with every option on and off, in three placement modes (vanilla-own, own-swapped, all-remote).
2. It records each write and its calling function.
3. It converts bin offsets to RAM with the formulas in 3.2.
4. It checks each address against the recomp's `config/funcmaps/*.json` (from `config/sotn.json`) to tell code from data.

Appendix D is the grouped output.

A caution for the recomp: AP takes over castle-flag bytes CF[0xE3], CF[0xE5..0xFF] and CF[0x118..0x119]. The recomp's `wrapers/Progress.cs` defines `ItemsCollectedIndex = 0xF4` with count 143 (0xF4..0x182). Check that none of the recomp's cheats or tracker bulk-write that range, because it would overwrite the AP seed and item index. CF[0xE4] is `SecondClockRoomDoor`, which AP deliberately skips.

---

## Appendix A - Zone table (data/Zones.py)

`area_flag` = low 16 bits of `g_api.o.Update` (u16 read at MainRAM 0x03C774 = PSX 0x8003C774); also the first u16 of the loaded stage overlay at 0x80180000. `loot_flag` = the game's own item-pickup bitfield for that stage inside g_CastleFlags (0x8003BDEC); bit N = stage item-table index N collected. `items`/`rewards` = offset inside the stage file; at runtime the table is at 0x80180000 + offset. `pos` = byte offset of the stage file's first data byte inside Track 1 .bin (raw 2352-byte sectors).

| id | abbr | name | disc pos | items tbl RAM | rewards RAM | area_flag | loot_flag PSX addr | bytes |
|---|---|---|---|---|---|---|---|---|
| 0 | ST0 | Final Stage: Bloodlines | 0x533efc8 | 0x80180a60 | - | 0x189c | - | - |
| 1 | ARE | Colosseum | 0x43c2018 | 0x80180fe8 | - | 0x8704 | 0x8003bf06 | 2 |
| 2 | CAT | Catacombs | 0x448f938 | 0x8018174c | - | 0xb6d4 | 0x8003befc | 3 |
| 3 | CEN | Center Cube | 0x455bff8 | - | - | 0x0e7c | 0x8003beec | 2 |
| 4 | CHI | Abandoned Mine | 0x45e8ae8 | 0x801809e4 | - | 0xdea4 | 0x8003bf02 | 2 |
| 5 | DAI | Royal Chapel | 0x4675f08 | 0x80180ec0 | - | 0x5fb8 | 0x8003beff | 3 |
| 6 | DRE | Nightmare | 0x5af2478 | 0x80181928 | - | 0x6fc0 | 0x8003bef4 | 5 |
| 7 | LIB | Long Library | 0x47a1ae8 | 0x80181a90 | - | 0xf160 | 0x8003befa | 2 |
| 8 | NO0 | Marble Gallery | 0x48f9a38 | 0x80181100 | - | 0x37b8 | 0x8003beec | 2 |
| 9 | NO1 | Outer Wall | 0x49d18b8 | 0x80181a2c | - | 0x1a20 | 0x8003beee | 2 |
| 10 | NO2 | Olrox's Quarters | 0x4aa0438 | 0x80180fec | - | 0x8744 | 0x8003bef0 | 2 |
| 11 | NO3 | Castle Entrance | 0x4b665e8 | 0x80181c8c | - | 0x187c | 0x8003bef2 | 2 |
| 12 | NP3 | Castle Entrance (after visiting Alchemy Laboratory) | 0x53f4708 | 0x80181618 | - | 0x90ec | 0x8003bef2 | 2 |
| 13 | NO4 | Underground Caverns | 0x4c307e8 | 0x80181928 | - | 0xa620 | 0x8003bef4 | 5 |
| 14 | NZ0 | Alchemy Laboratory | 0x54b0c88 | 0x801813b0 | - | 0x9504 | 0x8003bf0b | 2 |
| 15 | NZ1 | Clock Tower | 0x55724b8 | 0x8018111c | - | 0xc710 | 0x8003bf0d | 2 |
| 16 | TOP | Castle Keep | 0x560e7b8 | 0x80180d10 | - | 0xd660 | 0x8003bf08 | 3 |
| 17 | WRP | Warp rooms | 0x5883408 | - | - | 0x8218 | - | - |
| 18 | RARE | Reverse Colosseum | 0x57509e8 | 0x80180a3c | - | 0x6b70 | 0x8003bf3b | 2 |
| 19 | RCAT | Floating Catacombs | 0x4cfa0b8 | 0x801813c8 | - | 0x3f80 | 0x8003bf2b | 4 |
| 20 | RCEN | Reverse Center Cube | 0x56bd9e8 | - | - | 0x049c | - | - |
| 21 | RCHI | Cave | 0x4da4968 | 0x801807cc | - | 0xac24 | 0x8003bf33 | 2 |
| 22 | RDAI | Anti-Chapel | 0x4e31458 | 0x80180d2c | - | 0x465c | 0x8003bf2f | 3 |
| 23 | RLIB | Forbidden Library | 0x4ee2218 | 0x80180bc8 | - | 0x2b90 | 0x8003bf27 | 2 |
| 24 | RNO0 | Black Marble Gallery | 0x4f84a28 | 0x80180f8c | - | 0x7354 | 0x8003bf13 | 2 |
| 25 | RNO1 | Reverse Outer Wall | 0x504f558 | 0x80180ae4 | - | 0x9ccc | 0x8003bf17 | 2 |
| 26 | RNO2 | Death Wing's Lair | 0x50f7948 | 0x80180d40 | - | 0x6d20 | 0x8003bf1b | 2 |
| 27 | RNO3 | Reverse Entrance | 0x51ac758 | 0x80180f10 | - | 0x3ee0 | 0x8003bf1f | 2 |
| 28 | RNO4 | Reverse Caverns | 0x526a868 | 0x80181620 | - | 0xa214 | 0x8003bf23 | 4 |
| 29 | RNZ0 | Necromancy Laboratory | 0x5902278 | 0x80180cc8 | - | 0xcc34 | 0x8003bf43 | 2 |
| 30 | RNZ1 | Reverse Clock Tower | 0x59bb0d8 | 0x80180ec8 | 0x80182570 | 0xced0 | 0x8003bf47 | 2 |
| 31 | RTOP | Reverse Castle Keep | 0x57df998 | 0x801807c8 | - | 0x2524 | 0x8003bf3f | 4 |
| 32 | RWRP | Reverse Warp rooms | 0x5a6e358 | - | - | 0xa198 | - | - |
| 33 | BO0 | Olrox | 0x5fa9dc8 | - | 0x801824d4 | 0xc10c | 0x8003bef0 | 2 |
| 34 | BO1 | Granfaloon | 0x606dab8 | - | 0x80181b98 | 0x55d0 | 0x8003befc | 3 |
| 35 | BO2 | Werewolf & Minotaur | 0x60fca68 | - | 0x8018181c | 0x76a0 | 0x8003bf06 | 2 |
| 36 | BO3 | Scylla | 0x61a60b8 | 0x8018108c | 0x80181c60 | 0x6734 | 0x8003bef4 | 5 |
| 37 | BO4 | Doppleganger10 | 0x6246d38 | - | 0x801842b0 | 0x69ec | 0x8003beee | 2 |
| 38 | BO5 | Hippogryph | 0x6304e48 | - | 0x801818b8 | 0x6be4 | 0x8003beff | 3 |
| 39 | BO6 | Richter | 0x63aa448 | - | 0x80182f90 | 0x9b84 | - | - |
| 40 | BO7 | Cerberus | 0x66b32f8 | - | 0x80181440 | 0x6678 | 0x8003bf02 | 2 |
| 41 | RBO0 | Trio | 0x64705f8 | - | 0x80181988 | 0xa094 | 0x8003bf3b | 2 |
| 42 | RBO1 | Beezlebub | 0x6590a18 | - | 0x80181550 | 0x5174 | 0x8003bf43 | 2 |
| 43 | RBO2 | Death | 0x6620c28 | - | 0x80181788 | 0x1ab0 | 0x8003bf33 | 2 |
| 44 | RBO3 | Medusa | 0x67422a8 | - | 0x801812a8 | 0x31c8 | 0x8003bf2f | 3 |
| 45 | RBO4 | Creature | 0x67cfff8 | - | 0x801813b4 | 0x8e3c | 0x8003bf17 | 2 |
| 46 | RBO5 | Doppleganger40 | 0x6861468 | - | 0x80184348 | 0x5920 | 0x8003bf23 | 4 |
| 47 | RBO6 | Shaft/Dracula | 0x692b668 | - | - | 0x54ec | - | - |
| 48 | RBO7 | Akmodan II | 0x69d1598 | - | 0x80181300 | 0x5f04 | 0x8003bf1b | 2 |
| 49 | RBO8 | Galamoth | 0x6a5f2e8 | - | 0x80182334 | 0x9dc8 | 0x8003bf2b | 4 |

## Appendix B - Item table (Items.py)

AP item id == game-side id (no base offset). `addr` is the MainRAM offset the client writes when granting (inventory count byte for 1-258 = 0x8009798A+id; relic byte for 300-329 = 0x80097964+(id-300); vessels = g_Status HP/hearts). Tile value written into stage item tables = id+0x80 for equipment/usables, id-300 for relics, id-400 for vessels (Heart Vessel=12, Life Vessel=23); HEART/GOLD/SUBWEAPON tile ids come from data/io_items.py (Heart=0, Big heart=1, $1=2, $25=3, $50=4, $100=5, $250=6, $400=7, $700=8, $1000=9, $2000=10, $5000=11, Dagger=14 ... Agunea=22).

| id | type | class | addr (PSX) | name |
|---|---|---|---|---|
| 1 | USABLE | filler | 0x8009798b | Monster vial 1 |
| 2 | USABLE | filler | 0x8009798c | Monster vial 2 |
| 3 | USABLE | filler | 0x8009798d | Monster vial 3 |
| 4 | WEAPON1 | useful | 0x8009798e | Shield rod |
| 5 | SHIELD | useful | 0x8009798f | Leather shield |
| 6 | SHIELD | useful | 0x80097990 | Knight shield |
| 7 | SHIELD | useful | 0x80097991 | Iron shield |
| 8 | SHIELD | useful | 0x80097992 | AxeLord shield |
| 9 | SHIELD | useful | 0x80097993 | Herald shield |
| 10 | SHIELD | useful | 0x80097994 | Dark shield |
| 11 | SHIELD | useful | 0x80097995 | Goddess shield |
| 12 | SHIELD | useful | 0x80097996 | Shaman shield |
| 13 | SHIELD | useful | 0x80097997 | Medusa shield |
| 14 | SHIELD | useful | 0x80097998 | Skull shield |
| 15 | SHIELD | useful | 0x80097999 | Fire shield |
| 16 | SHIELD | useful | 0x8009799a | Alucard shield |
| 17 | WEAPON2 | useful | 0x8009799b | Sword of dawn |
| 18 | WEAPON1 | useful | 0x8009799c | Basilard |
| 19 | WEAPON1 | useful | 0x8009799d | Short sword |
| 20 | WEAPON1 | useful | 0x8009799e | Combat knife |
| 21 | WEAPON2 | useful | 0x8009799f | Nunchaku |
| 22 | WEAPON1 | useful | 0x800979a0 | Were bane |
| 23 | WEAPON1 | useful | 0x800979a1 | Rapier |
| 24 | USABLE | filler | 0x800979a2 | Karma coin |
| 25 | USABLE | filler | 0x800979a3 | Magic missile |
| 26 | WEAPON2 | filler | 0x800979a4 | Red rust |
| 27 | WEAPON2 | useful | 0x800979a5 | Takemitsu |
| 28 | WEAPON1 | useful | 0x800979a6 | Shotel |
| 29 | USABLE | filler | 0x800979a7 | Orange |
| 30 | USABLE | filler | 0x800979a8 | Apple |
| 31 | USABLE | filler | 0x800979a9 | Banana |
| 32 | USABLE | filler | 0x800979aa | Grapes |
| 33 | USABLE | filler | 0x800979ab | Strawberry |
| 34 | USABLE | filler | 0x800979ac | Pineapple |
| 35 | USABLE | filler | 0x800979ad | Peanuts |
| 36 | USABLE | filler | 0x800979ae | Toadstool |
| 37 | USABLE | filler | 0x800979af | Shiitake |
| 38 | USABLE | filler | 0x800979b0 | Cheesecake |
| 39 | USABLE | filler | 0x800979b1 | Shortcake |
| 40 | USABLE | filler | 0x800979b2 | Tart |
| 41 | USABLE | filler | 0x800979b3 | Parfait |
| 42 | USABLE | filler | 0x800979b4 | Pudding |
| 43 | USABLE | filler | 0x800979b5 | Ice cream |
| 44 | USABLE | filler | 0x800979b6 | Frankfurter |
| 45 | USABLE | filler | 0x800979b7 | Hamburger |
| 46 | USABLE | filler | 0x800979b8 | Pizza |
| 47 | USABLE | filler | 0x800979b9 | Cheese |
| 48 | USABLE | filler | 0x800979ba | Ham and eggs |
| 49 | USABLE | filler | 0x800979bb | Omelette |
| 50 | USABLE | filler | 0x800979bc | Morning set |
| 51 | USABLE | filler | 0x800979bd | Lunch A |
| 52 | USABLE | filler | 0x800979be | Lunch B |
| 53 | USABLE | filler | 0x800979bf | Curry rice |
| 54 | USABLE | filler | 0x800979c0 | Gyros plate |
| 55 | USABLE | filler | 0x800979c1 | Spaghetti |
| 56 | USABLE | filler | 0x800979c2 | Grape juice |
| 57 | USABLE | filler | 0x800979c3 | Barley tea |
| 58 | USABLE | filler | 0x800979c4 | Green tea |
| 59 | USABLE | filler | 0x800979c5 | Natou |
| 60 | USABLE | filler | 0x800979c6 | Ramen |
| 61 | USABLE | filler | 0x800979c7 | Miso soup |
| 62 | USABLE | filler | 0x800979c8 | Sushi |
| 63 | USABLE | filler | 0x800979c9 | Pork bun |
| 64 | USABLE | filler | 0x800979ca | Red bean bun |
| 65 | USABLE | filler | 0x800979cb | Chinese bun |
| 66 | USABLE | filler | 0x800979cc | Dim sum set |
| 67 | USABLE | filler | 0x800979cd | Pot roast |
| 68 | USABLE | filler | 0x800979ce | Sirloin |
| 69 | USABLE | filler | 0x800979cf | Turkey |
| 70 | USABLE | filler | 0x800979d0 | Meal ticket |
| 71 | USABLE | filler | 0x800979d1 | Neutron bomb |
| 72 | USABLE | filler | 0x800979d2 | Power of sire |
| 73 | USABLE | filler | 0x800979d3 | Pentagram |
| 74 | USABLE | filler | 0x800979d4 | Bat pentagram |
| 75 | USABLE | filler | 0x800979d5 | Shuriken |
| 76 | USABLE | filler | 0x800979d6 | Cross shuriken |
| 77 | USABLE | filler | 0x800979d7 | Buffalo star |
| 78 | USABLE | filler | 0x800979d8 | Flame star |
| 79 | USABLE | filler | 0x800979d9 | TNT |
| 80 | USABLE | filler | 0x800979da | Bwaka knife |
| 81 | USABLE | filler | 0x800979db | Boomerang |
| 82 | USABLE | filler | 0x800979dc | Javelin |
| 83 | WEAPON1 | useful | 0x800979dd | Tyrfing |
| 84 | WEAPON2 | useful | 0x800979de | Namakura |
| 85 | WEAPON1 | useful | 0x800979df | Knuckle duster |
| 86 | WEAPON1 | useful | 0x800979e0 | Gladius |
| 87 | WEAPON1 | useful | 0x800979e1 | Scimitar |
| 88 | WEAPON1 | useful | 0x800979e2 | Cutlass |
| 89 | WEAPON1 | useful | 0x800979e3 | Saber |
| 90 | WEAPON1 | useful | 0x800979e4 | Falchion |
| 91 | WEAPON1 | useful | 0x800979e5 | Broadsword |
| 92 | WEAPON1 | useful | 0x800979e6 | Bekatowa |
| 93 | WEAPON1 | useful | 0x800979e7 | Damascus sword |
| 94 | WEAPON1 | useful | 0x800979e8 | Hunter sword |
| 95 | WEAPON2 | useful | 0x800979e9 | Estoc |
| 96 | WEAPON1 | useful | 0x800979ea | Bastard sword |
| 97 | WEAPON1 | useful | 0x800979eb | Jewel knuckles |
| 98 | WEAPON2 | useful | 0x800979ec | Claymore |
| 99 | WEAPON1 | useful | 0x800979ed | Talwar |
| 100 | WEAPON2 | useful | 0x800979ee | Katana |
| 101 | WEAPON2 | useful | 0x800979ef | Flamberge |
| 102 | WEAPON1 | useful | 0x800979f0 | Iron fist |
| 103 | WEAPON2 | useful | 0x800979f1 | Zwei hander |
| 104 | WEAPON1 | useful | 0x800979f2 | Sword of hador |
| 105 | WEAPON1 | useful | 0x800979f3 | Luminus |
| 106 | WEAPON1 | useful | 0x800979f4 | Harper |
| 107 | WEAPON2 | useful | 0x800979f5 | Obsidian sword |
| 108 | WEAPON1 | useful | 0x800979f6 | Gram |
| 109 | WEAPON1 | useful | 0x800979f7 | Jewel sword |
| 110 | WEAPON1 | useful | 0x800979f8 | Mormegil |
| 111 | WEAPON1 | useful | 0x800979f9 | Firebrand |
| 112 | WEAPON1 | useful | 0x800979fa | Thunderbrand |
| 113 | WEAPON1 | useful | 0x800979fb | Icebrand |
| 114 | WEAPON1 | useful | 0x800979fc | Stone sword |
| 115 | WEAPON1 | useful | 0x800979fd | Holy sword |
| 116 | WEAPON1 | useful | 0x800979fe | Terminus est |
| 117 | WEAPON1 | useful | 0x800979ff | Marsil |
| 118 | WEAPON1 | useful | 0x80097a00 | Dark blade |
| 119 | WEAPON1 | useful | 0x80097a01 | Heaven sword |
| 120 | WEAPON1 | useful | 0x80097a02 | Fist of tulkas |
| 121 | WEAPON1 | useful | 0x80097a03 | Gurthang |
| 122 | WEAPON1 | useful | 0x80097a04 | Mourneblade |
| 123 | WEAPON1 | useful | 0x80097a05 | Alucard sword |
| 124 | WEAPON1 | useful | 0x80097a06 | Mablung sword |
| 125 | WEAPON1 | useful | 0x80097a07 | Badelaire |
| 126 | WEAPON1 | useful | 0x80097a08 | Sword familiar |
| 127 | WEAPON2 | useful | 0x80097a09 | Great sword |
| 128 | WEAPON1 | useful | 0x80097a0a | Mace |
| 129 | WEAPON1 | useful | 0x80097a0b | Morningstar |
| 130 | WEAPON1 | useful | 0x80097a0c | Holy rod |
| 131 | WEAPON1 | useful | 0x80097a0d | Star flail |
| 132 | WEAPON1 | useful | 0x80097a0e | Moon rod |
| 133 | WEAPON1 | useful | 0x80097a0f | Chakram |
| 134 | USABLE | filler | 0x80097a10 | Fire boomerang |
| 135 | USABLE | filler | 0x80097a11 | Iron ball |
| 136 | WEAPON1 | useful | 0x80097a12 | Holbein dagger |
| 137 | WEAPON1 | useful | 0x80097a13 | Blue knuckles |
| 138 | USABLE | filler | 0x80097a14 | Dynamite |
| 139 | WEAPON2 | useful | 0x80097a15 | Osafune katana |
| 140 | WEAPON2 | useful | 0x80097a16 | Masamune |
| 141 | WEAPON2 | useful | 0x80097a17 | Muramasa |
| 142 | USABLE | filler | 0x80097a18 | Heart refresh |
| 143 | WEAPON1 | useful | 0x80097a19 | Runesword |
| 144 | USABLE | filler | 0x80097a1a | Antivenom |
| 145 | USABLE | filler | 0x80097a1b | Uncurse |
| 146 | USABLE | filler | 0x80097a1c | Life apple |
| 147 | USABLE | filler | 0x80097a1d | Hammer |
| 148 | USABLE | filler | 0x80097a1e | Str. potion |
| 149 | USABLE | filler | 0x80097a1f | Luck potion |
| 150 | USABLE | filler | 0x80097a20 | Smart potion |
| 151 | USABLE | filler | 0x80097a21 | Attack potion |
| 152 | USABLE | filler | 0x80097a22 | Shield potion |
| 153 | USABLE | filler | 0x80097a23 | Resist fire |
| 154 | USABLE | filler | 0x80097a24 | Resist thunder |
| 155 | USABLE | filler | 0x80097a25 | Resist ice |
| 156 | USABLE | filler | 0x80097a26 | Resist stone |
| 157 | USABLE | filler | 0x80097a27 | Resist holy |
| 158 | USABLE | filler | 0x80097a28 | Resist dark |
| 159 | USABLE | filler | 0x80097a29 | Potion |
| 160 | USABLE | filler | 0x80097a2a | High potion |
| 161 | USABLE | filler | 0x80097a2b | Elixir |
| 162 | USABLE | filler | 0x80097a2c | Manna prism |
| 163 | WEAPON1 | useful | 0x80097a2d | Vorpal blade |
| 164 | WEAPON1 | useful | 0x80097a2e | Crissaegrim |
| 165 | WEAPON2 | useful | 0x80097a2f | Yasutsuna |
| 166 | USABLE | filler | 0x80097a30 | Library card |
| 167 | SHIELD | useful | 0x80097a31 | Alucart shield |
| 168 | WEAPON1 | useful | 0x80097a32 | Alucart sword |
| 170 | ARMOR | useful | 0x80097a34 | Cloth tunic |
| 171 | ARMOR | useful | 0x80097a35 | Hide cuirass |
| 172 | ARMOR | useful | 0x80097a36 | Bronze cuirass |
| 173 | ARMOR | useful | 0x80097a37 | Iron cuirass |
| 174 | ARMOR | useful | 0x80097a38 | Steel cuirass |
| 175 | ARMOR | useful | 0x80097a39 | Silver plate |
| 176 | ARMOR | useful | 0x80097a3a | Gold plate |
| 177 | ARMOR | useful | 0x80097a3b | Platinum mail |
| 178 | ARMOR | useful | 0x80097a3c | Diamond plate |
| 179 | ARMOR | useful | 0x80097a3d | Fire mail |
| 180 | ARMOR | useful | 0x80097a3e | Lightning mail |
| 181 | ARMOR | useful | 0x80097a3f | Ice mail |
| 182 | ARMOR | useful | 0x80097a40 | Mirror cuirass |
| 183 | ARMOR | progression | 0x80097a41 | Spike breaker |
| 184 | ARMOR | useful | 0x80097a42 | Alucard mail |
| 185 | ARMOR | useful | 0x80097a43 | Dark armor |
| 186 | ARMOR | useful | 0x80097a44 | Healing mail |
| 187 | ARMOR | useful | 0x80097a45 | Holy mail |
| 188 | ARMOR | useful | 0x80097a46 | Walk armor |
| 189 | ARMOR | useful | 0x80097a47 | Brilliant mail |
| 190 | ARMOR | useful | 0x80097a48 | Mojo mail |
| 191 | ARMOR | useful | 0x80097a49 | Fury plate |
| 192 | ARMOR | useful | 0x80097a4a | Dracula tunic |
| 193 | ARMOR | useful | 0x80097a4b | God's Garb |
| 194 | ARMOR | filler | 0x80097a4c | Axe Lord armor |
| 196 | HELMET | useful | 0x80097a4e | Sunglasses |
| 197 | HELMET | useful | 0x80097a4f | Ballroom mask |
| 198 | HELMET | useful | 0x80097a50 | Bandanna |
| 199 | HELMET | useful | 0x80097a51 | Felt hat |
| 200 | HELMET | useful | 0x80097a52 | Velvet hat |
| 201 | HELMET | useful | 0x80097a53 | Goggles |
| 202 | HELMET | useful | 0x80097a54 | Leather hat |
| 203 | HELMET | progression | 0x80097a55 | Holy glasses |
| 204 | HELMET | useful | 0x80097a56 | Steel helm |
| 205 | HELMET | useful | 0x80097a57 | Stone mask |
| 206 | HELMET | useful | 0x80097a58 | Circlet |
| 207 | HELMET | useful | 0x80097a59 | Gold circlet |
| 208 | HELMET | useful | 0x80097a5a | Ruby circlet |
| 209 | HELMET | useful | 0x80097a5b | Opal circlet |
| 210 | HELMET | useful | 0x80097a5c | Topaz circlet |
| 211 | HELMET | useful | 0x80097a5d | Beryl circlet |
| 212 | HELMET | useful | 0x80097a5e | Cat-eye circl. |
| 213 | HELMET | useful | 0x80097a5f | Coral circlet |
| 214 | HELMET | useful | 0x80097a60 | Dragon helm |
| 215 | HELMET | useful | 0x80097a61 | Silver crown |
| 216 | HELMET | useful | 0x80097a62 | Wizard hat |
| 218 | CLOAK | useful | 0x80097a64 | Cloth cape |
| 219 | CLOAK | useful | 0x80097a65 | Reverse cloak |
| 220 | CLOAK | useful | 0x80097a66 | Elven cloak |
| 221 | CLOAK | useful | 0x80097a67 | Crystal cloak |
| 222 | CLOAK | useful | 0x80097a68 | Royal cloak |
| 223 | CLOAK | useful | 0x80097a69 | Blood cloak |
| 224 | CLOAK | useful | 0x80097a6a | Joseph's cloak |
| 225 | CLOAK | useful | 0x80097a6b | Twilight cloak |
| 227 | ACCESSORY | useful | 0x80097a6d | Moonstone |
| 228 | ACCESSORY | useful | 0x80097a6e | Sunstone |
| 229 | ACCESSORY | useful | 0x80097a6f | Bloodstone |
| 230 | ACCESSORY | useful | 0x80097a70 | Staurolite |
| 231 | ACCESSORY | useful | 0x80097a71 | Ring of pales |
| 232 | ACCESSORY | filler | 0x80097a72 | Zircon |
| 233 | ACCESSORY | filler | 0x80097a73 | Aquamarine |
| 234 | ACCESSORY | filler | 0x80097a74 | Turquoise |
| 235 | ACCESSORY | filler | 0x80097a75 | Onyx |
| 236 | ACCESSORY | filler | 0x80097a76 | Garnet |
| 237 | ACCESSORY | filler | 0x80097a77 | Opal |
| 238 | ACCESSORY | filler | 0x80097a78 | Diamond |
| 239 | ACCESSORY | useful | 0x80097a79 | Lapis lazuli |
| 240 | ACCESSORY | useful | 0x80097a7a | Ring of ares |
| 241 | ACCESSORY | progression | 0x80097a7b | Gold ring |
| 242 | ACCESSORY | progression | 0x80097a7c | Silver ring |
| 243 | ACCESSORY | useful | 0x80097a7d | Ring of varda |
| 244 | ACCESSORY | useful | 0x80097a7e | Ring of arcana |
| 245 | ACCESSORY | useful | 0x80097a7f | Mystic pendant |
| 246 | ACCESSORY | useful | 0x80097a80 | Heart broach |
| 247 | ACCESSORY | useful | 0x80097a81 | Necklace of j |
| 248 | ACCESSORY | useful | 0x80097a82 | Gauntlet |
| 249 | ACCESSORY | useful | 0x80097a83 | Ankh of life |
| 250 | ACCESSORY | useful | 0x80097a84 | Ring of feanor |
| 251 | ACCESSORY | useful | 0x80097a85 | Medal |
| 252 | ACCESSORY | useful | 0x80097a86 | Talisman |
| 253 | ACCESSORY | useful | 0x80097a87 | Duplicator |
| 254 | ACCESSORY | useful | 0x80097a88 | King's stone |
| 255 | ACCESSORY | useful | 0x80097a89 | Covenant stone |
| 256 | ACCESSORY | useful | 0x80097a8a | Nauglamir |
| 257 | ACCESSORY | filler | 0x80097a8b | Secret boots |
| 258 | ARMOR | useful | 0x80097a8c | Alucart mail |
| 300 | RELIC | progression | 0x80097964 | Soul of bat |
| 301 | RELIC | useful | 0x80097965 | Fire of bat |
| 302 | RELIC | progression | 0x80097966 | Echo of bat |
| 303 | RELIC | useful | 0x80097967 | Force of echo |
| 304 | RELIC | progression | 0x80097968 | Soul of wolf |
| 305 | RELIC | useful | 0x80097969 | Power of wolf |
| 306 | RELIC | useful | 0x8009796a | Skill of wolf |
| 307 | RELIC | progression | 0x8009796b | Form of mist |
| 308 | RELIC | progression | 0x8009796c | Power of mist |
| 309 | RELIC | useful | 0x8009796d | Gas cloud |
| 310 | RELIC | progression | 0x8009796e | Cube of zoe |
| 311 | RELIC | useful | 0x8009796f | Spirit orb |
| 312 | RELIC | progression | 0x80097970 | Gravity boots |
| 313 | RELIC | progression | 0x80097971 | Leap stone |
| 314 | RELIC | progression | 0x80097972 | Holy symbol |
| 315 | RELIC | useful | 0x80097973 | Faerie scroll |
| 316 | RELIC | progression | 0x80097974 | Jewel of open |
| 317 | RELIC | progression | 0x80097975 | Merman statue |
| 318 | RELIC | useful | 0x80097976 | Bat card |
| 319 | RELIC | useful | 0x80097977 | Ghost card |
| 320 | RELIC | useful | 0x80097978 | Faerie card |
| 321 | RELIC | progression | 0x80097979 | Demon card |
| 322 | RELIC | useful | 0x8009797a | Sword card |
| 325 | RELIC | progression | 0x8009797d | Heart of vlad |
| 326 | RELIC | progression | 0x8009797e | Tooth of vlad |
| 327 | RELIC | progression | 0x8009797f | Rib of vlad |
| 328 | RELIC | progression | 0x80097980 | Ring of vlad |
| 329 | RELIC | progression | 0x80097981 | Eye of vlad |
| 400 | EVENT | progression | - | Victory |
| 401 | EVENT | progression | - | Boss token |
| 402 | EVENT | progression | - | Exploration token |
| 412 | POWERUP | useful | 0x80097ba8 | Heart Vessel |
| 423 | POWERUP | useful | 0x80097ba0 | Life Vessel |

## Appendix C - Location table (Locations.py) - AP location id -> in-game detection

Detection column is what client.py actually tests (see section 3). `idx` = index into that stage's item table (tile id at 0x80180000+items+2*idx) and bit index into the stage `loot_flag`. Castle-flag addresses are PSX addresses (MainRAM offset + 0x80000000).

| ap_id | name | zone(s) | vanilla | idx | detection / special |
|---|---|---|---|---|---|
| 1 | Colosseum Second Part - Bottom Right Room | ARE | Heart Vessel | 0 | loot_flag bit idx |
| 2 | Colosseum First Part - Bottom Left Room | ARE | Shield rod | 1 | loot_flag bit idx |
| 3 | Colosseum Second Part - Bottom Left Room | ARE | Blood cloak | 3 | loot_flag bit idx |
| 4 | Colosseum First Part - Next to Royal Chapel Passage | ARE | Knight shield | 4 | loot_flag bit idx |
| 5 | Colosseum First Part - Before Minotaurus & Werewolf | ARE | Library card | 5 | loot_flag bit idx |
| 6 | Colosseum First Part - Bottom Right Room | ARE | Green tea | 6 | loot_flag bit idx |
| 7 | Colosseum Junction Tunnel - Attic | ARE | Holy sword | 7 | loot_flag bit idx |
| 8 | Colosseum Second Part - Behind Mist Crate | ARE | Form of mist | 2 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 9 | Catacombs Upper - After Save Point Breakable Wall | CAT | Cat-eye circl. | 0 | loot_flag bit idx |
| 10 | Catacombs Bottom - Above Discus Lord Breakable Wall Room | CAT | Icebrand | 1 | loot_flag bit idx |
| 11 | Catacombs Bottom - After Save Point | CAT | Walk armor | 2 | loot_flag bit idx |
| 12 | Catacombs Bottom - After Granfaloon | CAT | Mormegil | 3 | loot_flag bit idx |
| 13 | Catacombs After Dark Spiked Area - Bottom Left Breakable | CAT | Library card | 4 | loot_flag bit idx |
| 14 | Catacombs Bottom - Above Discus Lord Red Vase 2 | CAT | Heart Vessel | 6 | loot_flag bit idx |
| 15 | Catacombs Bottom - Above Discus Lord Red Vase 1 | CAT | Ballroom mask | 7 | loot_flag bit idx |
| 16 | Catacombs Upper - After Save Point | CAT | Bloodstone | 8 | loot_flag bit idx |
| 17 | Catacombs Bottom - After Crypt Left Item | CAT | Life Vessel | 9 | loot_flag bit idx |
| 18 | Catacombs Bottom - After Crypt Right Item | CAT | Heart Vessel | 10 | loot_flag bit idx |
| 19 | Catacombs After Dark Spiked Area - Red Vase 2 | CAT | Cross shuriken | 11 | loot_flag bit idx |
| 20 | Catacombs After Dark Spiked Area - Red Vase 1 | CAT | Cross shuriken | 12 | loot_flag bit idx |
| 21 | Catacombs After Dark Spiked Area - Red Vase 4 | CAT | Karma coin | 13 | loot_flag bit idx |
| 22 | Catacombs After Dark Spiked Area - Red Vase 3 | CAT | Karma coin | 14 | loot_flag bit idx |
| 23 | Catacombs After Dark Spiked Area - Bottom Right Item | CAT | Pork bun | 15 | loot_flag bit idx |
| 24 | Catacombs After Dark Spiked Area - Bottom Left Item | CAT | Spike breaker | 16 | loot_flag bit idx |
| 25 | Catacombs Bottom - Sarcophagus 1 | CAT | Monster vial 3 | 17 | loot_flag bit idx |
| 26 | Catacombs Bottom - Sarcophagus 2 | CAT | Monster vial 3 | 18 | loot_flag bit idx |
| 27 | Catacombs Bottom - Sarcophagus 3 | CAT | Monster vial 3 | 19 | loot_flag bit idx |
| 28 | Catacombs Bottom - Sarcophagus 4 | CAT | Monster vial 3 | 20 | loot_flag bit idx |
| 29 | Abandoned Mine Demon Side - Behind Breakable Wall Item 1 | CHI | Power of sire | 0 | loot_flag bit idx |
| 30 | Abandoned Mine Bottom - Right Item | CHI | Karma coin | 1 | loot_flag bit idx |
| 31 | Abandoned Mine Demon Side - Item on the Floor | CHI | Ring of ares | 4 | loot_flag bit idx |
| 32 | Abandoned Mine Bottom - Left Item | CHI | Combat knife | 5 | loot_flag bit idx |
| 33 | Abandoned Mine - Bottom Descend Item 2 | CHI | Shiitake | 6 | loot_flag bit idx |
| 34 | Abandoned Mine - Bottom Descend Item 1 | CHI | Shiitake | 7 | loot_flag bit idx |
| 35 | Abandoned Mine Demon Side - Behind Breakable Wall Item 2 | CHI | Barley tea | 8 | loot_flag bit idx |
| 36 | Abandoned Mine Demon Side - Behind Breakable Wall Item 3 | CHI | Peanuts | 9 | loot_flag bit idx |
| 37 | Abandoned Mine Demon Side - Behind Breakable Wall Item 4 | CHI | Peanuts | 10 | loot_flag bit idx |
| 38 | Abandoned Mine Demon Side - Behind Breakable Wall Item 5 | CHI | Peanuts | 11 | loot_flag bit idx |
| 39 | Abandoned Mine Demon Side - Behind Breakable Wall Item 6 | CHI | Peanuts | 12 | loot_flag bit idx |
| 40 | Abandoned Mine Demon Side - Item on Breakable Wall | CHI | Turkey |  | break flag 0x8003be3d & 0x1; item id is written at bin 0x45e9602 |
| 41 | Abandoned Mine - Middle Descend Left Room | CHI | Demon card | 2 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 42 | Royal Chapel Stairs - Red Vase on Alcove 4 | DAI | Ankh of life | 0 | loot_flag bit idx |
| 43 | Royal Chapel Stairs - Upper Alcove | DAI | Morningstar | 1 | loot_flag bit idx |
| 44 | Royal Chapel - Item Behind Maria | DAI | Silver ring | 2 | loot_flag bit idx |
| 45 | Royal Chapel Stairs - Bottom Red Vase | DAI | Aquamarine | 3 | loot_flag bit idx |
| 46 | Royal Chapel Stairs - Red Vase on Alcove 1 | DAI | Mystic pendant | 4 | loot_flag bit idx |
| 47 | Royal Chapel Stairs - Red Vase on Alcove 2 | DAI | Magic missile | 5 | loot_flag bit idx |
| 48 | Royal Chapel Stairs - Red Vase on Alcove 3 | DAI | Shuriken | 6 | loot_flag bit idx |
| 49 | Royal Chapel Stairs - Red Vase on Alcove 5 | DAI | TNT | 7 | loot_flag bit idx |
| 50 | Royal Chapel Stairs - Red Vase on Alcove 6 | DAI | Boomerang | 8 | loot_flag bit idx |
| 51 | Royal Chapel - Inner Chapel Doorway Roof | DAI | Goggles | 9 | loot_flag bit idx |
| 52 | Royal Chapel Tower 1 - Top Item | DAI | Silver plate | 10 | loot_flag bit idx |
| 53 | Royal Chapel Tower 1 - Yellow Vase | DAI | Str. potion | 11 | loot_flag bit idx |
| 54 | Royal Chapel Tower 1 - Red Vase | DAI | Life Vessel | 12 | loot_flag bit idx |
| 55 | Royal Chapel Tower 2 - Top Item | DAI | Zircon | 13 | loot_flag bit idx |
| 56 | Royal Chapel Tower 3 - Top Item | DAI | Cutlass | 14 | loot_flag bit idx |
| 57 | Royal Chapel Tower 3 - Red Vase | DAI | Potion | 15 | loot_flag bit idx |
| 58 | Long Library - Deeper Library Upper Part Flame on Table | LIB | Stone mask | 1 | loot_flag bit idx |
| 59 | Long Library - Deeper Library Behind Bookshelf Item 2 | LIB | Holy rod | 2 | loot_flag bit idx |
| 60 | Long Library - Item Bellow Librarian | LIB | Bronze cuirass | 4 | loot_flag bit idx |
| 61 | Long Library - Deeper Library Lower Part Statue 1 | LIB | Takemitsu | 5 | loot_flag bit idx |
| 62 | Long Library - Deeper Library Lower Part Statue 2 | LIB | Onyx | 6 | loot_flag bit idx |
| 63 | Long Library - Deeper Library Lower Part Red Vase | LIB | Frankfurter | 7 | loot_flag bit idx |
| 64 | Long Library - Top Left Room Item 2 | LIB | Potion | 8 | loot_flag bit idx |
| 65 | Long Library - Top Left Room Item 3 | LIB | Antivenom | 9 | loot_flag bit idx |
| 66 | Long Library - Deeper Library Behind Bookshelf Item 1 | LIB | Topaz circlet | 10 | loot_flag bit idx |
| 67 | Long Library - Deeper Library Behind Mist Crate | LIB | Soul of bat | 0 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 68 | Long Library - Top Right Floor | LIB | Faerie scroll | 3 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 69 | Long Library - Top Left Room Item 1 | LIB | Faerie card | 11 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 70 | Long Library - Librarian Shop Item | LIB | Jewel of open |  | librarian shop slot; relic: relic-owned byte; item: inventory count rises while in room 0x9470 |
| 71 | Marble Gallery - Left Clock Before Olrox's Quarter | NO0 | Life Vessel | 0 | loot_flag bit idx |
| 72 | Marble Gallery - Right Clock Item 1 | NO0 | Alucart shield | 1 | loot_flag bit idx |
| 73 | Marble Gallery - Right Clock Item 2 | NO0 | Heart Vessel | 2 | loot_flag bit idx |
| 74 | Marble Gallery - Middle clock Left Item 1 | NO0 | Life apple | 3 | loot_flag bit idx |
| 75 | Marble Gallery - Middle clock Left Item 2 | NO0 | Hammer | 4 | loot_flag bit idx |
| 76 | Marble Gallery - Middle clock Left Item 3 | NO0 | Potion | 5 | loot_flag bit idx |
| 77 | Marble Gallery - Right Clock Item 3 | NO0 | Alucart mail | 6 | loot_flag bit idx |
| 78 | Marble Gallery - Right Clock Item 4 | NO0 | Alucart sword | 7 | loot_flag bit idx |
| 79 | Marble Gallery - Inside Clock Left Item | NO0 | Life Vessel | 8 | loot_flag bit idx |
| 80 | Marble Gallery - Inside Clock Right Item | NO0 | Heart Vessel | 9 | loot_flag bit idx |
| 81 | Marble Gallery - Bellow Red Trap Door Right Item | NO0 | Library card | 10 | loot_flag bit idx |
| 82 | Marble Gallery - Bellow Red Trap Door Left Item | NO0 | Attack potion | 11 | loot_flag bit idx |
| 83 | Marble Gallery - Descend to Entrance Item 2 | NO0 | Hammer | 12 | loot_flag bit idx |
| 84 | Marble Gallery - Descend to Entrance Item 1 | NO0 | Str. potion | 13 | loot_flag bit idx |
| 85 | Marble Gallery - Item Given by Maria | CEN | Holy glasses |  | castle flag 0x8003bec4 & 0x1; item id immediate at bin 0x456e368 |
| 86 | Marble Gallery - Descend to Entrance Item 3 | NO0 | Spirit orb | 14 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 87 | Marble Gallery - Middle clock Right Item | NO0 | Gravity boots | 15 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 88 | Outer Wall - Item 1 Behind Mist Grate | NO1 | Jewel knuckles | 0 | loot_flag bit idx |
| 89 | Outer Wall - Item 2 Behind Mist Grate | NO1 | Mirror cuirass | 1 | loot_flag bit idx |
| 90 | Outer Wall - Red Vase Near Elevator Switch | NO1 | Heart Vessel | 2 | loot_flag bit idx |
| 91 | Outer Wall - Yellow Vase on High Ledge | NO1 | Garnet | 3 | loot_flag bit idx |
| 92 | Outer Wall - Item After Doppleganger 10 | NO1 | Gladius | 4 | loot_flag bit idx |
| 93 | Outer Wall - Red Vase After Doppleganger 10 | NO1 | Life Vessel | 5 | loot_flag bit idx |
| 94 | Outer Wall - Red Vase Near Marble Gallery Door | NO1 | Zircon | 6 | loot_flag bit idx |
| 95 | Outer Wall - Breakable Wall in Room Behind Armor Lord | NO1 | Pot roast |  | break flag 0x8003bdfe & 0x1; item id is written at bin 0x4a197d8 (no_offset: raw id) |
| 96 | Outer Wall - Inside of Elevator | NO1 | Soul of wolf | 7 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 97 | Olrox's Quarters Path to Royal Chapel - On Wooden Display | NO2 | Heart Vessel | 1 | loot_flag bit idx |
| 98 | Olrox's Quarters Lower Part - Room Behind Breakable Wall Vase 3 | NO2 | Broadsword | 4 | loot_flag bit idx |
| 99 | Olrox's Quarters Lower Part - Room Behind Breakable Wall Vase 2 | NO2 | Onyx | 5 | loot_flag bit idx |
| 100 | Olrox's Quarters Lower Part - Room Behind Breakable Wall Vase 1 | NO2 | Cheese | 6 | loot_flag bit idx |
| 101 | Olrox's Quarters Upper Part - Ascend Shaft Red Vase 1 | NO2 | Manna prism | 7 | loot_flag bit idx |
| 102 | Olrox's Quarters Upper Part - Ascend Shaft Red Vase 2 | NO2 | Resist fire | 8 | loot_flag bit idx |
| 103 | Olrox's Quarters Upper Part - Ascend Shaft Red Vase 3 | NO2 | Luck potion | 9 | loot_flag bit idx |
| 104 | Olrox's Quarters Upper Part - Ledge Before Drop to Courtyard | NO2 | Estoc | 10 | loot_flag bit idx |
| 105 | Olrox's Quarters - Hole Before Olrox | NO2 | Iron ball | 11 | loot_flag bit idx |
| 106 | Olrox's Quarters Courtyard - Right Room | NO2 | Garnet | 12 | loot_flag bit idx |
| 107 | Olrox's Quarters - After Olrox | NO2 | Echo of bat | 0 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 108 | Olrox's Quarters Path to Royal Chapel - Hidden Attic | NO2 | Sword card | 2 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 109 | Entrance - Above First Encounter With Death | NO3,NP3 | Heart Vessel | 0 | loot_flag bit idx |
| 110 | Entrance - Right Alcove in Cube of Zoe Room | NO3,NP3 | Life Vessel | 1 | loot_flag bit idx |
| 111 | Entrance - Wolf/Bat Secret Room Right Item | NO3,NP3 | Life apple | 2 | loot_flag bit idx |
| 112 | Entrance - Behind Stone Wall in Cube of Zoe Room | NO3,NP3 | Shield potion | 4 | loot_flag bit idx |
| 113 | Entrance - Attic Above Mermans | NO3,NP3 | Holy mail | 5 | loot_flag bit idx |
| 114 | Entrance - By Underground Caverns Bottom Exit | NO3,NP3 | Life Vessel | 6 | loot_flag bit idx |
| 115 | Entrance - Castle Entrance Teleport Exit | NO3,NP3 | Heart Vessel | 7 | loot_flag bit idx |
| 116 | Entrance - Attic Near Start Gate Right item | NO3,NP3 | Life Vessel | 8 | loot_flag bit idx |
| 117 | Entrance - Wolf/Bat Secret Room Left Item | NP3 | Jewel sword | 9 | loot_flag bit idx |
| 118 | Entrance - Breakable Wall Above Merman | NO3,NP3 | Pot roast |  | break flag 0x8003be1f & 0x1; item id is written at bin 0x4ba9774, 0x5431554 (no_offset: raw id) |
| 119 | Entrance - Breakable Ledge Before Death | NO3,NP3 | Turkey |  | break flag 0x8003be24 & 0x1; item id is written at bin 0x4baa2b0, 0x5431f60 (no_offset: raw id) |
| 120 | Entrance - Pedestal in Cube of Zoe Room | NO3,NP3 | Cube of zoe | 3 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 121 | Entrance - Attic Near Start Gate Left item | NO3,NP3 | Power of wolf | 10 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 122 | Underground Caverns - Wooden Stand Close to Stairway | NO4,NO4 | Heart Vessel | 0 | loot_flag bit idx |
| 123 | Underground Caverns - Middle of Stairway Room | NO4 | Life Vessel | 1 | loot_flag bit idx |
| 124 | Underground Caverns Scylla Area - After Fight Item | NO4,BO3 | Crystal cloak | 2 | loot_flag bit idx |
| 125 | Underground Caverns - Top Underwater Item | NO4 | Antivenom | 4 | loot_flag bit idx |
| 126 | Underground Caverns - Bottom Underwater Item | NO4 | Life Vessel | 5 | loot_flag bit idx |
| 127 | Underground Caverns - Hidden Room Behind Waterfall | NO4 | Life Vessel | 6 | loot_flag bit idx |
| 128 | Underground Caverns - Top Left Room From Waterfall | NO4 | Herald shield | 7 | loot_flag bit idx |
| 129 | Underground Caverns - Red Vase on Ledge Next to Marble Gallery | NO4 | Zircon | 9 | loot_flag bit idx |
| 130 | Underground Caverns Succubus Side - Succubus item | NO4 | Gold ring | 10 | loot_flag bit idx; extra addr 0x4c324b4 |
| 131 | Underground Caverns - Room Behind Breakable Wall Close to Stairway | NO4 | Bandanna | 11 | loot_flag bit idx |
| 132 | Underground Caverns - Bottom of Stairway | NO4 | Shiitake | 12 | loot_flag bit idx |
| 133 | Underground Caverns Succubus Side - Red Vase 1 | NO4 | Claymore | 13 | loot_flag bit idx |
| 134 | Underground Caverns Succubus Side - Red Vase 2 | NO4 | Meal ticket | 14 | loot_flag bit idx |
| 135 | Underground Caverns Succubus Side - Red Vase 3 | NO4 | Meal ticket | 15 | loot_flag bit idx |
| 136 | Underground Caverns Succubus Side - Red Vase 4 | NO4 | Meal ticket | 16 | loot_flag bit idx |
| 137 | Underground Caverns Succubus Side - Red Vase 5 | NO4 | Meal ticket | 17 | loot_flag bit idx |
| 138 | Underground Caverns Succubus Side - Red Vase 6 | NO4 | Moonstone | 18 | loot_flag bit idx |
| 139 | Underground Caverns Scylla Area - Right item | NO4,BO3 | Scimitar | 19 | loot_flag bit idx |
| 140 | Underground Caverns Scylla Area - Left item | NO4,BO3 | Resist ice | 20 | loot_flag bit idx |
| 141 | Underground Caverns Scylla Area - Red Vase | NO4,BO3 | Pot roast | 21 | loot_flag bit idx |
| 142 | Underground Caverns Ice Area - On Alcove | NO4 | Onyx | 22 | loot_flag bit idx |
| 143 | Underground Caverns Ice Area - Underwater Item 1 | NO4 | Knuckle duster | 23 | loot_flag bit idx |
| 144 | Underground Caverns Ice Area - Underwater Item 2 | NO4 | Life Vessel | 24 | loot_flag bit idx |
| 145 | Underground Caverns Ice Area - Underwater Item 3 | NO4 | Elixir | 25 | loot_flag bit idx |
| 146 | Underground Caverns - Bellow Stairway | NO4 | Toadstool | 26 | loot_flag bit idx |
| 147 | Underground Caverns - Alcove Next to Drowned Guards | NO4 | Shiitake | 27 | loot_flag bit idx |
| 148 | Underground Caverns - Bellow Wooden Bridge Left Item | NO4 | Life Vessel | 28 | loot_flag bit idx |
| 149 | Underground Caverns - Bellow Wooden Bridge Right Item | NO4 | Heart Vessel | 29 | loot_flag bit idx |
| 150 | Underground Caverns - Underwater Stream | NO4 | Pentagram | 30 | loot_flag bit idx |
| 151 | Underground Caverns - Alcove Behind Waterfall | NO4 | Secret boots | 31 | loot_flag bit idx |
| 152 | Underground Caverns - Waterfall Upper Item | NO4 | Shiitake | 32 | loot_flag bit idx |
| 153 | Underground Caverns - Waterfall Bottom Item | NO4 | Toadstool | 33 | loot_flag bit idx |
| 154 | Underground Caverns - Next to Castle Entrance Passage | NO4 | Shiitake | 35 | loot_flag bit idx |
| 155 | Underground Caverns - Air Pocket Item | NO4 | Nunchaku | 36 | loot_flag bit idx |
| 156 | Underground Caverns Ice Area - After Ferryman | NO4 | Holy symbol | 3 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 157 | Underground Caverns - After Ferryman | NO4 | Merman statue | 8 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 158 | Alchemy Lab. - Globe by the Bottom Entrance | NZ0 | Hide cuirass | 0 | loot_flag bit idx |
| 159 | Alchemy Lab. - Globe in Hidden Room Behind Breakable Wall | NZ0 | Heart Vessel | 1 | loot_flag bit idx |
| 160 | Alchemy Lab. - Globe After Spike Puzzle | NZ0 | Cloth cape | 2 | loot_flag bit idx |
| 161 | Alchemy Lab. - Tank in Hidden Basement on Breakable Floor | NZ0 | Life Vessel | 3 | loot_flag bit idx |
| 162 | Alchemy Lab. - Globe on Middle Elevator Shaft Room | NZ0 | Sunglasses | 6 | loot_flag bit idx |
| 163 | Alchemy Lab. - Flame on Table Middle Way Up | NZ0 | Resist thunder | 7 | loot_flag bit idx |
| 164 | Alchemy Lab. - Flame Near Spike Switch | NZ0 | Leather shield | 8 | loot_flag bit idx |
| 165 | Alchemy Lab. - Item by Cannon | NZ0 | Basilard | 9 | loot_flag bit idx |
| 166 | Alchemy Lab. - Globe in Big Room With Axe Lord and Spittle Bone | NZ0 | Potion | 10 | loot_flag bit idx |
| 167 | Alchemy Lab. - Globe in Attic With Powerup Tanks | NZ0 | Skill of wolf | 4 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx; extra addr 0x54b1d5a |
| 168 | Alchemy Lab. - Globe in Upper-left Room of Slogra and Gaibon | NZ0,NZ0 | Bat card | 5 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx; extra addr 0x54b1d58 |
| 169 | Clock Tower - Bellow Broken Bridge Item 2 | NZ1 | Magic missile | 0 | loot_flag bit idx |
| 170 | Clock Tower - Bellow Broken Bridge Item 1 | NZ1 | Pentagram | 1 | loot_flag bit idx |
| 171 | Clock Tower - Rotating Gears Puzzle Room Item 1 | NZ1 | Star flail | 3 | loot_flag bit idx |
| 172 | Clock Tower - Rotating Gears Puzzle Room Item 2 | NZ1 | Gold plate | 4 | loot_flag bit idx |
| 173 | Clock Tower - Rotating Gears Puzzle Room Item 3 | NZ1 | Steel helm | 5 | loot_flag bit idx |
| 174 | Clock Tower - Behind Breakable Wall Close to Bronze Statue | NZ1 | Healing mail | 6 | loot_flag bit idx |
| 175 | Clock Tower - On Top of Column Item 2 | NZ1 | Bekatowa | 7 | loot_flag bit idx |
| 176 | Clock Tower - On Top of Column Item 1 | NZ1 | Shaman shield | 8 | loot_flag bit idx |
| 177 | Clock Tower - On Top of Column Item 3 | NZ1 | Ice mail | 9 | loot_flag bit idx |
| 178 | Clock Tower - Gears Puzzle Room Breakable Wall Room Left Item | NZ1 | Life Vessel | 10 | loot_flag bit idx |
| 179 | Clock Tower - Gears Puzzle Room Breakable Wall Room Right Item | NZ1 | Heart Vessel | 11 | loot_flag bit idx |
| 180 | Clock Tower - Before Karasuman Breakable Wall Item 2 | NZ1 | Bwaka knife |  | break flag 0x8003be8f & 0x4; item id is written at bin 0x55737a4 (no_offset: raw id) |
| 181 | Clock Tower - After Rotating Gears Behind Breakable Wall | NZ1 | Pot roast |  | break flag 0x8003be8f & 0x1; item id is written at bin 0x557379c (no_offset: raw id) |
| 182 | Clock Tower - Before Karasuman Breakable Wall Item 1 | NZ1 | Shuriken |  | break flag 0x8003be8f & 0x2; item id is written at bin 0x55737a0 (no_offset: raw id) |
| 183 | Clock Tower - Before Karasuman Breakable Wall Item 3 | NZ1 | TNT |  | break flag 0x8003be8f & 0x8; item id is written at bin 0x55737a8 (no_offset: raw id) |
| 184 | Clock Tower - Top Right Room in Open Area | NZ1 | Fire of bat | 2 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 185 | Castle Keep - Open Area Bottom Left on Ledge | TOP | Turquoise | 0 | loot_flag bit idx |
| 186 | Castle Keep - Open Area Bottom Left on Ledge Breakable Wall | TOP | Turkey | 1 | loot_flag bit idx |
| 187 | Castle Keep - Open Area Top Left Alcove Breakable Wall | TOP | Fire mail | 2 | loot_flag bit idx |
| 188 | Castle Keep - Top Right Room by Dual Moving Platforms | TOP | Tyrfing | 3 | loot_flag bit idx |
| 189 | Castle Keep - Hidden Stair Room Left Statue 1 | TOP | Sirloin | 4 | loot_flag bit idx |
| 190 | Castle Keep - Hidden Stair Room Left Statue 2 | TOP | Turkey | 5 | loot_flag bit idx |
| 191 | Castle Keep - Hidden Stair Room Left Yellow Vase 1 | TOP | Pot roast | 6 | loot_flag bit idx |
| 192 | Castle Keep - Hidden Stair Room Left Yellow Vase 2 | TOP | Frankfurter | 7 | loot_flag bit idx |
| 193 | Castle Keep - Hidden Stair Room Right Yellow Vase 1 | TOP | Resist stone | 8 | loot_flag bit idx |
| 194 | Castle Keep - Hidden Stair Room Right Yellow Vase 2 | TOP | Resist dark | 9 | loot_flag bit idx |
| 195 | Castle Keep - Hidden Stair Room Right Statue 1 | TOP | Resist holy | 10 | loot_flag bit idx |
| 196 | Castle Keep - Hidden Stair Room Right Statue 2 | TOP | Platinum mail | 11 | loot_flag bit idx |
| 197 | Castle Keep - Attic by Elevator Surround by Torches | TOP | Falchion | 12 | loot_flag bit idx |
| 198 | Castle Keep - Open Area Top Right Room Item 1 | TOP | Life Vessel | 13 | loot_flag bit idx |
| 199 | Castle Keep - Open Area Top Right Room Item 3 | TOP | Life Vessel | 14 | loot_flag bit idx |
| 200 | Castle Keep - Open Area Top Right Room Item 2 | TOP | Heart Vessel | 15 | loot_flag bit idx |
| 201 | Castle Keep - Open Area Top Right Room Item 4 | TOP | Heart Vessel | 16 | loot_flag bit idx |
| 202 | Castle Keep - Red Vase Before Richter | TOP | Heart Vessel | 18 | loot_flag bit idx |
| 203 | Castle Keep - Open Area Bottom Left Floor Item | TOP | Leap stone | 17 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 204 | Castle Keep - Open Area Top Left Alcove | TOP | Power of mist | 19 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 205 | Castle Keep - Open Area Top Right Room Item 5 | TOP | Ghost card | 20 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 206 | Reverse Colosseum Junction Tunnel - Breakable Floor Room | RARE | Fury plate | 0 | loot_flag bit idx |
| 207 | Reverse Colosseum Right Part - Top Right Room | RARE | Zircon | 1 | loot_flag bit idx |
| 208 | Reverse Colosseum Right Part - Top Left Room | RARE | Buffalo star | 2 | loot_flag bit idx |
| 209 | Reverse Colosseum Left Part - Top Right Room | RARE | Gram | 3 | loot_flag bit idx |
| 210 | Reverse Colosseum Left Part - Top Left Room | RARE | Aquamarine | 4 | loot_flag bit idx |
| 211 | Reverse Colosseum Left Part - Left Item on Floor | RARE | Heart Vessel | 5 | loot_flag bit idx |
| 212 | Reverse Colosseum Left Part - Middle Item on Floor | RARE | Life Vessel | 6 | loot_flag bit idx |
| 213 | Reverse Colosseum Left Part - Right Item on Floor | RARE | Heart Vessel | 7 | loot_flag bit idx |
| 214 | Floating Catacombs Bottom - After Save Point Item | RCAT | Magic missile | 0 | loot_flag bit idx |
| 215 | Floating Catacombs Bottom - After Save Point Breakable Wall | RCAT | Buffalo star | 1 | loot_flag bit idx |
| 216 | Floating Catacombs After Spike Tunnel - Top Left Vase | RCAT | Resist thunder | 2 | loot_flag bit idx |
| 217 | Floating Catacombs After Spike Tunnel - Top Right Vase | RCAT | Resist fire | 3 | loot_flag bit idx |
| 218 | Floating Catacombs After Spike Tunnel - Bottom Left Vase | RCAT | Karma coin | 4 | loot_flag bit idx |
| 219 | Floating Catacombs After Spike Tunnel - Bottom Right Vase | RCAT | Karma coin | 5 | loot_flag bit idx |
| 220 | Floating Catacombs After Spike Tunnel - Deep Left Item | RCAT | Red bean bun | 6 | loot_flag bit idx |
| 221 | Floating Catacombs After Spike Tunnel - Deep Right Item | RCAT | Elixir | 7 | loot_flag bit idx |
| 222 | Floating Catacombs After Spike Tunnel - Deep Right Breakable Wall Item | RCAT | Library card | 8 | loot_flag bit idx |
| 223 | Floating Catacombs Upper - Start of Crypt Left Item | RCAT | Life Vessel | 9 | loot_flag bit idx |
| 224 | Floating Catacombs Upper - Start of Crypt Right Item | RCAT | Heart Vessel | 10 | loot_flag bit idx |
| 225 | Floating Catacombs Upper - After Crypt Cave Upper Red Vase | RCAT | Shield potion | 11 | loot_flag bit idx |
| 226 | Floating Catacombs Upper - After Crypt Cave Bottom Red Vase | RCAT | Attack potion | 12 | loot_flag bit idx |
| 227 | Floating Catacombs Upper - After Crypt Breakable Wall Room | RCAT | Necklace of j | 13 | loot_flag bit idx |
| 228 | Floating Catacombs Upper - Before Galamoth Save Point | RCAT | Diamond | 14 | loot_flag bit idx |
| 229 | Floating Catacombs Upper - After Galamoth Left Item | RCAT | Heart Vessel | 15 | loot_flag bit idx |
| 230 | Floating Catacombs Upper - After Galamoth Right Item | RCAT | Life Vessel | 16 | loot_flag bit idx |
| 231 | Floating Catacombs Upper - After Galamoth Deeper Room Right Item | RCAT | Ruby circlet | 17 | loot_flag bit idx |
| 232 | Floating Catacombs Upper - After Galamoth Deeper Room Left Item | RCAT | Gas cloud | 18 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 233 | Cave Demon Side - Breakable Wall Room Left Item | RCHI | Power of sire | 0 | loot_flag bit idx |
| 234 | Cave Demon Side - Breakable Wall Room Right Item | RCHI | Life apple | 1 | loot_flag bit idx |
| 235 | Cave - Middle Ascend Right Item | RCHI | Alucard sword | 2 | loot_flag bit idx |
| 236 | Cave - Upper Right Room Left Item(Shared with Demon Side) | RCHI,RCHI | Green tea | 3 | loot_flag bit idx |
| 237 | Cave - Upper Right Room Right Item | RCHI | Power of sire | 4 | loot_flag bit idx |
| 238 | Cave - Upper Ascend Item 2 | RCHI | Shiitake | 6 | loot_flag bit idx |
| 239 | Cave - Upper Ascend Item 1 | RCHI | Shiitake | 7 | loot_flag bit idx |
| 240 | Cave - Death Item | RCHI,RBO2 | Eye of vlad | 5 | time-attack record 0x8003ca58 (u16) != 0; boss reward table RBO2[21] |
| 241 | Anti-Chapel Stairs - Bottom Yellow Vase | RDAI | Fire boomerang | 2 | loot_flag bit idx |
| 242 | Anti-Chapel Stairs - Red Vase Alcove 3 | RDAI | Diamond | 3 | loot_flag bit idx |
| 243 | Anti-Chapel Stairs - Red Vase at Top | RDAI | Zircon | 4 | loot_flag bit idx |
| 244 | Anti-Chapel Stairs - Red Vase Alcove 6 | RDAI | Heart Vessel | 5 | loot_flag bit idx |
| 245 | Anti-Chapel Stairs - Red Vase Alcove 5 | RDAI | Shuriken | 6 | loot_flag bit idx |
| 246 | Anti-Chapel Stairs - Red Vase Alcove 4 | RDAI | TNT | 7 | loot_flag bit idx |
| 247 | Anti-Chapel Stairs - Red Vase Alcove 2 | RDAI | Boomerang | 8 | loot_flag bit idx |
| 248 | Anti-Chapel Stairs - Red Vase Alcove 1 | RDAI | Javelin | 9 | loot_flag bit idx |
| 249 | Anti-Chapel Tower 3  - Bottom Item | RDAI | Manna prism | 10 | loot_flag bit idx |
| 250 | Anti-Chapel Tower 3 - Yellow Vase | RDAI | Smart potion | 11 | loot_flag bit idx |
| 251 | Anti-Chapel Tower 3 - Red Vase | RDAI | Life Vessel | 12 | loot_flag bit idx |
| 252 | Anti-Chapel Tower 2 - Bottom Item | RDAI | Talwar | 13 | loot_flag bit idx |
| 253 | Anti-Chapel Tower 1 - Bottom Item | RDAI | Bwaka knife | 14 | loot_flag bit idx |
| 254 | Anti-Chapel Tower 1 - Red Vase | RDAI | Magic missile | 15 | loot_flag bit idx |
| 255 | Anti-Chapel - After Spiked Tunnel | RDAI | Twilight cloak | 16 | loot_flag bit idx |
| 256 | Anti-Chapel - Next to Upper Save Point | RDAI | Heart Vessel | 17 | loot_flag bit idx |
| 257 | Medusa Kill Item | RDAI,RBO3 | Heart of vlad | 0 | time-attack record 0x8003ca64 (u16) != 0; boss reward table RBO3[17] |
| 258 | Forbidden Library - Inner Study Red Vase | RLIB | Turquoise | 0 | loot_flag bit idx |
| 259 | Forbidden Library - Inner Study Left Statue | RLIB | Opal | 1 | loot_flag bit idx |
| 260 | Forbidden Library - Inner Study Right Statue | RLIB | Library card | 2 | loot_flag bit idx |
| 261 | Forbidden Library Main Area - Bottom Right Room Left Item | RLIB | Resist fire | 3 | loot_flag bit idx |
| 262 | Forbidden Library Main Area - Bottom Right Room Middle Item | RLIB | Resist ice | 4 | loot_flag bit idx |
| 263 | Forbidden Library Main Area - Bottom Right Room Right Item | RLIB | Resist stone | 5 | loot_flag bit idx |
| 264 | Forbidden Library Inner Part - Bottom Left Room Green Candle | RLIB | Neutron bomb | 6 | loot_flag bit idx |
| 265 | Forbidden Library Inner Part - Bottom Left Room Behind Bookshelf | RLIB | Badelaire | 7 | loot_flag bit idx |
| 266 | Forbidden Library Inner Part - Behind Mist Crate | RLIB | Staurolite | 8 | loot_flag bit idx |
| 267 | Black Marble Gallery - Corridor to Entrance Item on Spike Trap | RNO0 | Library card | 0 | loot_flag bit idx |
| 268 | Black Marble Gallery - Ascend to Entrance Item on Floor 2 | RNO0 | Potion | 1 | loot_flag bit idx |
| 269 | Black Marble Gallery - Ascend to Entrance Item on Floor 1 | RNO0 | Antivenom | 2 | loot_flag bit idx |
| 270 | Black Marble Gallery - Middle Clock Right Item | RNO0 | Life Vessel | 3 | loot_flag bit idx |
| 271 | Black Marble Gallery - Middle Clock Left Item | RNO0 | Heart Vessel | 4 | loot_flag bit idx |
| 272 | Black Marble Gallery - Left Clock Second Room Item on Left | RNO0 | Resist dark | 5 | loot_flag bit idx |
| 273 | Black Marble Gallery - Left Clock Second Room Item on Right | RNO0 | Resist holy | 6 | loot_flag bit idx |
| 274 | Black Marble Gallery - Left Clock First Room Item on Left | RNO0 | Resist thunder | 7 | loot_flag bit idx |
| 275 | Black Marble Gallery - Left Clock First Room Item on Right | RNO0 | Resist fire | 8 | loot_flag bit idx |
| 276 | Black Marble Gallery - Behind Magic Blue Door | RNO0 | Meal ticket | 9 | loot_flag bit idx |
| 277 | Black Marble Gallery - Hole on the Ceiling | RNO0 | Iron ball | 10 | loot_flag bit idx |
| 278 | Black Marble Gallery - Item Inside the Clock | RNO0 | Heart refresh | 11 | loot_flag bit idx |
| 279 | Reverse Outer Wall - Item at the Top | RNO1 | Heart Vessel | 0 | loot_flag bit idx |
| 280 | Reverse Outer Wall - Mist Crate Room Left Item | RNO1 | Shotel | 1 | loot_flag bit idx |
| 281 | Reverse Outer Wall - Mist Crate Room Right Item | RNO1 | Hammer | 2 | loot_flag bit idx |
| 282 | Reverse Outer Wall - Red Vase Near Door to BMG | RNO1 | Life Vessel | 3 | loot_flag bit idx |
| 283 | Reverse Outer Wall - Yellow Vase on Alcove Near Creature | RNO1 | Luck potion | 4 | loot_flag bit idx |
| 284 | Reverse Outer Wall - Item on the Floor Near Creature | RNO1 | Shield potion | 5 | loot_flag bit idx |
| 285 | Reverse Outer Wall - Red Vase Near Creature | RNO1 | High potion | 6 | loot_flag bit idx |
| 286 | Reverse Outer Wall - Bottom Red Vase Near Elevator Machinery | RNO1 | Garnet | 7 | loot_flag bit idx |
| 287 | Reverse Outer Wall - Breakable Wall on Room Bellow Mist Crate | RNO1 | Dim sum set |  | break flag 0x8003be04 & 0x1; item id is written at bin 0x507d08c (no_offset: raw id) |
| 288 | Creature Kill Item | RNO1,RBO4 | Tooth of vlad | 8 | time-attack record 0x8003ca68 (u16) != 0; boss reward table RBO4[18] |
| 289 | Death Wing's Lair Main Area - Room Behind Breakable Wall Left Red Vase | RNO2 | Opal | 0 | loot_flag bit idx |
| 290 | Death Wing's Lair Main Area - Room Behind Breakable Wall Middle Red Vase | RNO2 | Sword of hador | 1 | loot_flag bit idx |
| 291 | Death Wing's Lair Main Area - Room Behind Breakable Wall Right Red Vase | RNO2 | High potion | 2 | loot_flag bit idx |
| 292 | Death Wing's Lair Upper Part - Top Red Vase on Shaft | RNO2 | Shield potion | 3 | loot_flag bit idx |
| 293 | Death Wing's Lair Upper Part - Middle Red Vase on Shaft | RNO2 | Luck potion | 4 | loot_flag bit idx |
| 294 | Death Wing's Lair Upper Part - Bottom Red Vase on Shaft | RNO2 | Manna prism | 5 | loot_flag bit idx |
| 295 | Death Wing's Lair - Red Vase Next to Path to Courtyard | RNO2 | Aquamarine | 6 | loot_flag bit idx |
| 296 | Death Wing's Lair Courtyard - Top Left Room | RNO2 | Alucard mail | 7 | loot_flag bit idx |
| 297 | Death Wing's Lair Path to Anti-Chapel - Bellow Wooden Pedestal | RNO2 | Life Vessel | 8 | loot_flag bit idx |
| 298 | Death Wing's Lair Path to Anti-Chapel - Breakable Floor Room | RNO2 | Heart refresh | 9 | loot_flag bit idx |
| 299 | Death Wing's Lair - Attic Before Akmodan II | RNO2 | Shuriken | 10 | loot_flag bit idx |
| 300 | Death Wing's Lair - After Akmodan II | RNO2 | Heart Vessel | 11 | loot_flag bit idx |
| 301 | Death Wing's Lair - Akmodan II Item | RNO2,RBO7 | Rib of vlad | 12 | time-attack record 0x8003ca74 (u16) != 0; boss reward table RBO7[19] |
| 302 | Reverse Entrance - Main Gate Bottom Left Item | RNO3 | Hammer | 0 | loot_flag bit idx |
| 303 | Reverse Entrance - Main Gate Bottom Right Item | RNO3 | Antivenom | 1 | loot_flag bit idx |
| 304 | Reverse Entrance - Breakable Ledge on Main Corridor | RNO3 | High potion | 2 | loot_flag bit idx |
| 305 | Reverse Entrance - Bellow Stone Pedestal | RNO3 | Heart Vessel | 3 | loot_flag bit idx |
| 306 | Reverse Entrance - Wolf/Bat Secret Room Left Item | RNO3 | Zircon | 4 | loot_flag bit idx |
| 307 | Reverse Entrance - Wolf/Bat Secret Room Middle Item | RNO3 | Opal | 5 | loot_flag bit idx |
| 308 | Reverse Entrance - Wolf/Bat Secret Room Right Item | RNO3 | Beryl circlet | 6 | loot_flag bit idx |
| 309 | Reverse Entrance - Hole in Main Corridor Back Item | RNO3 | Fire boomerang | 7 | loot_flag bit idx |
| 310 | Reverse Entrance - Middle Room in Open Area Before Main Corridor | RNO3 | Life Vessel | 8 | loot_flag bit idx |
| 311 | Reverse Entrance - Room by Nova Skeleton on the Ledge | RNO3 | Talisman | 9 | loot_flag bit idx |
| 312 | Reverse Entrance - Breakable Big Rock in Main Corridor | RNO3 | Pot roast |  | break flag 0x8003be27 & 0x1; item id is written at bin 0x51e6e4c (no_offset: raw id) |
| 313 | Reverse Caverns Upper - End of Cavern | RNO4 | Alucard shield | 0 | loot_flag bit idx |
| 314 | Reverse Caverns Upper - Near Exit | RNO4 | Shiitake | 1 | loot_flag bit idx |
| 315 | Reverse Caverns Waterfall - Alcove 1 | RNO4 | Toadstool | 2 | loot_flag bit idx |
| 316 | Reverse Caverns Waterfall - Alcove 2 | RNO4 | Shiitake | 3 | loot_flag bit idx |
| 317 | Reverse Caverns Waterfall - Bottom Right Room | RNO4 | Garnet | 4 | loot_flag bit idx |
| 318 | Reverse Caverns Bottom - Underwater Stream | RNO4 | Bat pentagram | 5 | loot_flag bit idx |
| 319 | Reverse Caverns Bottom - Underwater Top Item | RNO4 | Life Vessel | 6 | loot_flag bit idx |
| 320 | Reverse Caverns Bottom - Item on Air Pocket | RNO4 | Heart Vessel | 7 | loot_flag bit idx |
| 321 | Reverse Caverns Bottom - Underwater Bottom Item | RNO4 | Potion | 8 | loot_flag bit idx |
| 322 | Reverse Caverns Bottom - Alcove Near Water Leak | RNO4 | Shiitake | 9 | loot_flag bit idx |
| 323 | Reverse Caverns Bottom - Near Stairs Hole | RNO4 | Shiitake | 10 | loot_flag bit idx |
| 324 | Reverse Caverns Stairs - Middle Room | RNO4 | Opal | 11 | loot_flag bit idx |
| 325 | Reverse Caverns Stairs - Bottom Item | RNO4 | Life Vessel | 12 | loot_flag bit idx |
| 326 | Reverse Caverns Stairs - Bottom Item Behind Breakable Wall | RNO4 | Diamond | 13 | loot_flag bit idx |
| 327 | Reverse Caverns Bottom - Red Vase Near Exit | RNO4 | Zircon | 14 | loot_flag bit idx |
| 328 | Reverse Caverns Succubus Side - First Red Vase | RNO4 | Heart Vessel | 15 | loot_flag bit idx |
| 329 | Reverse Caverns Succubus Side - Bottom Left Red Vase | RNO4 | Meal ticket | 16 | loot_flag bit idx |
| 330 | Reverse Caverns Succubus Side - Middle Left Red Vase | RNO4 | Meal ticket | 17 | loot_flag bit idx |
| 331 | Reverse Caverns Succubus Side - Middle Right Red Vase | RNO4 | Meal ticket | 18 | loot_flag bit idx |
| 332 | Reverse Caverns Succubus Side - Top Right Red Vase | RNO4 | Meal ticket | 19 | loot_flag bit idx |
| 333 | Reverse Caverns Succubus Side - Top Left Red Vase | RNO4 | Meal ticket | 20 | loot_flag bit idx |
| 334 | Reverse Caverns Doppleganger - Item on Alcove | RNO4 | Zircon | 21 | loot_flag bit idx |
| 335 | Reverse Caverns Doppleganger - Bottom Area Left Red Vase | RNO4 | Pot roast | 22 | loot_flag bit idx |
| 336 | Reverse Caverns Doppleganger - Bottom Area Right Room | RNO4 | Dark blade | 23 | loot_flag bit idx |
| 337 | Reverse Caverns Ice Area - Underwater Alcove Item | RNO4 | Manna prism | 24 | loot_flag bit idx |
| 338 | Reverse Caverns Ice Area - Inside Cave | RNO4 | Elixir | 25 | loot_flag bit idx |
| 339 | Reverse Caverns Waterfall - Behind Waterfall Room | RNO4 | Osafune katana | 26 | loot_flag bit idx |
| 340 | Reverse Caverns Ice Area - At End | RNO4 | Force of echo | 27 | relic pedestal: owned-relic byte of the relic placed here (via ROM relic table); if converted to item: loot_flag bit idx |
| 341 | Necromancy Lab. - Breakable Wall on Tunnel Right of Elevator Shaft | RNZ0 | Heart Vessel | 1 | loot_flag bit idx |
| 342 | Necromancy Lab. - Bottom Room From Spike Traps | RNZ0 | Life Vessel | 2 | loot_flag bit idx |
| 343 | Necromancy Lab. - Middle Room on Elevator Shaft | RNZ0 | Goddess shield | 3 | loot_flag bit idx |
| 344 | Necromancy Lab. - Blue Flame in Room With Lesser and Fire Demons | RNZ0 | Manna prism | 4 | loot_flag bit idx |
| 345 | Necromancy Lab. - Breakable Ceil on Tunnel Right of Elevator Shaft | RNZ0 | Katana | 5 | loot_flag bit idx |
| 346 | Necromancy Lab. - Hole in Room With Lesser and Fire Demons | RNZ0 | High potion | 6 | loot_flag bit idx |
| 347 | Necromancy Lab. - Globe in Bitterfly Room | RNZ0 | Turquoise | 7 | loot_flag bit idx |
| 348 | Necromancy Lab. - Bottom Left Room From Beezelbub | RNZ0 | Ring of arcana | 8 | loot_flag bit idx |
| 349 | Necromancy Lab. - Globe in the Room With Lesser Demons and Ctulhu | RNZ0 | Resist dark | 9 | loot_flag bit idx |
| 350 | Reverse Clock Tower Open Area - Above Stone Bridge Left Item | RNZ1 | Magic missile | 0 | loot_flag bit idx |
| 351 | Reverse Clock Tower Open Area - Above Stone Bridge Right Item | RNZ1 | Karma coin | 1 | loot_flag bit idx |
| 352 | Reverse Clock Tower Open Area - Left Column | RNZ1 | Str. potion | 2 | loot_flag bit idx |
| 353 | Reverse Clock Tower Open Area - Middle Column | RNZ1 | Luminus | 3 | loot_flag bit idx |
| 354 | Reverse Clock Tower Open Area - Right Column | RNZ1 | Smart potion | 4 | loot_flag bit idx |
| 355 | Reverse Clock Tower Open Area - Bottom Left Room | RNZ1 | Dragon helm | 5 | loot_flag bit idx |
| 356 | Reverse Clock Tower Medusa Area - Gears Puzzle Room Left Item | RNZ1 | Diamond | 6 | loot_flag bit idx |
| 357 | Reverse Clock Tower Medusa Area - Gears Puzzle Room Middle Item | RNZ1 | Life apple | 7 | loot_flag bit idx |
| 358 | Reverse Clock Tower Medusa Area - Gears Puzzle Room Right Item | RNZ1 | Sunstone | 8 | loot_flag bit idx |
| 359 | Reverse Clock Tower Medusa Area - Room Behind Bottom Left Breakable Wall Left Item | RNZ1 | Life Vessel | 9 | loot_flag bit idx |
| 360 | Reverse Clock Tower Medusa Area - Room Behind Bottom Left Breakable Wall Right Item | RNZ1 | Heart Vessel | 10 | loot_flag bit idx |
| 361 | Reverse Clock Tower - Behind Breakable Wall Next to Bronze Statue | RNZ1 | Moon rod | 11 | loot_flag bit idx |
| 362 | Reverse Clock Tower - Near Darkwing Bat Middle Breakable Wall | RNZ1 | Bwaka knife |  | break flag 0x8003be97 & 0x4; item id is written at bin 0x59bc354 (no_offset: raw id) |
| 363 | Reverse Clock Tower - Breakable Wall Item on Brackets | RNZ1 | Pot roast |  | break flag 0x8003be97 & 0x1; item id is written at bin 0x59bc34c (no_offset: raw id) |
| 364 | Reverse Clock Tower - Near Darkwing Bat Right Breakable Wall | RNZ1 | Shuriken |  | break flag 0x8003be97 & 0x2; item id is written at bin 0x59bc350 (no_offset: raw id) |
| 365 | Reverse Clock Tower - Near Darkwing Bat Left Breakable Wall | RNZ1 | TNT |  | break flag 0x8003be97 & 0x8; item id is written at bin 0x59bc358 (no_offset: raw id) |
| 366 | Reverse Clock Tower - Darkwing Bat Item | RNZ1 | Ring of vlad | 12 | time-attack record 0x8003ca78 (u16) != 0 |
| 367 | R. Castle Keep - Open Area Top Right Breakable Wall | RTOP | Sword of dawn | 0 | loot_flag bit idx |
| 368 | R. Castle Keep - Open Area Bottom Left Underpass Breakable Wall | RTOP | Iron ball | 1 | loot_flag bit idx |
| 369 | R. Castle Keep - Red Vase After Entering | RTOP | Zircon | 2 | loot_flag bit idx |
| 370 | R. Castle Keep - Bellow Stairs Right Statue 2 | RTOP | Bastard sword | 4 | loot_flag bit idx |
| 371 | R. Castle Keep - Bellow Stairs Right Statue 1 | RTOP | Life Vessel | 5 | loot_flag bit idx |
| 372 | R. Castle Keep - Bellow Stairs Right Yellow Vase 2 | RTOP | Heart Vessel | 6 | loot_flag bit idx |
| 373 | R. Castle Keep - Bellow Stairs Right Yellow Vase 1 | RTOP | Life Vessel | 7 | loot_flag bit idx |
| 374 | R. Castle Keep - Bellow Stairs Left Yellow Vase 2 | RTOP | Heart Vessel | 8 | loot_flag bit idx |
| 375 | R. Castle Keep - Bellow Stairs Left Yellow Vase 1 | RTOP | Life Vessel | 9 | loot_flag bit idx |
| 376 | R. Castle Keep - Bellow Stairs Left Statue 1 | RTOP | Heart Vessel | 10 | loot_flag bit idx |
| 377 | R. Castle Keep - Bellow Stairs Left Statue 2 | RTOP | Royal cloak | 11 | loot_flag bit idx |
| 378 | R. Castle Keep - Open Area Bottom Right Room Item 1 | RTOP | Resist fire | 17 | loot_flag bit idx |
| 379 | R. Castle Keep - Open Area Bottom Right Room Item 2 | RTOP | Resist ice | 18 | loot_flag bit idx |
| 380 | R. Castle Keep - Open Area Bottom Right Room Item 4 | RTOP | Resist thunder | 19 | loot_flag bit idx |
| 381 | R. Castle Keep - Open Area Bottom Right Room Item 3 | RTOP | Resist stone | 20 | loot_flag bit idx |
| 382 | R. Castle Keep - Open Area Bottom Right Room Window Item | RTOP | High potion | 21 | loot_flag bit idx |
| 383 | R. Castle Keep - Open Area Top Right Ledge | RTOP | Garnet | 22 | loot_flag bit idx |
| 384 | R. Castle Keep - Bottom Left Room on Dual Elevator Area | RTOP | Lightning mail | 23 | loot_flag bit idx |
| 385 | R. Castle Keep - Bellow Save Point | RTOP | Library card | 24 | loot_flag bit idx |
| 386 | Reverse Colosseum - Trio item | RARE,RBO0 | Life Vessel | 8 | time-attack record 0x8003ca54 (u16) != 0; boss reward table RBO0[2]; Trio special-case |
| 387 | Alchemy Lab. - Slogra and Gaibon item | NZ0 | Life Vessel |  | time-attack record 0x8003ca40 (u16) != 0; boss drop tile at bin 0x54b3ad2 (RAM 0x8018285a) |
| 388 | Outer Wall - Doppleganger 10 item | NO1,BO4 | Life Vessel |  | time-attack record 0x8003ca30 (u16) != 0; boss drop tile at bin 0x624b970 (RAM 0x801842b8) |
| 389 | Long Library - Lesser Demon item | LIB | Life Vessel |  | time-attack record 0x8003ca6c (u16) != 0; boss drop tile at bin 0x47a53d2 (RAM 0x801831ca) |
| 390 | Clock Tower - Karasuman item | NZ1 | Life Vessel |  | time-attack record 0x8003ca50 (u16) != 0; boss drop tile at bin 0x5574d6e (RAM 0x801823f6) |
| 391 | Royal Chapel - Hippogryph item | DAI,BO5 | Life Vessel |  | time-attack record 0x8003ca44 (u16) != 0; boss drop tile at bin 0x6306a9c (RAM 0x801818c4) |
| 392 | Colosseum - Minotaur & Werewolf item | ARE,BO2 | Life Vessel |  | time-attack record 0x8003ca38 (u16) != 0; boss drop tile at bin 0x60fe618 (RAM 0x80181820) |
| 393 | Olrox's Quarters - Olrox item | NO2,BO0 | Life Vessel |  | time-attack record 0x8003ca2c (u16) != 0; boss drop tile at bin 0x5fac75c (RAM 0x801824d4) |
| 394 | Underground Caverns - Scylla item | NO4,BO3 | Life Vessel |  | time-attack record 0x8003ca3c (u16) != 0; boss drop tile at bin 0x61a80ae (RAM 0x80181c66) |
| 395 | Abandoned Mine - Cerberus item | CHI,BO7 | Life Vessel |  | time-attack record 0x8003ca5c (u16) != 0; boss drop tile at bin 0x66b49a2 (RAM 0x8018144a) |
| 396 | Catacombs - Granfaloon item | CAT,BO1 | Life Vessel |  | time-attack record 0x8003ca34 (u16) != 0; boss drop tile at bin 0x606f9e2 (RAM 0x80181b9a) |
| 397 | Reverse Caverns - Doppleganger 40 item | RNO4,RBO5 | Life Vessel |  | time-attack record 0x8003ca70 (u16) != 0; boss drop tile at bin 0x6866138 (RAM 0x80184350) |
| 398 | Floating Catacombs - Galamoth item | RCAT,RBO8 | Life Vessel |  | time-attack record 0x8003ca7c (u16) != 0; boss drop tile at bin 0x6a61ae6 (RAM 0x8018233e) |
| 399 | Necromancy Lab. - Beezelbub item | RNZ0,RBO1 | Life Vessel |  | time-attack record 0x8003ca48 (u16) != 0; boss drop tile at bin 0x65921d2 (RAM 0x8018155a) |

### Enemysanity locations (ap_id 400-540)

Bestiary bit: byte 0x8003BF7C + (game_id-1)//8, bit (game_id-1)%8. 'Granted' bit (client-written, so a save reload doesn't re-grant): same bit in byte 0x8003BECF if byte index 0, else 0x8003BED0 + byte index.

| ap_id | name | game_id | bestiary byte.bit | granted byte | zones |
|---|---|---|---|---|---|
| 400 | Enemysanity - Blood skeleton | 2 | 0x8003bf7c.1 | 0x8003becf | NZ0,CAT,RCAT |
| 401 | Enemysanity - Bat | 3 | 0x8003bf7c.2 | 0x8003becf | NO3,NP3,DAI,NO4,CHI,RCAT,RCHI |
| 402 | Enemysanity - Stone skull | 4 | 0x8003bf7c.3 | 0x8003becf | RARE,RNO0,RNO1 |
| 403 | Enemysanity - Zombie | 5 | 0x8003bf7c.4 | 0x8003becf | NO3,NP3 |
| 404 | Enemysanity - Merman | 6 | 0x8003bf7c.5 | 0x8003becf | NO3,NP3 |
| 405 | Enemysanity - Skeleton | 7 | 0x8003bf7c.6 | 0x8003becf | NZ0,NO0,NO1,RCAT |
| 406 | Enemysanity - Warg | 8 | 0x8003bf7c.7 | 0x8003becf | NO3,NP3 |
| 407 | Enemysanity - Bone scimitar | 9 | 0x8003bf7d.0 | 0x8003bed1 | NZ0,ARE,NO3 |
| 408 | Enemysanity - Merman(red) | 10 | 0x8003bf7d.1 | 0x8003bed1 | NO3,NP3 |
| 409 | Enemysanity - Spittle bone | 11 | 0x8003bf7d.2 | 0x8003bed1 | NZ0 |
| 410 | Enemysanity - Axe knight | 12 | 0x8003bf7d.3 | 0x8003bed1 | NZ0,NO0 |
| 411 | Enemysanity - Bloody zombie | 13 | 0x8003bf7d.4 | 0x8003bed1 | NZ0,NO2,NP3 |
| 412 | Enemysanity - Slinger | 14 | 0x8003bf7d.5 | 0x8003bed1 | NO0 |
| 413 | Enemysanity - Ouija table | 15 | 0x8003bf7d.6 | 0x8003bed1 | NO0 |
| 414 | Enemysanity - Skelerang | 16 | 0x8003bf7d.7 | 0x8003bed1 | NO0,DAI,NO2 |
| 415 | Enemysanity - Thornweed | 17 | 0x8003bf7e.0 | 0x8003bed2 | CAT,LIB,RCHI,RNO0 |
| 416 | Enemysanity - Gaibon | 18 | 0x8003bf7e.1 | 0x8003bed2 | NZ0,NP3,RCHI |
| 417 | Enemysanity - Ghost | 19 | 0x8003bf7e.2 | 0x8003bed2 | NO0 |
| 418 | Enemysanity - Marionette | 20 | 0x8003bf7e.3 | 0x8003bed2 | NO0 |
| 419 | Enemysanity - Slogra | 21 | 0x8003bf7e.4 | 0x8003bed2 | NZ0,NP3,RCHI |
| 420 | Enemysanity - Diplocephalus | 22 | 0x8003bf7e.5 | 0x8003bed2 | NO0 |
| 421 | Enemysanity - Flea man | 23 | 0x8003bf7e.6 | 0x8003bed2 | LIB,NO0 |
| 422 | Enemysanity - Medusa head | 24 | 0x8003bf7e.7 | 0x8003bed2 | NO1,NZ1,RNO0,RNO2,RNZ1 |
| 423 | Enemysanity - Blade soldier | 25 | 0x8003bf7f.0 | 0x8003bed3 | ARE |
| 424 | Enemysanity - Bone musket | 26 | 0x8003bf7f.1 | 0x8003bed3 | NO1,ARE |
| 425 | Enemysanity - Medusa head(yellow) | 27 | 0x8003bf7f.2 | 0x8003bed3 | NO1,NZ1,RNO0,RNO2,RNZ1 |
| 426 | Enemysanity - Plate lord | 28 | 0x8003bf7f.3 | 0x8003bed3 | NO0,ARE |
| 427 | Enemysanity - Stone rose | 29 | 0x8003bf7f.4 | 0x8003bed3 | NO0 |
| 428 | Enemysanity - Axe knight(armored) | 30 | 0x8003bf7f.5 | 0x8003bed3 | NO1,TOP,ARE |
| 429 | Enemysanity - Ctulhu | 31 | 0x8003bf7f.6 | 0x8003bed3 | NO0,RNO2,RNZ0 |
| 430 | Enemysanity - Bone archer | 32 | 0x8003bf7f.7 | 0x8003bed3 | NO1,NO4 |
| 431 | Enemysanity - Bone pillar | 33 | 0x8003bf80.0 | 0x8003bed4 | DAI |
| 432 | Enemysanity - Doppleganger10 | 34 | 0x8003bf80.1 | 0x8003bed4 | NO1,BO4 |
| 433 | Enemysanity - Owl | 35 | 0x8003bf80.2 | 0x8003bed4 | NO3,NP3,ARE |
| 434 | Enemysanity - Phantom skull | 36 | 0x8003bf80.3 | 0x8003bed4 | NZ1 |
| 435 | Enemysanity - Scylla wyrm | 37 | 0x8003bf80.4 | 0x8003bed4 | NO4,BO3 |
| 436 | Enemysanity - Skeleton ape | 38 | 0x8003bf80.5 | 0x8003bed4 | NO1,NO4 |
| 437 | Enemysanity - Spear guard | 39 | 0x8003bf80.6 | 0x8003bed4 | NO1,NO4 |
| 438 | Enemysanity - Spellbook | 40 | 0x8003bf80.7 | 0x8003bed4 | LIB |
| 439 | Enemysanity - Winged guard | 41 | 0x8003bf81.0 | 0x8003bed5 | DAI |
| 440 | Enemysanity - Ectoplasm | 42 | 0x8003bf81.1 | 0x8003bed5 | LIB |
| 441 | Enemysanity - Sword lord | 43 | 0x8003bf81.2 | 0x8003bed5 | NO1,NZ1 |
| 442 | Enemysanity - Toad | 44 | 0x8003bf81.3 | 0x8003bed5 | NO4 |
| 443 | Enemysanity - Armor lord | 45 | 0x8003bf81.4 | 0x8003bed5 | NO1,ARE |
| 444 | Enemysanity - Corner guard | 46 | 0x8003bf81.5 | 0x8003bed5 | DAI |
| 445 | Enemysanity - Dhuron | 47 | 0x8003bf81.6 | 0x8003bed5 | LIB |
| 446 | Enemysanity - Frog | 48 | 0x8003bf81.7 | 0x8003bed5 | NO4 |
| 447 | Enemysanity - Frozen shade | 49 | 0x8003bf82.0 | 0x8003bed6 | NO4 |
| 448 | Enemysanity - Magic tome | 50 | 0x8003bf82.1 | 0x8003bed6 | LIB |
| 449 | Enemysanity - Skull lord | 51 | 0x8003bf82.2 | 0x8003bed6 | NZ1,RTOP |
| 450 | Enemysanity - Black crow | 52 | 0x8003bf82.3 | 0x8003bed6 | DAI |
| 451 | Enemysanity - Blue raven | 53 | 0x8003bf82.4 | 0x8003bed6 | DAI |
| 452 | Enemysanity - Corpseweed | 54 | 0x8003bf82.5 | 0x8003bed6 | LIB,NO4,CHI,RCHI,RNO0 |
| 453 | Enemysanity - Flail guard | 55 | 0x8003bf82.6 | 0x8003bed6 | NZ1 |
| 454 | Enemysanity - Flea rider | 56 | 0x8003bf82.7 | 0x8003bed6 | TOP |
| 455 | Enemysanity - Spectral sword | 57 | 0x8003bf83.0 | 0x8003bed7 | NO2 |
| 456 | Enemysanity - Bone halberd | 58 | 0x8003bf83.1 | 0x8003bed7 | DAI |
| 457 | Enemysanity - Scylla | 59 | 0x8003bf83.2 | 0x8003bed7 | NO4,BO3 |
| 458 | Enemysanity - Hunting girl | 60 | 0x8003bf83.3 | 0x8003bed7 | DAI,ARE |
| 459 | Enemysanity - Owl knight | 62 | 0x8003bf83.5 | 0x8003bed7 | NO3,NP3,ARE |
| 460 | Enemysanity - Spectral sword(swords) | 63 | 0x8003bf83.6 | 0x8003bed7 | DAI |
| 461 | Enemysanity - Vandal sword | 64 | 0x8003bf83.7 | 0x8003bed7 | NZ1 |
| 462 | Enemysanity - Flea armor | 65 | 0x8003bf84.0 | 0x8003bed8 | LIB,NZ1 |
| 463 | Enemysanity - Hippogryph | 66 | 0x8003bf84.1 | 0x8003bed8 | DAI,BO5 |
| 464 | Enemysanity - Paranthropus | 67 | 0x8003bf84.2 | 0x8003bed8 | ARE,RNO1 |
| 465 | Enemysanity - Slime | 68 | 0x8003bf84.3 | 0x8003bed8 | CAT |
| 466 | Enemysanity - Blade master | 69 | 0x8003bf84.4 | 0x8003bed8 | ARE |
| 467 | Enemysanity - Wereskeleton | 70 | 0x8003bf84.5 | 0x8003bed8 | CAT |
| 468 | Enemysanity - Grave keeper | 71 | 0x8003bf84.6 | 0x8003bed8 | ARE,CAT |
| 469 | Enemysanity - Gremlin | 72 | 0x8003bf84.7 | 0x8003bed8 | CHI,CAT,RNZ0 |
| 470 | Enemysanity - Harpy | 73 | 0x8003bf85.0 | 0x8003bed9 | NZ1 |
| 471 | Enemysanity - Minotaurus | 74 | 0x8003bf85.1 | 0x8003bed9 | ARE,BO2 |
| 472 | Enemysanity - Werewolf | 75 | 0x8003bf85.2 | 0x8003bed9 | ARE,BO2 |
| 473 | Enemysanity - Bone ark | 76 | 0x8003bf85.3 | 0x8003bed9 | CAT |
| 474 | Enemysanity - Valhalla knight | 77 | 0x8003bf85.4 | 0x8003bed9 | ARE,NO2,RNZ1 |
| 475 | Enemysanity - Cloaked knight | 78 | 0x8003bf85.5 | 0x8003bed9 | NZ1,RNZ1 |
| 476 | Enemysanity - Fishhead | 79 | 0x8003bf85.6 | 0x8003bed9 | NO4 |
| 477 | Enemysanity - Lesser demon | 80 | 0x8003bf85.7 | 0x8003bed9 | LIB,RNZ0 |
| 478 | Enemysanity - Lossoth | 81 | 0x8003bf86.0 | 0x8003beda | CAT |
| 479 | Enemysanity - Salem witch | 82 | 0x8003bf86.1 | 0x8003beda | CHI,RNZ0 |
| 480 | Enemysanity - Blade | 83 | 0x8003bf86.2 | 0x8003beda | NO2,NO3,NP3,RNO0 |
| 481 | Enemysanity - Gurkha | 84 | 0x8003bf86.3 | 0x8003beda | NO3,NP3,RNO0 |
| 482 | Enemysanity - Hammer | 85 | 0x8003bf86.4 | 0x8003beda | NO2 |
| 483 | Enemysanity - Discus lord | 86 | 0x8003bf86.5 | 0x8003beda | CAT |
| 484 | Enemysanity - Karasuman | 87 | 0x8003bf86.6 | 0x8003beda | NZ1,RNO2 |
| 485 | Enemysanity - Large slime | 88 | 0x8003bf86.7 | 0x8003beda | CAT |
| 486 | Enemysanity - Hellfire beast | 89 | 0x8003bf87.0 | 0x8003bedb | CAT |
| 487 | Enemysanity - Cerberos | 90 | 0x8003bf87.1 | 0x8003bedb | CHI,BO7 |
| 488 | Enemysanity - Killer fish | 91 | 0x8003bf87.2 | 0x8003bedb | NO4,RNO4 |
| 489 | Enemysanity - Olrox | 92 | 0x8003bf87.3 | 0x8003bedb | NO2,BO0 |
| 490 | Enemysanity - Succubus | 93 | 0x8003bf87.4 | 0x8003bedb | NO4,DRE |
| 491 | Enemysanity - Tombstone | 94 | 0x8003bf87.5 | 0x8003bedb | RTOP |
| 492 | Enemysanity - Venus weed | 95 | 0x8003bf87.6 | 0x8003bedb | CHI |
| 493 | Enemysanity - Lion | 96 | 0x8003bf87.7 | 0x8003bedb | RLIB |
| 494 | Enemysanity - Scarecrow | 97 | 0x8003bf88.0 | 0x8003bedc | RLIB |
| 495 | Enemysanity - Granfaloon | 98 | 0x8003bf88.1 | 0x8003bedc | CAT,BO1 |
| 496 | Enemysanity - Schmoo | 99 | 0x8003bf88.2 | 0x8003bedc | RLIB |
| 497 | Enemysanity - Tin man | 100 | 0x8003bf88.3 | 0x8003bedc | RLIB |
| 498 | Enemysanity - Ballon pod | 101 | 0x8003bf88.4 | 0x8003bedc | RDAI,RNO4 |
| 499 | Enemysanity - Yorick | 102 | 0x8003bf88.5 | 0x8003bedc | RTOP |
| 500 | Enemysanity - Bomb knight | 103 | 0x8003bf88.6 | 0x8003bedc | RNZ1 |
| 501 | Enemysanity - Flying zombie | 104 | 0x8003bf88.7 | 0x8003bedc | RNO2 |
| 502 | Enemysanity - Bitterfly | 105 | 0x8003bf89.0 | 0x8003bedd | RNZ0 |
| 503 | Enemysanity - Jack O'bones | 106 | 0x8003bf89.1 | 0x8003bedd | RNO0,RNO1,RNO3,RNO4 |
| 504 | Enemysanity - Archer | 107 | 0x8003bf89.2 | 0x8003bedd | RDAI |
| 505 | Enemysanity - Werewolf(reverse) | 108 | 0x8003bf89.3 | 0x8003bedd | RARE |
| 506 | Enemysanity - Black panther | 109 | 0x8003bf89.4 | 0x8003bedd | RDAI |
| 507 | Enemysanity - Darkwing bat | 110 | 0x8003bf89.5 | 0x8003bedd | RNZ1 |
| 508 | Enemysanity - Dragon rider | 111 | 0x8003bf89.6 | 0x8003bedd | RNO3 |
| 509 | Enemysanity - Minotaur | 112 | 0x8003bf89.7 | 0x8003bedd | RARE |
| 510 | Enemysanity - Nova skeleton | 113 | 0x8003bf8a.0 | 0x8003bede | RNO0,RNO1,RNO3,RNO4 |
| 511 | Enemysanity - Orobourous | 114 | 0x8003bf8a.1 | 0x8003bede | RNO3 |
| 512 | Enemysanity - White dragon | 115 | 0x8003bf8a.2 | 0x8003bede | RARE |
| 513 | Enemysanity - Fire warg | 116 | 0x8003bf8a.3 | 0x8003bede | RNO3 |
| 514 | Enemysanity - Rock knight | 117 | 0x8003bf8a.4 | 0x8003bede | RNO4 |
| 515 | Enemysanity - Sniper of goth | 118 | 0x8003bf8a.5 | 0x8003bede | RDAI |
| 516 | Enemysanity - Spectral sword(shields) | 119 | 0x8003bf8a.6 | 0x8003bede | RDAI |
| 517 | Enemysanity - Ghost dancer | 120 | 0x8003bf8a.7 | 0x8003bede | RNO2 |
| 518 | Enemysanity - Warg rider | 121 | 0x8003bf8b.0 | 0x8003bedf | RNO3 |
| 519 | Enemysanity - Cave troll | 122 | 0x8003bf8b.1 | 0x8003bedf | RNO4 |
| 520 | Enemysanity - Dark octopus | 123 | 0x8003bf8b.2 | 0x8003bedf | RNO4 |
| 521 | Enemysanity - Fire demon | 124 | 0x8003bf8b.3 | 0x8003bedf | RNZ0 |
| 522 | Enemysanity - Gorgon | 125 | 0x8003bf8b.4 | 0x8003bedf | RNO0 |
| 523 | Enemysanity - Malachi | 126 | 0x8003bf8b.5 | 0x8003bedf | RNO2 |
| 524 | Enemysanity - Akmodan II | 127 | 0x8003bf8b.6 | 0x8003bedf | RNO2,RBO7 |
| 525 | Enemysanity - Blue venus weed | 128 | 0x8003bf8b.7 | 0x8003bedf | RNO3,RNO4 |
| 526 | Enemysanity - Doppleganger40 | 129 | 0x8003bf8c.0 | 0x8003bee0 | RNO4,RBO5 |
| 527 | Enemysanity - Medusa | 130 | 0x8003bf8c.1 | 0x8003bee0 | RDAI,RBO3 |
| 528 | Enemysanity - The creature | 131 | 0x8003bf8c.2 | 0x8003bee0 | RNO1,RBO4 |
| 529 | Enemysanity - Fake Grant | 132 | 0x8003bf8c.3 | 0x8003bee0 | RARE,RBO0 |
| 530 | Enemysanity - Fake Trevor | 133 | 0x8003bf8c.4 | 0x8003bee0 | RARE,RBO0 |
| 531 | Enemysanity - Imp | 134 | 0x8003bf8c.5 | 0x8003bee0 | RDAI,RNZ0,RNO4 |
| 532 | Enemysanity - Fake Sipha | 135 | 0x8003bf8c.6 | 0x8003bee0 | RARE,RBO0 |
| 533 | Enemysanity - Beezelbub | 136 | 0x8003bf8c.7 | 0x8003bee0 | RNZ0,RBO1 |
| 534 | Enemysanity - Azaghal | 137 | 0x8003bf8d.0 | 0x8003bee1 | RARE,RNO2 |
| 535 | Enemysanity - Frozen half | 138 | 0x8003bf8d.1 | 0x8003bee1 | RCAT |
| 536 | Enemysanity - Salome | 139 | 0x8003bf8d.2 | 0x8003bee1 | RCAT |
| 537 | Enemysanity - Dodo bird | 141 | 0x8003bf8d.4 | 0x8003bee1 | RNO3 |
| 538 | Enemysanity - Galamoth | 142 | 0x8003bf8d.5 | 0x8003bee1 | RCAT,RBO8 |
| 539 | Enemysanity - Guardian | 143 | 0x8003bf8d.6 | 0x8003bee1 | RNO0 |
| 540 | Enemysanity - Death | 144 | 0x8003bf8d.7 | 0x8003bee1 | RCHI,RBO2 |

## Appendix D - Every patch write grouped by function, with RAM address and code or data

From the test script (section 8.5), combining the vanilla-own, own-swapped and all-remote runs with every option on. `CODE:<fn>` = inside that function per the recomp's `config/funcmaps/*.json`. `DATA` = outside any known function; this includes the **injected routines placed in unused data space** (e.g. LIB `0x801D4600`, RCHI `0x801AA000`, DRA `0x800DB9B8`, `0x80136C00`, BO2 `0x801B5CC8`, NO0 `0x801AD91C`, NO4 `0x801DEBB4`, RNZ1 `0x801BEED0`, RARE `0x801B8A00`). `?` = a file I couldn't map without the disc. Functions that only write large data tables (write_tile_id, write_entity, enemy_stat_rando, randomize_drop, randomize_candles, randomize_shop, write_seed, cape_color, Richter/Maria colours, music) are left out here; their counts are in section 8.3.

```
### write_tokens
  ? (bin 0x438d4e8-0x438d5dc) 224 writes
  ? (bin 0x438d66c-0x438d6d4) 91 writes
  ARE   0x801b6f84-0x801b6f88   1 writes  CODE:EntityCavernDoor
  BO0   0x801824d4-0x801824d6   1 writes  DATA
  BO1   0x80181b9a-0x80181b9c   1 writes  DATA
  BO2   0x80181820-0x80181822   1 writes  DATA
  BO3   0x80181c66-0x80181c68   1 writes  DATA
  BO4   0x801842b8-0x801842ba   1 writes  DATA
  BO5   0x801818c4-0x801818c6   1 writes  DATA
  BO5   0x801a4fd4-0x801a4fd8   1 writes  CODE:func_801A4E40
  BO7   0x8018144a-0x8018144c   1 writes  DATA
  CEN   0x8018fea0-0x8018fea2   1 writes  CODE:EntityPlatform
  CHI   0x801809ea-0x801809ec   1 writes  DATA
  DRA   0x800dfb58-0x800dfc4c 224 writes  DATA
  DRA   0x800dfcdc-0x800dfd44  91 writes  DATA
  DRA   0x801173c8-0x801173cc   1 writes  CODE:ControlBatForm
  LIB   0x80181350-0x80181352   1 writes  DATA
  LIB   0x801831ca-0x801831cc   1 writes  DATA
  NO1   0x801beab0-0x801beab2   1 writes  CODE:func_us_801BE880
  NO3   0x801b9920-0x801b9924   1 writes  CODE:EntityCavernDoor
  NO3   0x801ba7cc-0x801ba7ce   1 writes  CODE:EntityMermanRockLeftSide
  NO3   0x801bb0a8-0x801bb0aa   1 writes  CODE:EntityStairwayPiece
  NO4   0x8018193c-0x8018193e   1 writes  DATA
  NP3   0x801b417c-0x801b4180   1 writes  CODE:EntityCavernDoor
  NP3   0x801b506c-0x801b506e   1 writes  CODE:EntityMermanRockLeftSide
  NP3   0x801b5948-0x801b594a   1 writes  CODE:EntityStairwayPiece
  NZ0   0x80180fa0-0x80180fa4   2 writes  DATA
  NZ0   0x8018285a-0x8018285c   1 writes  DATA
  NZ1   0x80181084-0x80181092   4 writes  DATA
  NZ1   0x801823f6-0x801823f8   1 writes  DATA
  RBO1  0x8018155a-0x8018155c   1 writes  DATA
  RBO2  0x801817b2-0x801817b4   1 writes  DATA
  RBO3  0x801812ca-0x801812cc   1 writes  DATA
  RBO4  0x801813d8-0x801813da   1 writes  DATA
  RBO5  0x80184350-0x80184352   1 writes  DATA
  RBO7  0x80181326-0x80181328   1 writes  DATA
  RBO8  0x8018233e-0x80182340   1 writes  DATA
  RNO1  0x801a7d64-0x801a7d66   1 writes  CODE:func_801A7B34
  RNO3  0x801b2f04-0x801b2f06   1 writes  CODE:func_801B2BF0
  RNZ1  0x80181014-0x80181022   4 writes  DATA
  RNZ1  0x80182598-0x8018259a   1 writes  DATA
  RNZ1  0x801a72fc-0x801a72fe   1 writes  CODE:func_801A7254
  RNZ1  0x801ac85c-0x801ac85e   1 writes  CODE:func_801AC7CC
### replace_shop_relic_with_relic
  LIB   0x801814d4-0x801814d5   1 writes  DATA
  LIB   0x801ad088-0x801ad098  16 writes  DATA
  LIB   0x801b2b08-0x801b2b09   1 writes  CODE:func_us_801B29C4
### replace_boss_relic_with_item
  RARE  0x80180a4c-0x80180a4e   1 writes  DATA
  RARE  0x801823bc-0x801823c4   3 writes  DATA
  RARE  0x8018293e-0x80182946   3 writes  DATA
  RARE  0x801a6e64-0x801a6e6c   2 writes  CODE:Update
  RARE  0x801b8a00-0x801b8a54  21 writes  DATA
  RBO0  0x80181988-0x8018198a   1 writes  DATA
  RBO0  0x8019465c-0x80194660   1 writes  CODE:RBO0_EntityBoss
  RBO2  0x80181788-0x8018178a   1 writes  DATA
  RBO2  0x8019f728-0x8019f72c   1 writes  CODE:func_8019F4AC
  RBO3  0x801812a8-0x801812aa   1 writes  DATA
  RBO3  0x80192cbc-0x80192cc0   1 writes  CODE:func_us_80192B38
  RBO4  0x801813b4-0x801813b6   1 writes  DATA
  RBO4  0x80198970-0x80198974   1 writes  CODE:func_8019879C
  RBO7  0x80181300-0x80181302   1 writes  DATA
  RBO7  0x8019400c-0x80194010   1 writes  CODE:func_80193E88
  RCHI  0x801807d6-0x801807d8   1 writes  DATA
  RCHI  0x801818f4-0x801818fc   3 writes  DATA
  RCHI  0x80181d54-0x80181d5c   3 writes  DATA
  RCHI  0x8019af18-0x8019af20   2 writes  CODE:RCHI_Update
  RCHI  0x801aa000-0x801aa054  21 writes  DATA
  RDAI  0x80180d2c-0x80180d2e   1 writes  DATA
  RDAI  0x80181dc6-0x80181dce   3 writes  DATA
  RDAI  0x80182732-0x8018273a   3 writes  DATA
  RDAI  0x801b4950-0x801b4958   2 writes  CODE:RDAI_Update
  RDAI  0x801c7900-0x801c7954  21 writes  DATA
  RNO1  0x80180af4-0x80180af6   1 writes  DATA
  RNO1  0x80182334-0x8018233c   3 writes  DATA
  RNO1  0x80182a20-0x80182a28   3 writes  DATA
  RNO1  0x801a9fc0-0x801a9fc8   2 writes  CODE:Update
  RNO1  0x801b7500-0x801b7554  21 writes  DATA
  RNO2  0x80180d58-0x80180d5a   1 writes  DATA
  RNO2  0x801829d6-0x801829de   3 writes  DATA
  RNO2  0x801831ba-0x801831c2   3 writes  DATA
  RNO2  0x801b7014-0x801b701c   2 writes  CODE:RNO2_Update
  RNO2  0x801cbf00-0x801cbf54  21 writes  DATA
### replace_trio_relic_with_item
  RARE  0x801823c0-0x801823c2   1 writes  DATA
  RARE  0x80182942-0x80182944   1 writes  DATA
### modify_drop
  ARE   0x801b9894-0x801b9898   1 writes  CODE:HitDetection
  ARE   0x801b98b0-0x801b98b4   1 writes  CODE:HitDetection
  ARE   0x801b98c0-0x801b98c4   1 writes  CODE:HitDetection
  CAT   0x801bc864-0x801bc868   1 writes  CODE:HitDetection
  CAT   0x801bc890-0x801bc894   1 writes  CODE:HitDetection
  CHI   0x8019f034-0x8019f038   1 writes  CODE:HitDetection
  CHI   0x8019f060-0x8019f064   1 writes  CODE:HitDetection
  DAI   0x801c7148-0x801c714c   1 writes  CODE:HitDetection
  DAI   0x801c7174-0x801c7178   1 writes  CODE:HitDetection
  DRA   0x800ff4c0-0x800ff4f4  13 writes  CODE:func_800FF494
  LIB   0x801c02f0-0x801c02f4   1 writes  CODE:HitDetection
  LIB   0x801c031c-0x801c0320   1 writes  CODE:HitDetection
  NO0   0x801c4948-0x801c494c   1 writes  CODE:HitDetection
  NO0   0x801c4974-0x801c4978   1 writes  CODE:HitDetection
  NO1   0x801c2bb0-0x801c2bb4   1 writes  CODE:HitDetection
  NO1   0x801c2bdc-0x801c2be0   1 writes  CODE:HitDetection
  NO2   0x801b98d4-0x801b98d8   1 writes  CODE:HitDetection
  NO2   0x801b9900-0x801b9904   1 writes  CODE:HitDetection
  NO3   0x801c2a0c-0x801c2a10   1 writes  CODE:HitDetection
  NO3   0x801c2a38-0x801c2a3c   1 writes  CODE:HitDetection
  NO4   0x801cb7b0-0x801cb7b4   1 writes  CODE:HitDetection
  NO4   0x801cb7dc-0x801cb7e0   1 writes  CODE:HitDetection
  NP3   0x801ba27c-0x801ba280   1 writes  CODE:HitDetection
  NP3   0x801ba2a8-0x801ba2ac   1 writes  CODE:HitDetection
  NZ0   0x801ba694-0x801ba698   1 writes  CODE:HitDetection
  NZ0   0x801ba6c0-0x801ba6c4   1 writes  CODE:HitDetection
  NZ1   0x801ad8a0-0x801ad8a4   1 writes  CODE:HitDetection
  NZ1   0x801ad8cc-0x801ad8d0   1 writes  CODE:HitDetection
  RARE  0x801a7d00-0x801a7d04   1 writes  CODE:HitDetection
  RARE  0x801a7d2c-0x801a7d30   1 writes  CODE:HitDetection
  RCAT  0x801b5110-0x801b5114   1 writes  CODE:HitDetection
  RCAT  0x801b513c-0x801b5140   1 writes  CODE:HitDetection
  RCHI  0x8019bdb4-0x8019bdb8   1 writes  CODE:RCHI_HitDetection
  RCHI  0x8019bde0-0x8019bde4   1 writes  CODE:RCHI_HitDetection
  RDAI  0x801b57ec-0x801b57f0   1 writes  CODE:RDAI_HitDetection
  RDAI  0x801b5818-0x801b581c   1 writes  CODE:RDAI_HitDetection
  RLIB  0x801a3d20-0x801a3d24   1 writes  CODE:RLIB_HitDetection
  RLIB  0x801a3d4c-0x801a3d50   1 writes  CODE:RLIB_HitDetection
  RNO0  0x801b84e4-0x801b84e8   1 writes  CODE:RNO0_HitDetection
  RNO0  0x801b8510-0x801b8514   1 writes  CODE:RNO0_HitDetection
  RNO1  0x801aae5c-0x801aae60   1 writes  CODE:RNO1_HitDetection
  RNO1  0x801aae88-0x801aae8c   1 writes  CODE:RNO1_HitDetection
  RNO2  0x801b7eb0-0x801b7eb4   1 writes  CODE:RNO2_HitDetection
  RNO2  0x801b7edc-0x801b7ee0   1 writes  CODE:RNO2_HitDetection
  RNO3  0x801b5070-0x801b5074   1 writes  CODE:RNO3_HitDetection
  RNO3  0x801b509c-0x801b50a0   1 writes  CODE:RNO3_HitDetection
  RNO4  0x801cb3a4-0x801cb3a8   1 writes  CODE:RNO4_HitDetection
  RNO4  0x801cb3d0-0x801cb3d4   1 writes  CODE:RNO4_HitDetection
  RNZ0  0x801addc4-0x801addc8   1 writes  CODE:HitDetection
  RNZ0  0x801addf0-0x801addf4   1 writes  CODE:HitDetection
  RNZ1  0x801ae060-0x801ae064   1 writes  CODE:RNZ1_HitDetection
  RNZ1  0x801ae08c-0x801ae090   1 writes  CODE:RNZ1_HitDetection
  RTOP  0x801a36b4-0x801a36b8   1 writes  CODE:HitDetection
  RTOP  0x801a36e0-0x801a36e4   1 writes  CODE:HitDetection
  TOP   0x801ae7f0-0x801ae7f4   1 writes  CODE:HitDetection
  TOP   0x801ae81c-0x801ae820   1 writes  CODE:HitDetection
### randomize_starting_equipment
  DRA   0x800ff800-0x800ff802   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff83c-0x800ff83e   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff850-0x800ff852   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff860-0x800ff862   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff89c-0x800ff89e   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff8b0-0x800ff8b2   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff8c0-0x800ff8c2   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff8d8-0x800ff8da   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff8ec-0x800ff8ee   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff8fc-0x800ff8fe   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff914-0x800ff916   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff928-0x800ff92a   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff938-0x800ff93a   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff958-0x800ff95a   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff96c-0x800ff96e   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff97c-0x800ff97e   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff9b8-0x800ff9ba   1 writes  CODE:InitStatsAndGear
  DRA   0x800ff9cc-0x800ff9ce   1 writes  CODE:InitStatsAndGear
  DRA   0x801001a8-0x801001aa   1 writes  CODE:InitStatsAndGear
  DRA   0x801001b4-0x801001b6   1 writes  CODE:InitStatsAndGear
  DRA   0x801001c0-0x801001c2   1 writes  CODE:InitStatsAndGear
  DRA   0x801001cc-0x801001ce   1 writes  CODE:InitStatsAndGear
  DRA   0x801001d8-0x801001da   1 writes  CODE:InitStatsAndGear
  DRA   0x801001e4-0x801001e6   1 writes  CODE:InitStatsAndGear
  NO3   0x80181ad4-0x80181ae0   6 writes  DATA
### no_prologue
  ? (bin 0x4392b1c-0x4392b1d) 1 writes
  DRA   0x800ffbc0-0x800ffbf8  14 writes  CODE:InitStatsAndGear
  DRA   0x800ffda0-0x800ffda8   2 writes  CODE:InitStatsAndGear
### map_color
  ? (bin 0x3874848-0x387484c) 1 writes
  ? (bin 0x38c0508-0x38c050c) 1 writes
### alucard_palette
  DRA   0x800db1f6-0x800db1f8   1 writes  DATA
  DRA   0x800db20a-0x800db214   5 writes  DATA
### alucard_liner
  DRA   0x800db1f8-0x800db200   4 writes  DATA
### magic_max
  ? (bin 0x3868268-0x3868288) 8 writes
  ? (bin 0x38682a8-0x38682c8) 8 writes
  ? (bin 0x38682e8-0x3868308) 8 writes
  ? (bin 0x3868328-0x3868348) 8 writes
  ? (bin 0x3868368-0x3868388) 8 writes
  ? (bin 0x38683a8-0x38683c8) 8 writes
  ? (bin 0x38683e8-0x3868408) 8 writes
  ? (bin 0x3868428-0x3868448) 8 writes
  ? (bin 0x3868468-0x3868488) 8 writes
  ? (bin 0x38684a8-0x38684c8) 8 writes
  ? (bin 0x38684e8-0x3868508) 8 writes
  ? (bin 0x3868528-0x3868548) 8 writes
  ? (bin 0x3868568-0x3868588) 8 writes
  ? (bin 0x38685a8-0x38685c8) 8 writes
  DRA   0x800fe0e8-0x800fe15c  29 writes  CODE:func_800FE044
### anti_freeze
  DRA   0x80121b74-0x80121b75   1 writes  CODE:EntityLevelUpAnimation
### my_purse
  NO3   0x801befb0-0x801befb4   1 writes  CODE:NO3_EntityCutscene
### fast_warp
  RWRP  0x8018972c-0x8018972d   1 writes  CODE:EntityRWarpRoom
  WRP   0x801878b8-0x801878b9   1 writes  CODE:EntityWarpRoom
### unlocked_patches
  ARE   0x801b6e84-0x801b6e8c   2 writes  CODE:EntityCavernDoor
  ARE   0x801b6f78-0x801b6f80   2 writes  CODE:EntityCavernDoor
  DAI   0x801c0ff4-0x801c0ffc   2 writes  CODE:EntityBlock
  NO3   0x801b96c4-0x801b96cc   2 writes  CODE:EntityCavernDoorLever
  NO3   0x801b9a34-0x801b9a3c   2 writes  CODE:EntityCavernDoor
  NO3   0x801b9d4c-0x801b9d54   2 writes  CODE:EntityWeightsSwitch
  NO3   0x801b9f5c-0x801b9f64   2 writes  CODE:EntityPathBlockSmallWeight
  NO3   0x801b9f88-0x801b9f90   2 writes  CODE:EntityPathBlockSmallWeight
  NO3   0x801ba1e4-0x801ba1ec   2 writes  CODE:EntityPathBlockTallWeight
  NO3   0x801ba20c-0x801ba214   2 writes  CODE:EntityPathBlockTallWeight
  NO3   0x801bbf9c-0x801bbfa4   2 writes  CODE:EntityHeartRoomSwitch
  NO3   0x801bc0cc-0x801bc0d4   2 writes  CODE:EntityHeartRoomGoldDoor
  NO3   0x801bc20c-0x801bc214   2 writes  CODE:EntityHeartRoomGoldDoor
  NP3   0x801b3ef8-0x801b3f00   2 writes  CODE:EntityCavernDoorLever
  NP3   0x801b4290-0x801b4298   2 writes  CODE:EntityCavernDoor
  NP3   0x801b45bc-0x801b45c4   2 writes  CODE:EntityWeightsSwitch
  NP3   0x801b47f8-0x801b4800   2 writes  CODE:EntityPathBlockSmallWeight
  NP3   0x801b4818-0x801b4820   2 writes  CODE:EntityPathBlockSmallWeight
  NP3   0x801b4a84-0x801b4a8c   2 writes  CODE:EntityPathBlockTallWeight
  NP3   0x801b4aac-0x801b4ab4   2 writes  CODE:EntityPathBlockTallWeight
  NP3   0x801b5fc4-0x801b5fcc   2 writes  CODE:EntityHeartRoomSwitch
  NP3   0x801b60f4-0x801b60fc   2 writes  CODE:EntityHeartRoomGoldDoor
  NP3   0x801b6234-0x801b623c   2 writes  CODE:EntityHeartRoomGoldDoor
  RNO3  0x80182e38-0x80182e3a   1 writes  DATA
  RNO3  0x8018354a-0x8018354c   1 writes  DATA
  RNO3  0x8019a5d6-0x8019a5da   1 writes  DATA
  RNO3  0x8019a616-0x8019a61a   1 writes  DATA
  RNO3  0x8019a656-0x8019a65a   1 writes  DATA
  RNO3  0x8019a696-0x8019a69a   1 writes  DATA
  RNO3  0x8019a6d6-0x8019a6da   1 writes  DATA
  RNO3  0x8019a716-0x8019a71a   1 writes  DATA
  RNO3  0x8019a756-0x8019a75a   1 writes  DATA
  RNO3  0x8019a796-0x8019a79a   1 writes  DATA
### surprise_patches
  DRA   0x800a8728-0x800a872c   1 writes  DATA
  DRA   0x800a8738-0x800a873c   1 writes  DATA
  DRA   0x800a8748-0x800a874c   1 writes  DATA
  DRA   0x800a8758-0x800a875c   1 writes  DATA
  DRA   0x800a8768-0x800a876c   1 writes  DATA
  DRA   0x800a8778-0x800a877c   1 writes  DATA
  DRA   0x800a8788-0x800a878c   1 writes  DATA
  DRA   0x800a8798-0x800a879c   1 writes  DATA
  DRA   0x800a87a8-0x800a87ac   1 writes  DATA
  DRA   0x800a87b8-0x800a87bc   1 writes  DATA
  DRA   0x800a87c8-0x800a87cc   1 writes  DATA
  DRA   0x800a87d8-0x800a87dc   1 writes  DATA
  DRA   0x800a87e8-0x800a87ec   1 writes  DATA
  DRA   0x800a87f8-0x800a87fc   1 writes  DATA
  DRA   0x800a8808-0x800a880c   1 writes  DATA
  DRA   0x800a8818-0x800a881c   1 writes  DATA
  DRA   0x800a8828-0x800a882c   1 writes  DATA
  DRA   0x800a8838-0x800a883c   1 writes  DATA
  DRA   0x800a8848-0x800a884c   1 writes  DATA
  DRA   0x800a8858-0x800a885c   1 writes  DATA
  DRA   0x800a8868-0x800a886c   1 writes  DATA
  DRA   0x800a8878-0x800a887c   1 writes  DATA
  DRA   0x800a8888-0x800a888c   1 writes  DATA
  DRA   0x800a8898-0x800a889c   1 writes  DATA
  DRA   0x800a88a8-0x800a88ac   1 writes  DATA
  DRA   0x800a88b8-0x800a88bc   1 writes  DATA
  DRA   0x800a88c8-0x800a88cc   1 writes  DATA
  DRA   0x800a88d8-0x800a88dc   1 writes  DATA
  DRA   0x800a88e8-0x800a88ec   1 writes  DATA
  DRA   0x800a88f8-0x800a88fc   1 writes  DATA
### start_room_rando
  DRA   0x800a2844-0x800a2850   3 writes  DATA
  DRA   0x800db9b8-0x800dba0c  21 writes  DATA
  DRA   0x80110288-0x8011028a   1 writes  CODE:func_8010FDF8
  DRA   0x801105d8-0x801105da   1 writes  CODE:CheckGravityBootsInput
  NO3   0x8018045c-0x80180461   5 writes  DATA
  NO3   0x80183cd4-0x80183ce4   4 writes  DATA
  NO3   0x801ba3a4-0x801ba3b8   5 writes  CODE:EntityTrapDoor
  TOP   0x801ac1e8-0x801ac1f0   2 writes  CODE:TOP_EntityCutscene
### rlib_card
  DRA   0x8010f26c-0x8010f270   1 writes  CODE:func_8010EDB8
  RANDO_FUNC_BLOCK(LBA24551) 0x800988bc-0x800988c8   3 writes  INJ-BLOCK
  RANDO_FUNC_BLOCK(LBA24551) 0x8009890c-0x8009897c  28 writes  INJ-BLOCK
### randomize_josephs_cloak
  DRA   0x800e4ba4-0x800e4ba8   1 writes  CODE:HandlePlay
  DRA   0x80136c00-0x80136c64  25 writes  DATA
### randomize_grav_boot_colors
  DRA   0x8011e1ac-0x8011e1b1   2 writes  CODE:EntityGravityBootBeam
  DRA   0x8011e1c2-0x8011e1ef  12 writes  CODE:EntityGravityBootBeam
### randomize_hydro_storm_color
  ? (bin 0x3a19544-0x3a19569) 5 writes
### randomize_wing_smash_color
  DRA   0x800db248-0x800db24a   1 writes  DATA
### randomize_dracula_cape
  ST0   0x8019a762-0x8019a768   3 writes  DATA
### single_hit_gears
  NZ1   0x801a8a1c-0x801a8a34   6 writes  CODE:EntityWallGear
  RNZ1  0x801a8300-0x801a8318   6 writes  CODE:func_801A81C8
### apply_acessibility_patches
  ? (bin 0x4397122-0x4397123) 1 writes
  BO0   0x801b4e9c-0x801b4e9d   1 writes  CODE:func_801B365C
  BO2   0x801a6f04-0x801a6f08   1 writes  CODE:func_801A6EF8
  BO2   0x801b5cc8-0x801b5d28  24 writes  DATA
  BO3   0x801a094c-0x801a0950   1 writes  CODE:func_801A07CC
  BO3   0x801a3514-0x801a3515   1 writes  CODE:func_801A2AEC
  DRA   0x800a2984-0x800a2985   1 writes  DATA
  DRA   0x80118c28-0x80118c2c   1 writes  CODE:func_80118C28
  NO0   0x801ad91c-0x801ad928   3 writes  DATA
  NO0   0x801cd33c-0x801cd340   1 writes  CODE:EntityClockRoomController
  NO0   0x801cd35c-0x801cd360   1 writes  CODE:EntityClockRoomController
  NZ0   0x801b7eee-0x801b7ef0   1 writes  CODE:NZ0_EntityCutscene
  NZ1   0x80182476-0x80182477   1 writes  DATA
  NZ1   0x801a8c24-0x801a8c25   1 writes  CODE:EntitySecretAreaDoor
### rando_func_master
  DRA   0x800e2e98-0x800e2ef8  24 writes  CODE:DebugEditColorChannel
  DRA   0x800e3b60-0x800e3b68   2 writes  CODE:MainGame
  RANDO_FUNC_BLOCK(LBA24551) 0x800988b0-0x80098980  52 writes  INJ-BLOCK
### replace_shop_relic_with_item
  LIB   0x8018134c-0x80181350   2 writes  DATA
  LIB   0x801814d4-0x801814d6   1 writes  DATA
  LIB   0x801b2b08-0x801b2b10   2 writes  CODE:func_us_801B29C4
  LIB   0x801b2b80-0x801b2b84   1 writes  CODE:func_us_801B29C4
  LIB   0x801b3050-0x801b3054   1 writes  CODE:func_us_801B2BE4
  LIB   0x801b317c-0x801b3184   2 writes  CODE:func_us_801B2BE4
  LIB   0x801b3638-0x801b363c   1 writes  CODE:func_us_801B2BE4
  LIB   0x801b369c-0x801b36a0   1 writes  CODE:func_us_801B2BE4
  LIB   0x801b3730-0x801b3734   1 writes  CODE:func_us_801B2BE4
  LIB   0x801b3750-0x801b3760   4 writes  CODE:func_us_801B2BE4
  LIB   0x801b431c-0x801b4324   2 writes  CODE:func_us_801B420C
  LIB   0x801b43c0-0x801b43c4   1 writes  CODE:func_us_801B420C
  LIB   0x801b4f10-0x801b4f14   1 writes  CODE:func_us_801B4ED4
  LIB   0x801b4fb4-0x801b4fb8   1 writes  CODE:func_us_801B4ED4
  LIB   0x801b59f4-0x801b59f8   1 writes  CODE:func_us_801B56E4
  LIB   0x801d4600-0x801d4664  25 writes  DATA
  LIB   0x801d4680-0x801d46cc  19 writes  DATA
  LIB   0x801d4700-0x801d4728  10 writes  DATA
### replace_holy_glasses_with_relic
  CEN   0x80181328-0x80181332   5 writes  DATA
  CEN   0x801813be-0x801813c8   5 writes  DATA
  CEN   0x8018fe98-0x8018fe9c   1 writes  CODE:EntityPlatform
### replace_gold_ring_with_relic
  NO4   0x80184278-0x8018427a   1 writes  DATA
  NO4   0x801852f6-0x801852f8   1 writes  DATA
  NO4   0x801cc590-0x801cc594   1 writes  CODE:CreateEntitiesToTheRight
  NO4   0x801debb4-0x801debf8  17 writes  DATA
### replace_ring_of_vlad_with_item
  RNZ1  0x80180ee0-0x80180ee2   1 writes  DATA
  RNZ1  0x80182570-0x80182572   1 writes  DATA
  RNZ1  0x80182dd6-0x80182dd8   1 writes  DATA
  RNZ1  0x801ac840-0x801ac850   2 writes  CODE:func_801AC7CC
  RNZ1  0x801ac85c-0x801ac86c   3 writes  CODE:func_801AC7CC
  RNZ1  0x801acb0c-0x801acb10   1 writes  CODE:func_801AC7CC
  RNZ1  0x801beed0-0x801bef44  29 writes  DATA
### replace_trio_with_relic
  RARE  0x801823be-0x801823c4   3 writes  DATA
  RARE  0x80182940-0x80182946   3 writes  DATA
  RBO0  0x8018198c-0x8018198e   1 writes  DATA
  RBO0  0x801a6088-0x801a608c   1 writes  CODE:EntityLifeUpSpawn
```
