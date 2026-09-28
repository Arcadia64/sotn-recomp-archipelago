# AP options audit: which ones work in SymphonyRecomp when the mod writes the patch bytes to RAM

Researched 2026-09-28. Read-only study; nothing in the world, mod or recomp was changed.

## Status after the fixes

Everything MISSING or PARTIAL below has been implemented (not yet tested in game):

| Option | Now handled by |
|---|---|
| infinite_wing_smash | `OptionHooks` PreHook `dra/ControlBatForm_dra` (timer kept above 1 when the patch NOP is in RAM) |
| map_color, magic_vessels sprite, Richter palettes | `Recomp.py` sends F_GAME / F_GAME2 as named keys; `OptionHooks` PreHook `dra/func_801080DC` patches each streamed chunk before upload |
| unlocked_mode, open_no4, open_are | `GameRules.OpenShortcuts`: castle flags 0x30/0x31/0x32/0x60/0xB1 from the seed's options |
| enemy_stats (Faerie force) | `OptionData.g.cs` hooks on all 51 stages' HitDetection -> `OptionHooks.FaerieIn/Out` |
| drop_mod guaranteed / easy | `SeedPlan.ApplyDra` writes the recomp's always-drop switch when the DRA patch is present |
| starting_zone (reverse castle) | `OptionHooks` spoofs flag 0x96 around `top/TOP_EntityCutscene` when the patch is present |
| reverse_library | `Rom.py rlib_card` now writes the `E6 FF` name marker the recomp checks |
| random_music | `OptionData.g.cs` hooks on the 25 functions with song sites (incl. the 4 Vlad-boss rewrites that hard-code songs) + PreHook `dra/PlaySfx` |
| skip_nz1 | `OptionHooks` hooks on `nz1/EntityWallGear`, `rnz1/func_801A81C8` |
| color_randomizer | Joseph's cloak (`HandlePlay`), gravity-boot beam, wing-smash palette, Hydro Storm (`ric`), F_GAME2 (streaming hook), SEL ending palettes (`SeedPlan.ApplyResidentFiles`), MAR (stage key) |
| randomize_drop (MAR) | MAR payload key -> stage 0x17 |
| remove_prologue records | `Prologue.AfterNewGameSetup` clears the 28 time-attack records |

`tools/check_hooks.py` confirms every hook names a function in its overlay's dispatch table.

## Original audit

Scope: every option in `apworld/sotn/Options.py` that changes the game. Pure-logic options (item_pool,
boss_locations, enemysanity, enemy_scroll, no_logic, accessibility, start_inventory, powerful_items as a
pool option) are skipped. Options the mod already implements natively (remove_prologue, death_link,
auto_heal, the always-on accessibility fixes, walls, special spots) are only mentioned where this audit
found something new about them.

## Summary table

WORKS = the patch bytes the mod already writes are enough. PARTIAL = some of it works. MISSING = nothing
visible happens in the recomp (or only a cosmetic/irrelevant part).

| Option | Verdict | What's needed |
|---|---|---|
| infinite_wing_smash | MISSING | Recomp never reads the NOP at DRA `0x801173C8`. Turn on `QualityOfLife.InfiniteWingSmash` by reflection (as Fixes.cs does for BugFixes), or a PreHook on `dra/ControlBatForm` that keeps the timer `0x80137FFC` above 1 when `ReadU32(0x801173C8)==0`. |
| randomize_items | WORKS | Stage item tables/boss drops are data; wall immediates are read back by RandoPatch (NO3/NP3 rocks) and WallDrops.cs (NO1/RNO1/RNO3); CEN holy glasses by `RandoPatch.EntityPlatform`. |
| rng_start_gear | WORKS | `RandoPatch.InitStatsAndGear` reads all 24 patched immediates (`0x800FF800..0x800FF9CC`, `0x801001A8..0x801001E4`); NO3 Death-cutscene sprite ids are data. |
| map_color | MISSING | Only writes F_GAME.BIN/F_GAME2.BIN (payload key "BIN", not applied). Needs the F_GAME streaming hook (see "Files the mod doesn't apply") or a direct g_Clut/VRAM write. |
| alucard_palette | WORKS | DRA data `0x800DB1F6`, `0x800DB20A..0x800DB213`. |
| alucard_liner | WORKS | DRA data `0x800DB1F8..0x800DB1FF` (default = vanilla bytes). |
| magic_vessels | PARTIAL | Gameplay WORKS (`RandoPatch.MagicMaxUp_Pre` checks `ReadU32(0x800FE0F4)==0x10400003`, AP writes exactly that). The MP-vessel sprite (448 bytes in F_GAME.BIN) is not applied: the pickup still looks like a heart vessel. |
| anti_freeze | WORKS | `RandoPatch.AntiFreeze` checks `ReadU8(0x80121B74)==0x00`; AP writes 0x00 there. |
| my_purse | WORKS | `RandoPatch.NO3_EntityCutscene_Pre` checks `ReadU32(0x801BEFB0)!=0x14400006` (AP writes 0x18000006) and sets castle flag 0x35, which destroys the Death cutscene like the patch does. |
| fast_warp | WORKS | `RandoPatch.FastWarps` checks `ReadU8(0x801878B8)==0x02` (WRP) / `ReadU8(0x8018972C)==0x02` (RWRP); AP writes 0x02 to both; WRP/RWRP are payload zone keys. |
| unlocked_mode | PARTIAL | RNO3 data part works. The 23 `li v0,1; nop` code edits in NO3/NP3/ARE/DAI are not read by the recomp: set castle flags 0x30, 0x31, 0x32, 0x60, 0xB1 (`Shortcut` enum) instead. |
| open_no4 | MISSING | Code edit (`beq`→`bne zero,zero`) in `EntityCavernDoor` (NP3 `0x801B417C`, + NO3 `0x801B9920` for open_early) not read. Spoof/set castle flag 0x30 (EntranceToCaverns). |
| open_are | MISSING | Same for ARE `EntityCavernDoor` `0x801B6F84`: castle flag 0xB1 (ColosseumToChapel). |
| relic_suprise | WORKS | DRA relic-def data `0x800A8728+16n` (does not collide with the preset-detection words at +8). |
| enemy_stats | PARTIAL | Random stats, elements and the stats text are DRA data: WORK. The "Faerie Scroll force" (`li v0,3` in 51 HitDetection functions) is not read: the text only shows if the player really has Faerie Scroll on. Needs a Pre/Post hook pair per HitDetection that temporarily sets bit 1 of `0x80097973`. |
| difficult / enemy_mod | WORKS (easy: see drop_mod) | DRA EnemyDef HP/ATK/DEF data; shop prices are LIB data. `easy` also turns on guaranteed drops (drop_mod 3), which is PARTIAL. |
| drop_mod | increased/abundant WORKS; guaranteed PARTIAL | Rates are DRA data. Guaranteed: the DRA part is detected (`RandoPatch.func_800FF494` checks `0x3C068009`/`0x34C67BF4` at `0x800FF4C0/4`, exact match) but the per-stage roll removal in HitDetection is not. Write `0x34020100` to DRA `0x800FF460` to switch on the recomp's own "Always Drop" (`RandoPatch.func_800FF460`), which makes the luck roll pass. |
| random_shop | WORKS | LIB shop table data `0x80181354..0x80181497`; no overlap with the mod's Librarian entry 0 (`0x8018134C`). |
| shop_prices | WORKS | Same table (prices). |
| starting_zone | first castle WORKS; reverse castle PARTIAL | Destination (DRA `0x800A2844`), NO3 exit data and CHI tile fixes are data; `RandoPatch.SecondCastleStart` detects `ReadU32(0x801BA3A4)==0x34040020` (exact match) and `OverrideIsRelicActive` replaces AP's IsRelicActive routine. Missing: the TOP Richter-cutscene skip (`li v0,1` at `0x801AC1E8`). |
| reverse_library | MISSING (detection mismatch) | Recomp gates on sotn.io's renamed card: `ReadU8(0x800DD20C)==0xE6`. AP's `rlib_card` never writes that byte (sotn.io writes u16 `0xFFE6` there plus a new description at `0x800DD1E0`). Write `E6 FF` at `0x800DD20C` (Rom.py or mod); the recomp hooks then do the rest. |
| random_music | PARTIAL | Area BGM (30 bytes in the DRA stage table, `0x800A3C58+0x2C*stage`) WORKS. Of 51 boss/cutscene music immediates only 5 are read back (RNZ1 `func_801AC7CC` ×3, `RBO0_EntityBoss` ×2); the other 46 play vanilla tracks. Needs a hook that remaps the BGM request word `0x80097910` (vanilla→patched id per loaded overlay) before `RunMainEngine`/`func_800F2860` read it. |
| skip_nz1 | MISSING | Code in NZ1 `EntityWallGear` `0x801A8A1C..33` / RNZ1 `func_801A81C8` `0x801A8300..17` not read. PostHook that writes `0x000F` to the puzzle mask (NZ1 `0x80180FD0`, RNZ1 `0x80180F6C`) when a gear finishes turning. |
| color_randomizer | PARTIAL | WORK (data): capes, DOP10/DOP40 capes, wing-smash outline colour (when that variant is rolled), Dracula's cape (ST0), Maria palettes in CEN/DAI/NZ0/TOP/BO5, Richter palettes in TOP/BO2/BO6. MISSING: Joseph's cloak (injected routine), gravity-boot beam (code), wing-smash CLUT (code variant), Hydro Storm (RIC.BIN code), Richter HUD/main palettes (F_GAME2.BIN), ending palettes (SEL.BIN), Maria in the Clock Room cutscene (MAR.BIN). |
| randomize_drop | WORKS (except MAR) | EnemyDef drop ids (DRA) and per-overlay global drop tables are data. The MAR (Clock Room cutscene) global table is in "BIN" and not applied. Bone Scimitar: recomp reads the EnemyDef, not AP's immediates (different random item than the patch, still random). |
| randomize_candles | WORKS | Stage layout `state` data in stage files only. |

No detection-value mismatch was found except reverse_library (above). Every signature the recomp checks
for an option this world uses matches the bytes Rom.py writes (listed in "Recomp detections" below).

Extra finding (remove_prologue, handled by the mod): Rom.py's `no_prologue` also clears the 28
time-attack records (`0x8003CA28..0x8003CA94`) when the post-prologue NO3 init runs; vanilla only clears
them on the ST0/Richter path, which the skip bypasses. Prologue.cs only changes the stage id, so stale
records from a save loaded earlier in the session survive into the new game (bosses with a record don't
spawn, and CheckTracker would see their kill-time checks as done). Suggest zeroing those 28 words in
`Prologue.AfterNewGameSetup`. Not tested in game.

## How this was checked

- A scratch harness ran each Rom.py option function on its own (stubbed AP modules, fake world RNG),
  recorded every token, mapped each disc offset to its file through the disc's ISO9660 directory
  (`ref/SymphonyRecomp/disc`), then to RAM using the recomp's overlay bases (`config/sotn.json`), and
  marked code vs data using the recomp funcmaps. Code sites were disassembled old→new.
- Every `ReadU8/ReadU16/ReadU32` of an overlay/DRA code address in `patches/` (only `RandoPatch.cs` and
  one in `FunctionFixes.cs` have any) was compared with those sites.
- The fork's own test seed (`ref/archipelago/test-output/fork`, slot 4 with most options on) was decoded
  to confirm which bytes end up under the payload key "BIN".
- sotn.io's `src/util.js` (fetched from GitHub) was checked where the recomp's detection was written
  for sotn.io bytes (reverse library card, wing smash, anti-freeze, fast warp, my purse).

Reminders about the mod's write path (mod/SeedPlan.cs): DRA bytes are written at connect and on every
stage load; stage/boss bytes on that stage's load (PreHook `dra/func_800F16D0` from RunMainEngine).
So DRA *data* the game reads live takes effect at once, data copied elsewhere at boot or on equip takes
effect from the next time the game copies it, and code bytes only matter if a recomp rewrite reads them.

## Files the mod doesn't apply (payload key "BIN")

Recomp.py maps only DRA.BIN and the 50 AP zone files; everything else keeps its raw disc offset under
"BIN" and SeedPlan.cs skips it. Disc files were identified from the ISO directory:

| Disc offset (bin) | File (LBA, size) | File offset | Written by | Where it lives at runtime |
|---|---|---|---|---|
| `0x438D4E8..` `0x438D66C..` (and `0x4392B1C`, `0x4397122`) | `/ST/SEL/SEL.BIN` (LBA 30031, 355112 B) | `0x2A9A4..`, `0x2F594`, `0x3321A` | write_seed second copy; no_prologue; "Clear Game" accessibility patch | SEL overlay at `0x80180000` while on the title/file-select screens (stage 0x45). The seed copy is client-only (ignore). `0x801AF594` (start stage 0x41) = Prologue.cs; `0x801B321A` (Clear Game) = Fixes.cs. |
| `0x436BA7C`, `0x436BA9C` | SEL.BIN | `0xD5B4`, `0xD5D4` | color_randomizer: Maria/Richter "ending" palettes | SEL overlay RAM `0x8018D5B4`/`0x8018D5D4` (data). |
| `0x3711A68..0x3711B38` | `/WARNING.TIM` (LBA 24545) | `0x3000..` | rando_func_master + rlib_card (the CD-loaded MIPS block, sector LBA 24551, loaded to `0x800988B0`) | Never needed: the recomp never runs it. |
| `0x3874848`, `0x3874864` | `/BIN/F_GAME.BIN` (LBA 25038, 270336 B = 33×0x2000) | `0x41800` (4 B), `0x4181C` (2 B) | map_color (Alucard) | CLUT row: VRAM (0..15, 252) and the game CLUT bank mirror in RAM `0x8006EBCC + (off-0x40000)` = `0x800703CC`/`0x800703E8`. |
| `0x3868268..0x38685C7` | F_GAME.BIN | `0x36C40..0x36F9F` (14 rows × 32 B) | magic_vessels sprite | Texture only (VRAM, not kept in RAM): chunk 27 → VRAM rect x 928..943, y 433..446 (derived from the loader table; verify). |
| `0x38C0508`, `0x38C0524` | `/BIN/F_GAME2.BIN` (LBA 25170) | `0x41800`, `0x4181C` | map_color (Richter) | Same CLUT row when F_GAME2 (Richter) is loaded. |
| `0x38BE9EA..0x38BF0xx` | F_GAME2.BIN | `0x40072..0x40600` | color_randomizer: Richter HUD, pause UI, main + item-crash palettes | CLUT rows 240..242 → RAM mirror `0x8006EBCC+off-0x40000`. |
| `0x3A19544..0x3A19568` | `/BIN/RIC.BIN` (LBA 25814) | `0x2BFBC..0x2BFE0` | color_randomizer: Hydro Storm | Richter overlay at `0x8013C000` (`CopyRicOvlCallback`): RAM `0x80167FBC..` = immediates in `RicEntityCrashHydroStorm` (code, so writing them alone wouldn't help). |
| `0x650E768`, `0x6508xxx`, `0x6516xxx` | `/BOSS/MAR/MAR.BIN` (LBA 45043, 110448 B) | `0x52E0`, `0xAE4`, `0xDA5C` | Maria palette (colors), global drop table (randomize_drop *_global), Faerie force (enemy_stats, code) | Clock Room cutscene overlay, stage 0x17, loaded at `0x80180000` like any stage. |

How F_GAME/F_GAME2 load (DRA `UpdateCd`): `g_LoadFile` (`0x8006BAFC`) = 2 picks LBA `0x61CE` (F_GAME) or
`0x6252` (F_GAME2, when the stage is ST0 or Richter is playing) at `0x80108654..0x8010867C` and stores it in
the CD descriptor at `0x800AC9F8` (`{u32 lba, u32 type = 1, u32 size = 0x42000, …}`; UpdateCd copies type
to `0x80137F58` and size>>13 = 33 chunks to `0x80137F68`). Sectors stream into a 16 KB ring buffer at
`0x801EC000`; every 4 sectors `func_801080DC` LoadImages one 0x2000-byte chunk to VRAM (chunk index
`0x80137F74`; ring position `0x80137F70`: 3 → buffer `0x801EC000`, 7 → `0x801EE000`). For type 1 chunk i
goes to x = `u16 table 0x800AC958[i]`, y = ((i&2)?0x80:0)+0x100, 32×128 halfwords (chunk 26 is special:
y 0x181, 127 rows), and chunk 32 (file `0x40000..0x41FFF`) goes to the CLUT rect (0,240,256,16). After the
load, UpdateCd's post-load step for type 1 (`0x80108DA0`) StoreImages (0,240,256,16) back into RAM at
`0x8006EBCC` (the g_Clut bank at `0x8006CBCC+0x2000`). So the file is never resident in RAM as a whole.

Suggested way to apply any F_GAME/F_GAME2 bytes (map colour, vessel sprite, Richter palettes), in the
recomp's "patch the data as it arrives" spirit: PreHook `dra/func_801080DC`; when `ReadU32(0x80137F58)==1`
and `ReadU32(0x80137F70)` is 3 or 7, the chunk about to be LoadImage'd is at `0x801EC000`/`0x801EE000` and
covers file bytes `[chunk*0x2000, +0x2000)`; write the payload's bytes for that file (pick by the LBA at
`0x800AC9F8`) into the buffer. The CLUT mirror in RAM then picks them up by itself. Recomp.py should emit
named keys ("F_GAME", "F_GAME2", "RIC", "SEL", "MAR") with file offsets instead of raw "BIN" disc offsets.
Alternative for the two map-colour words only: after the load, write them to `0x8006EBCC+off-0x40000` and
to VRAM (like `Sotn.Palette.Write`, via `Runtime.Gpu.Shadow` / `GpuHle.Backend.WriteVram`).

MAR: add a "MAR" zone key (stage 0x17, file LBA 45043) to Recomp.py and `SpecialData.Zones`; SeedPlan
then applies it on stage load like any other stage.

## Recomp detections vs AP bytes

Every code-byte read in the recomp that matters for an AP option, with what Rom.py writes:

| Recomp check | Value checked | AP writes | Match |
|---|---|---|---|
| `RandoPatch.AntiFreeze` | `ReadU8(0x80121B74)==0x00` | `0x00` (`ori v0,zero,3`→`0`) | yes |
| `RandoPatch.FastWarps` | `ReadU8(0x801878B8)==0x02`, `ReadU8(0x8018972C)==0x02` | `0x02`, `0x02` | yes |
| `RandoPatch.NO3_EntityCutscene_Pre` | `ReadU32(0x801BEFB0)!=0x14400006` | `0x18000006` | yes |
| `RandoPatch.MagicMaxUp_Pre` | `a1==0x4000 && ReadU32(0x800FE0F4)==0x10400003` | `0x10400003` | yes |
| `RandoPatch.InitStatsAndGear` | `ReadU16` of 18 immediates `0x800FF800..0x800FF9CC` + `0x801001A8..0x801001E4` (+`0x801001F0` acc2, unpatched) | the same 24 halfwords | yes |
| `RandoPatch.func_800FF494` | `ReadU32(0x800FF4C0)==0x3C068009 && ReadU32(0x800FF4C4)==0x34C67BF4` | `0x3C068009`, `0x34C67BF4` | yes |
| `RandoPatch.func_800FF460` | `ReadU32(0x800FF460)==0x34020100` | not written (AP patches HitDetection instead) | n/a (usable as a switch) |
| `RandoPatch.SecondCastleStart` | `ReadU32(0x801BA3A4)==0x34040020` | `0x34040020` | yes |
| `RandoPatch.func_800F16D0` | `ReadU8(0x800F1724)` (library-card stage) | not written (AP's routine would write it at run time) | n/a |
| `RandoPatch.ReverseLibraryCard_func_8010E42C_Pre` | `ReadU8(0x800DD20C)==0xE6` | not written | **no** |
| `RandoPatch.func_801AC7CC_rnz1` / `RBO0_EntityBoss` | `ReadU16` of music immediates `0x801ACA08/AC/B24`, `0x801945CC/674` | patched ids | yes |
| `RandoPatch.EntityBoneScimitar_no3` | `ReadU16(0x800A9982/4)` (EnemyDef drops) | AP writes those and, separately, immediates `0x801D60FC/0x801D6100` | reads the def, not the immediates |

The item-placement detections (walls, holy glasses, Vlad bosses, Trio, gold ring, Ring of Vlad, jewel)
are covered in the placement notes and not repeated here.

## Per-option detail

### infinite_wing_smash
- Rom.py: `write_tokens` writes `00000000` at bin `0x134990` = DRA `0x801173C8` in `ControlBatForm`:
  `addiu v0,v0,-1` → `nop`. That instruction decrements the wing-smash timer (u32 `0x80137FFC`, set to 0x40
  at `0x80116BE4`); the smash ends when it reaches 0.
- Recomp: no rewrite reads `0x801173C8`. sotn.io's own infinite-wing-smash patch is different (byte 0 at
  bin `0x134074` = `0x80116BDC`, the initial 0x40) and isn't detected either. The recomp has its own
  setting `QualityOfLife.InfiniteWingSmash` (writes `0x80137FFC` low byte = 0 every frame in
  `QualityOfLife.Apply`, PreHook of `dra/RenderEntities`).
- Verdict: MISSING.
- Fix, either:
  - reflection, as Fixes.cs does for BugFixes: `Type.GetType("Recompiled.QualityOfLife, sotn").GetField("InfiniteWingSmash")`,
    set while connected with the option on, restore the player's value after; or
  - recomp convention: `[PreHook("dra","ControlBatForm")]`: if `m.ReadU32(0x801173C8)==0` and
    `m.ReadU32(0x80137FFC) < 2`, write 2. The timer is only written at `0x80116BE4` and `0x801173D0`, so
    this cannot disturb anything else.

### randomize_items (and powerful_items' effect on it)
- Non-pool locations get shuffled vanilla items: item-table tile ids and boss `bin_address` drops (stage
  data), and `no_offset` walls (code immediates). All are in stage files, so SeedPlan writes them.
- The immediates are read back by `RandoPatch.EntityMermanRockLeftSide_no3/np3`,
  `EntityStairwayPiece_no3/np3` (`ReadU16(0x801BA7CC/0x801B506C/0x801BB0A8/0x801B5948)`) and by the mod's
  WallDrops.cs for NO1/RNO1/RNO3; CHI turkey (ap 40) is data.
- Verdict: WORKS.

### rng_start_gear
- DRA `InitStatsAndGear`: 6 starting-equipment immediates at `0x801001A8/B4/C0/CC/D8/E4`, 18 Death
  gear-strip immediates at `0x800FF800..0x800FF9CC` (item ids and inventory offsets), plus NO3 data
  `0x80181AD4..0x80181ADF` (items Death's cutscene draws).
- `RandoPatch.InitStatsAndGear` (full replacement) reads every one of those halfwords (comments "Read Right
  Hand Starting Weapon Value from Overlay" etc.). The inventory offsets are used unsigned
  (`0x79E6+`), all < 0x8000, so no sign issue.
- Needs DRA written before the new game starts, which SeedPlan does at connect.
- Verdict: WORKS.

### map_color
- Writes only F_GAME.BIN `+0x41800` (u32: colour index 0 and 1) and, for gray/pink, `+0x4181C` (index 14),
  same in F_GAME2.BIN. Nothing in DRA or a stage.
- These are "BIN" payload bytes; SeedPlan skips them.
- Verdict: MISSING. Fix: see "Files the mod doesn't apply" (F_GAME streaming hook, or write
  `0x800703CE` (+`0x800703E8`) and VRAM (1,252) (+(14,252)) after F_GAME loads).

### alucard_palette / alucard_liner
- DRA data `0x800DB1F6`, `0x800DB20A..13` / `0x800DB1F8..FF` (Alucard CLUT source colours that the palette
  loader copies into g_Clut). Liner is always written (default = vanilla bytes).
- Verdict: WORKS (takes effect the next time the game loads Alucard's palette; SeedPlan writes DRA at
  connect, before a game is started or loaded).

### magic_vessels
- DRA `func_800FE044` `0x800FE0E8..0x800FE15B`: rewritten heart-max-up branch (hearts +5, MP max +3 and MP
  refilled, Richter returns 1). `RandoPatch.MagicMaxUp_Pre` (PreHook of `func_800FE044`) checks
  `c.A1 == 0x4000 && ReadU32(0x800FE0F4) == 0x10400003`; AP's word at `0x800FE0F4` is `0x10400003`. The
  pre-hook adds MP max +3 and refills MP; the vanilla heart path still runs, so the result equals AP's.
- Sprite: 14 rows of the vessel graphic in F_GAME.BIN `0x36C40..0x36F9F` ("BIN", not applied).
- Verdict: PARTIAL (MP vessels work, the pickup still looks like a heart vessel). Fix: F_GAME streaming hook.

### anti_freeze
- DRA `EntityLevelUpAnimation` `0x80121B74`: `ori v0,zero,3` → `0` (the freeze-frame count stored to
  `0x80097420`).
- `RandoPatch.AntiFreeze` (PostHook of `EntityLevelUpAnimation`): `if ReadU8(0x80121B74)==0 &&
  ReadU8(0x80097420)==3 → write 0`. Same byte as AP and sotn.io (bin `0x140A2C`).
- Verdict: WORKS.

### my_purse
- NO3 `NO3_EntityCutscene` `0x801BEFB0`: `bnez v0` (castle flag 0x35 already set → destroy the Death
  cutscene entity) → `blez zero` (always destroy).
- `RandoPatch.NO3_EntityCutscene_Pre`: `if ReadU32(0x801BEFB0) != 0x14400006 → WriteU8(0x8003BE21, 1)`,
  i.e. sets castle flag 0x35, which takes the same branch. Same bytes as sotn.io.
- Side difference: the flag stays set (AP only skips this branch); NO3 also reads flag 0x35 at
  `0x801BBBB4`. That is the recomp's behaviour for sotn.io too.
- Verdict: WORKS.

### fast_warp
- WRP `EntityWarpRoom` `0x801878B8` and RWRP `EntityRWarpRoom` `0x8018972C`: `addiu v0,v0,1` → `+2`.
- `RandoPatch.FastWarps` (PostHook of both) checks stage 0x0E/0x2E and those bytes == 2, then skips a warp
  step. Same bytes as sotn.io (bin `0x588BE90`, `0x5A78FE4`). WRP and RWRP are payload zone keys.
- Verdict: WORKS.

### unlocked_mode
- 23 two-instruction edits `lui v0,0x8004; lbu v0,-0x41xx(v0)` → `ori v0,zero,1; nop`, i.e. "castle flag
  is set": NO3/NP3 `EntityCavernDoorLever`, `EntityCavernDoor` (flag 0x30), `EntityWeightsSwitch`,
  `EntityPathBlockSmallWeight` ×2, `EntityPathBlockTallWeight` ×2 (0x31), `EntityHeartRoomSwitch`,
  `EntityHeartRoomGoldDoor` ×2 (0x32); ARE `EntityCavernDoor` ×2 (0xB1); DAI `EntityBlock` (0x60).
  Plus RNO3 data (tile ids `0x80182E38`, `0x8018354A`, 8 entity y words `0x8019A5D6..0x8019A79A`).
- Recomp reads none of the code sites. The RNO3 data is applied.
- Verdict: PARTIAL.
- Fix: while connected with the option on, set castle flags 0x30 (EntranceToCaverns), 0x31
  (EntranceToMarble), 0x32 (EntranceWarp), 0x60 (ChapelStatue), 0xB1 (ColosseumToChapel), e.g. with
  `Progress.SetShortcut` at new game / on stage load. Equivalent to the patch (it makes every reader see
  "opened"); the flags persist in the save, which matches the option's intent.

### open_no4
- `open` (1): NP3 `EntityCavernDoor` `0x801B417C` `beq v0,zero` → `bne zero,zero` (never takes the
  "closed" branch of flag 0x30). `open_early` (2): also NO3 `0x801B9920`.
- Not read by the recomp. Verdict: MISSING.
- Fix (mirrors the patch exactly): Pre/Post hook pair on `np3/EntityCavernDoor` (and `no3/EntityCavernDoor`
  for open_early) that, when the patched word `0x14000005` is in RAM at the site, sets
  `0x8003BE1C` (flag 0x30) to 1 for the call and restores it after. Simpler alternative: set flag 0x30
  when NP3 first loads (open) or at new game (open_early).

### open_are
- ARE `EntityCavernDoor` `0x801B6F84`: `beq v0,zero` → `bne zero,zero` (flag 0xB1).
- Not read. Verdict: MISSING. Fix: same as open_no4 with `are/EntityCavernDoor`, word `0x14000066`, flag
  byte `0x8003BE9D`; or set flag 0xB1.

### relic_suprise
- DRA relic definitions: first u32 (sprite/palette) of the 30 entries at `0x800A8728 + 0x10*n` set to one
  value. Data, read when a relic orb spawns.
- The recomp's preset detection reads the name pointers at +8 of some entries (`0x800A8840/50/80/90/A0`);
  AP's writes stop at +3, so no false preset detection.
- Verdict: WORKS.

### enemy_stats
- Data (DRA): EnemyDef HP/ATK/DEF, attack element, weakness/resist/guard/absorb, name pointers replaced by
  generated stats text (`0x800A89F0..0x800E0800`). WORKS.
- Code: 51 overlays, `HitDetection` `lui v0,0x8009; lbu v0,0x7973(v0)` → `ori v0,zero,3; nop` (act as if
  Faerie Scroll were owned and on, so the enemy "name" = stats box appears on every hit). Sites: ARE
  `0x801B91C0`, CAT `0x801BC190`, CEN `0x80191938`, CHI `0x8019E960`, DAI `0x801C6A74`, DRE `0x80197A7C`,
  LIB `0x801BFC1C`, NO0 `0x801C4274`, NO1 `0x801C24DC`, NO2 `0x801B9200`, NO3 `0x801C2338`, NO4 `0x801CB0DC`,
  NP3 `0x801B9BA8`, NZ0 `0x801B9FC0`, NZ1 `0x801AD1CC`, TOP `0x801AE11C`, WRP `0x80188CD4`, ST0
  `0x801B2214`, RARE `0x801A762C`, RCAT `0x801B4A3C`, RCEN `0x801A0F58`, RCHI `0x8019B6E0`, RDAI `0x801B5118`,
  RLIB `0x801A364C`, RNO0 `0x801B7E10`, RNO1 `0x801AA788`, RNO2 `0x801B77DC`, RNO3 `0x801B499C`, RNO4
  `0x801CACD0`, RNZ0 `0x801AD6F0`, RNZ1 `0x801AD98C`, RTOP `0x801A2FE0`, RWRP `0x8018AC54`, BO0..BO7, RBO0..RBO8,
  MAR (`0x8018DA5C`, not even written: "BIN").
- The recomp reads none of them. Note that with widescreen "Extended" on, HitDetection is replaced by
  `WidescreenPatch.HitDetection` (C#, PreHook on overlay `*`, not ST0), which reads the relic byte itself
  (`ReadU8(0x80097964+15) & 2`); without it the recompiled code reads it. Either way a spoof of the relic
  byte works.
- Verdict: PARTIAL.
- Fix: per overlay, `[PreHook(ov, "<its HitDetection>")]` save `0x80097973`, OR in bit 1 when the site
  holds `0x34020003`; `[PostHook]` restore. (Mod hooks have no "*" wildcard: `SymbolRegistry.Resolve`
  looks up exact overlay/function names, e.g. `are/HitDetection_are`, `rchi/RCHI_HitDetection`.)

### difficult / enemy_mod
- DRA EnemyDef HP/ATK/DEF scaling (`0x800A89F4..0x800AC672`), shop price range (LIB data). WORKS.
- `easy` also sets `drop_mod = 3` (guaranteed drops; the fork fixed upstream's "0" bug), so easy inherits
  the drop_mod guaranteed verdict below.

### drop_mod
- increased/abundant: EnemyDef rare/uncommon drop rates 64/32 or 128/64 (DRA data). WORKS.
- guaranteed (3):
  - DRA `func_800FF494` `0x800FF4C0..0x800FF4F3` (which drop: rare/uncommon by kill-count parity).
    `RandoPatch.func_800FF494` checks exactly `0x3C068009`/`0x34C67BF4` at `0x800FF4C0/4` and returns
    0x20/0x40 the same way. WORKS.
  - Per stage `HitDetection`: `beqz s0` after the luck roll → `nop` (always drop), `bne v1,v0` (the Jewel
    Sword gem-table branch when the hit effect is 5) → `blez zero`, and in ARE `beqz s1` after AllocEntity
    → `nop`. 27 overlays (list in Appendix). Not read.
  - Verdict: PARTIAL (drops happen at the normal luck-scaled rate, but always the enemy's own item).
  - Fix: write `0x34020100` at DRA `0x800FF460`. That is the recomp's own switch
    (`RandoPatch.func_800FF460` returns 0x100, so `(rand & 0xFF) < 0x100` always passes). The luck roll
    (`g_api` `0x8003C878`) is called once per overlay, only from HitDetection, and the widescreen C#
    HitDetection calls the same g_api entry, so this is equivalent in both paths. It also applies in boss
    overlays, which AP doesn't patch (harmless). The Jewel Sword branch difference stays (tiny).

### random_shop / shop_prices
- LIB shop table: type byte, id and u32 price per entry `0x80181354..0x80181497` (the `_lib` choices put a
  Library card in entry 1). Data. The mod's Librarian item uses entry 0 (`0x8018134C..0x80181353`), no
  overlap. (Upstream bug kept: entries 13 and 14 share `priceAddress 0x047a3100`.)
- Verdict: WORKS.

### starting_zone (start_room_rando)
- Always: DRA teleport entry 100 (`0x800A2844`: x/y, room, stage), NO3 exit data (`0x80183CD4`, 16 B:
  exit using teleport 0x64) and `0x8018045C` (5 B), CHI tile fixes when the room is in the mine. All data.
  `RandoPatch.func_800F16D0` (replacement) reads the teleport table from RAM (`0x800A2464+10*idx` = stage),
  so the warp works. First castle: WORKS.
- Reverse castle (stage >= 0x20) adds:
  - NO3 `EntityTrapDoor` `0x801BA3A4..B7` (`li a0,0x20; sw a0,g_StageId; j 0x801BA4AC`).
    `RandoPatch.SecondCastleStart` (PostHook) checks `ReadU32(0x801BA3A4)==0x34040020`: exact match. It
    sets g_StageId=0x20 and the recomp's "second start" byte `0x8000C003`.
  - DRA routine at `0x800DB9B8` + `jal` retargets in `func_8010FDF8` `0x80110288` and
    `CheckGravityBootsInput` `0x801105D8` (IsRelicActive → true in the reverse castle until the map bytes
    `0x8006BBFB`/`0x8006BCC0` are set). Not executed, but `RandoPatch.OverrideIsRelicActive` does the same
    for relics 0xC/0xD when `0x8000C003` is set (checks bits `&1`/`&4` of those bytes instead of whole
    bytes). Caveat: `0x8000C003` is plain RAM, lost after a reload/reboot (recomp limitation).
  - TOP `TOP_EntityCutscene` `0x801AC1E8`: `lui/lbu` of `0x8003BE82` (castle flag 0x96) → `li v0,1; nop`
    ("Disable Richter Cutscene"). Not read.
  - Verdict: PARTIAL. Fix for the cutscene: Pre/Post hook pair on `top/TOP_EntityCutscene` that sets byte
    `0x8003BE82` to 1 for the call when `ReadU32(0x801AC1E8)==0x34020001`, restoring it after.

### reverse_library (rlib_card + rando_func_master)
- Rom.py: DRA `func_8010EDB8` `0x8010F26C` `jal 0x8010E42C` → `jal 0x800988BC`, and a routine in the
  CD-loaded block (WARNING.TIM LBA 24551 → RAM `0x800988B0`, loaded by the `rando_func_master` hook in
  `MainGame` `0x800E3B60` → `DebugEditColorChannel` `0x800E2E98`). The routine checks pad byte
  `0x8003925D` (Down) and `0x8006BBFB`, then writes the stage byte into code at `0x800F1724`
  (`func_800F16D0`) and `0x800F32A4` (`RunMainEngine`), `0x88BE`/`0x7C0E` to `0x800A3C98`, and jumps to
  `func_8010E42C`. None of it runs in the recomp (the recompiled `jal` still calls `func_8010E42C`).
- Recomp: three hooks (`ReverseLibraryCard_func_8010E42C_Pre`, `_func_800F16D0_Post`,
  `_func_800F223C_Pre`) plus `RandoPatch.func_800F16D0`. The first one returns at once unless
  `ReadU8(0x800DD20C) == 0xE6`: the down-arrow glyph sotn.io puts at the end of the Library card's name
  (sotn.io `applyRLBCPatches`: `writeShort(0xf1e14, 0xffe6)` = DRA `0x800DD20C` ← `E6 FF`, plus a new
  description at bin `0xf1de8` = `0x800DD1E0`). AP's `rlib_card` writes only the hook and routine. Then it
  requires Richter saved (`ReadU32(0x8003CA60)!=0`, time-attack 14) and Down held
  (`ReadU16(0x80097490)&0x4000`), sets stage 0x22 and `0x88BE`.
- Verdict: MISSING (detection mismatch).
- Fix: make the gate true. Best in Rom.py `rlib_card` (write u16 `0xFFE6` at bin `0xF1E14`, optionally
  sotn.io's description words at bin `0xF1DE8`), which lands in the DRA payload; or have the mod write
  `E6 FF` at `0x800DD20C` when `reverse_library` is on. Vanilla there is `FF 00` (terminator + pad) after
  "CARD", so the name gains a down arrow, as in sotn.io. Condition difference: AP's routine tests the
  map byte `0x8006BBFB` (non-zero), the recomp tests the Save Richter time-attack record `0x8003CA60`;
  the option text ("after Richter is saved") matches the recomp's test.

### random_music
- DRA stage table music byte (`0x800A3C40 + 0x2C*stage + 0x18`, 30 stages): data, WORKS for area BGM.
- 51 boss/cutscene immediates (`ori vX,zero,0x3xx` low byte) in LIB `func_us_801BB53C`, NO3
  `EntityBackgroundLightning`/`EntityDeathCutsceneManager`, NZ0 `EntityBossFightManager`, NZ1
  `EntityBossDoorTrigger`, RCEN `func_8019AAFC`, RNZ1 `func_801AC7CC`, DRE `EntitySuccubus`, BO0/BO1/BO2/BO3/
  BO5/BO7, RBO0/1/2/3/4/5/7/8 boss controllers. Only 5 are read back: RNZ1 `0x801ACA08`, `0x801ACAAC`,
  `0x801ACB24` (`RandoPatch.func_801AC7CC_rnz1`) and RBO0 `0x801945CC`, `0x80194674`
  (`RandoPatch.RBO0_EntityBoss`). RandoPatch's rewrites of the RBO2/3/4/7 controllers read only the
  reward index, not the music.
- Effect: boss themes and the "area music comes back after the event" calls stay vanilla, so e.g. after
  a boss the area track switches to the vanilla one.
- Verdict: PARTIAL.
- How those sites work: each is `ori vX,zero,0x3xx` followed by `sw vX, 0x80097910` (the requested BGM
  track, u32); DRA reads that word in `RunMainEngine` (4 places) and `func_800F2860` (the recomp's
  FairySongPatch already hooks it), which start the track.
- Fix: at each stage load SeedPlan writes the payload's music sites for that overlay; just before writing,
  read the vanilla halfword at each site and build a vanilla→patched id map for the loaded overlay. Then a
  PreHook on `dra/func_800F2860` and `dra/RunMainEngine` swaps the word at `0x80097910` when it holds a
  vanilla id from that map. Caveat: if an area's new track (from the DRA table) happens to equal a vanilla
  id in the same overlay's map it would be swapped too; checking that the value changed since the last
  hook call narrows this.

### skip_nz1 (single_hit_gears)
- NZ1 `EntityWallGear` `0x801A8A1C..0x801A8A33`: when a gear finishes a turn, instead of setting/clearing
  its own bit in the puzzle mask `0x80180FD0`, write `0x000F` (all solved) there, play sound 0x676 and
  continue. RNZ1 `func_801A81C8` `0x801A8300..0x801A8317` the same with `0x80180F6C`.
- Not read by the recomp (FunctionFixes.ClockCollisionFix only touches `EntitySecretAreaDoor`).
- Verdict: MISSING.
- Fix: PreHook `nz1/EntityWallGear` remembers the entity (`a0`) and its turn countdown (`+0x80`); PostHook:
  if `ReadU32(0x801A8A1C)==0x2403000F` and the countdown went 1 → 0 in this call, write u16 `0x000F` to
  `0x80180FD0`. Same for `rnz1/func_801A81C8` with `0x801A8300` and `0x80180F6C`.

### color_randomizer (all parts)
| Part | Where | Kind | Verdict / fix |
|---|---|---|---|
| Capes (Cloth, Reverse/Inverted, Elven, Crystal, Royal, Blood, Twilight) | DRA `0x800A37FC..0x800A3847` | data (palette-effect sources, pointed to by the table at `0x800A38E0..`) | WORKS |
| DOP10 / DOP40 capes | BO4 `0x801AC294`, RBO5 `0x801AC36C` | data | WORKS |
| Joseph's cloak | DRA `HandlePlay` `0x800E4BA4` `jal 0x800E493C`→`jal 0x80136C00` + 25-word routine at `0x80136C00` storing 6 colours to `0x0003CAA8+4i` | code | MISSING. Fix: when `ReadU32(0x800E4BA4)==0x0C04DB00` (e.g. PostHook `dra/HandlePlay`), copy `ReadU8(0x80136C08+16*i)` to `0x8003CAA8+4*i`, i=0..5 (the custom cloak colours in g_Settings). |
| Gravity boots beam | DRA `EntityGravityBootBeam`: colour immediates `0x8011E1AC`/`0x8011E1B0` and the source register of 12 `sb` at `0x8011E1C0..0x8011E1EC` | code | MISSING. Fix: PostHook on the init step (`+0x2C` step 0): walk the entity's prims (`+0x64` index into g_PrimBuf `0x80086FEC`, 0x34 each, `next` chain) and set each of the 12 bytes to 0 / `ReadU8(0x8011E1AC)` / `ReadU8(0x8011E1B0)` according to the `rt` field (0/4/5) of the patched `sb` at `0x8011E1C0+4k`, offset = its immediate. |
| Hydro Storm | RIC.BIN `RicEntityCrashHydroStorm` `0x80167FBC..0x80167FE0` (5 colour immediates stored into prim bytes +4,+6,+0x10,+0x11,…) | code in an unapplied file | MISSING (Richter only). Needs a "RIC" payload key and a hook on `ric/RicEntityCrashHydroStorm` that rewrites those prim bytes. |
| Wing smash | either DRA `0x800DB248` (outline colour, data) or `EntityWingSmashTrail` `0x8011E438` (`ori v0,zero,0x81xx`, stored to entity `+0x16` palette) | data or code (seed-dependent) | Data variant WORKS; CLUT variant MISSING. Fix: PostHook `dra/EntityWingSmashTrail`: trail entities whose palette is `0x8102` get `ReadU16(0x8011E438)`. |
| Dracula's cape | ST0 `0x8019A762` | data | WORKS |
| Richter | F_GAME2.BIN `0x40072..0x40600` (HUD, pause UI, main palette, item-crash palettes), SEL.BIN `0xD5D4` (ending), TOP `0x80191654`, BO2 `0x80193EBA`, BO6 `0x8019E9A0`/`0x8019EB00` | data | TOP/BO2/BO6 WORK; F_GAME2 and SEL MISSING (unapplied files; F_GAME streaming hook / SEL key). |
| Maria | SEL.BIN `0xD5B4` (ending), CEN `0x8018698C`, DAI `0x8019780C`, NZ0 `0x8019663C`, TOP `0x801911F4`, BO5 `0x8018F024`, MAR.BIN `0x52E0` (Clock Room) | data | all but SEL and MAR WORK; MAR needs the "MAR" zone key. |

Verdict: PARTIAL.

### randomize_drop
- DRA EnemyDef rare/uncommon drop ids (`0x800A8A0A..0x800AC6FB`): data, WORKS.
- `*_global` choices: the 32-entry global drop table in each stage/boss overlay (64 B): data, WORKS for the
  50 zone files; MAR's (`MAR.BIN 0xAE4`) is in "BIN" and not applied.
- NO3 `EntityBoneScimitar` immediates `0x801D60FC`/`0x801D6100`: `RandoPatch.EntityBoneScimitar_no3`
  replaces them with reads of the Bone Scimitar EnemyDef (`ReadU16(0x800A9984/2) - 0x80`). Vanilla the two
  agree; Rom.py rolls them separately (test seeds: immediates `0xD1/0x18` vs def items `0xE9/0xF5`), so the
  scimitar drops the def's random item, not the immediate's. Still randomized.
- Verdict: WORKS (MAR table missing until a "MAR" key exists).

### randomize_candles
- Stage layout `state` words (`(candle_type<<8)|item`) in the 27 normal/reverse stage files only (ST0
  excluded by Rom.py). Data.
- Verdict: WORKS.

### Always-on writes (for reference)
- Maria dialog skip BO5 `0x801A4FD4`, accessibility patches: handled by Fixes.cs / recomp BugFixes.
- `rando_func_master` (DRA `MainGame` `0x800E3B60`, `DebugEditColorChannel` `0x800E2E98`, WARNING.TIM
  block): inert in the recomp; nothing needed except the reverse_library fix above.
- DRA seed block `0x800DFAEC..0x800DFD44` is excluded by Recomp.py; its SEL.BIN copy is in "BIN" (ignore).

## Appendix: guaranteed-drop HitDetection sites (drop_mod 3 / easy)

ARE `0x801B9894`, `0x801B98B0`, `0x801B98C0`; CAT `0x801BC864`, `0x801BC890`; CHI `0x8019F034`,
`0x8019F060`; DAI `0x801C7148`, `0x801C7174`; LIB `0x801C02F0`, `0x801C031C`; NO0 `0x801C4948`,
`0x801C4974`; NO1 `0x801C2BB0`, `0x801C2BDC`; NO2 `0x801B98D4`, `0x801B9900`; NO3 `0x801C2A0C`,
`0x801C2A38`; NO4 `0x801CB7B0`, `0x801CB7DC`; NP3 `0x801BA27C`, `0x801BA2A8`; NZ0 `0x801BA694`,
`0x801BA6C0`; NZ1 `0x801AD8A0`, `0x801AD8CC`; TOP `0x801AE7F0`, `0x801AE81C`; RARE `0x801A7D00`,
`0x801A7D2C`; RCAT `0x801B5110`, `0x801B513C`; RCHI `0x8019BDB4`, `0x8019BDE0`; RDAI `0x801B57EC`,
`0x801B5818`; RLIB `0x801A3D20`, `0x801A3D4C`; RNO0 `0x801B84E4`, `0x801B8510`; RNO1 `0x801AAE5C`,
`0x801AAE88`; RNO2 `0x801B7EB0`, `0x801B7EDC`; RNO3 `0x801B5070`, `0x801B509C`; RNO4 `0x801CB3A4`,
`0x801CB3D0`; RNZ0 `0x801ADDC4`, `0x801ADDF0`; RNZ1 `0x801AE060`, `0x801AE08C`; RTOP `0x801A36B4`,
`0x801A36E0`. Pattern: `beqz s0,…` → `nop` (first), `bne v1,v0,+13` → `blez zero,+13` (second).
