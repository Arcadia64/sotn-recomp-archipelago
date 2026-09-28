Next:
1. In-game test pass of everything in the "not yet seen in game" list.
2. Tracker PR (location 394, enemysanity 400-540); offer the AP world fixes upstream.
# SotN Archipelago for SymphonyRecomp: plan

Goal: play SotN in an Archipelago multiworld on the PC recomp, on a **vanilla disc**, with no disc patch and no BizHawk.

Reference material (in `ref/`, not ours, not in git):
- `ref/SymphonyRecomp` - the recomp, its RecompOne runtime, and the mods submodule
- `ref/ap-world` - fdelduque AP world, Beta 0.8161 (world 0.8.16) and alpha 0.8142, plus a sparse clone
- `ref/SOTN-AP-MapTracker` - Michpem PopTracker pack 0.2.14
- `ref/archipelago` - Archipelago 0.6.7 for local seeds and a test server

Our research notes: `docs/research/ap-world-notes.md` (every AP world patch write, RAM address, option, bug),
`docs/research/ap-look-notes.md` (pickups, icons, gold, pickup text), `docs/research/options-audit.md`
(each AP option vs what the recomp supports).

---

## Status

Works in game (tested against a local AP 0.6.7 server):
- Connect (auto-reconnect, compression), scouts, messages; seed options from slot_data.
- Item placement on stage load. Stock seeds: `mod/Placement.cs` from scouts. Seeds from our AP world
  (`apworld/`): the patch's own bytes from `slot_data["recomp"]`. Offline checks: 0 differences from the
  official patch (`tools/verify_placement.py`, `tools/verify_payload.py`).
- Other players' items: an AP badge (gold progression, blue useful, grey filler, red trap) named
  "<player>'s <item>" on pickup, no gold, not added to the inventory.
- Check detection for floor items, walls, relic spots, bosses, enemysanity, goal.
- Receiving items (all kinds), only in normal play, count saved with the game; saves linked to a seed.
- remove_prologue.

Built, verified offline or by reading the code, not yet seen in game:
- Special spots: Vlad relic bosses and Trio (one pickup between boss drop and room copy), Darkwing Bat
  (drop slot fix), Gold ring / Holy glasses holding relics, the NO1/RNO1/RNO3 walls (`WallDrops.cs`).
- Librarian's Jewel of Open slot: a relic sells as that relic; an item (own or another player's) sells as
  a normal shop entry: named and badged in the list, one unit, gone once bought, the purchase is the check
  (`Special.ShopEntryWithItem`, `ApLook` shop hooks).
- DeathLink, auto_heal, Library card soft-lock escapes, Cave demon wall, tracker data storage keys.
- Always-on AP fixes (`Fixes.cs`).
- Every AP option that changes the game, including the ones the recomp can't take from the patch bytes
  alone (`OptionHooks.cs`, `OptionData.g.cs`, graphics/SEL/RIC payload keys): see the status table in
  `docs/research/options-audit.md`. Offline: payload equals the patch for every file, all 189 hooks
  resolve (`tools/check_hooks.py`).

Next:
1. In-game test pass of everything in the "not yet seen in game" list.
2. Tracker PR (location 394, enemysanity 400-540); offer the AP world fixes upstream.

---

## Setting up a PC

`ref/` is not in git. To build and test:
1. `git clone --depth 1 https://github.com/BlackLabelHQ/SymphonyRecomp.git ref/SymphonyRecomp`, then
   `git -C ref/SymphonyRecomp submodule update --init --depth 1`.
2. US bin/cue in `ref/SymphonyRecomp/disc` (exact names in its README). If a private NuGet feed is set up
   globally, add `ref/SymphonyRecomp/nuget.config` listing only nuget.org (copy `tools/modcheck/nuget.config`).
3. **Before building**, add `ref/SymphonyRecomp/Directory.Build.targets` with
   `<Project><ItemGroup><Compile Remove="mods/**" /></ItemGroup></Project>`. The game project compiles
   every `.cs` under its folder, so without this a deployed mod gets compiled into the game itself.
4. .NET 10 SDK. From `ref/SymphonyRecomp`: `dotnet build RecompOne/RecompOne.sln`, then
   `dotnet run --project RecompOne/RecompOne.Recompiler config/sotn.json`, then `dotnet build RecompOne.SoTN.csproj`
   (about 7 minutes).
5. `git clone --depth 1 --branch 0.6.7 https://github.com/ArchipelagoMW/Archipelago.git ref/archipelago`;
   Python 3.12 venv in `ref/archipelago/.venv`; `pip install -r requirements.txt "setuptools<81"`;
   `python -c "import ModuleUpdate; ModuleUpdate.update(yes=True)"`; `mkdir custom_worlds`.
6. Upstream AP world 0.8.16.1: download `sotn.apworld` from
   https://github.com/fdelduque/Archipelago/releases/tag/b08161 into `ref/ap-world/apworld-b08161/` and unzip
   it there (`tools/gen_location_data.py` reads `ref/ap-world/apworld-b08161/sotn` by default).
7. `tools/make_test_seeds.sh` generates the stock and fork test seeds; `tools/deploy-mod.sh` copies `mod/`
   into the game's `mods/archipelago` (enable it once in the game's Mods menu); `dotnet build tools/modcheck`
   compile-checks the mod. See TESTING.md.

---

## 1. How it fits together

```
 AP server  <--websocket-->  [ Archipelago mod inside SymphonyRecomp ]  --hooks-->  recompiled game
                                   |- connect / scouts / checks / received items / deathlink
                                   |- on stage load: write placements + seed tables into RAM
                                   |- hooks for spots where item ids are baked into game code
                                   |- per frame: read game flags -> send checks; give received items
                                   |- ImGui connect panel, toasts, item log
 PopTracker  <--websocket-->  AP server   (unchanged, it never reads game memory)
```

- The recomp is a static recompile: MIPS code on the disc is ignored, data on the disc is used. The AP patch is part data, part MIPS code, so the code parts would be ignored anyway. Everything is done at runtime instead, which is how the recomp's own built-in randomizer already works (`patches/rando/Randomizer.cs`, `ApplyInternalRandomizer` on stage load).
- The mod is a folder with `mod.json` and `.cs` files. The recomp compiles it at load time (Roslyn) and wires `[PreHook]`, `[PostHook]` and `[Replace]` attributes onto named game functions (MonoMod detours). Mods stack on top of the recomp's built-in patches: any number of pre/post hooks, one replace per function.
- Limitation: mods are source only, no extra DLLs. The AP connection uses `System.Net.WebSockets.ClientWebSocket` and `System.Text.Json`, both built into .NET, so no Archipelago.MultiClient.Net.
- Where seed data comes from:
  - **LocationScouts** for every own location: item id, owning player, progression/useful flags.
  - **slot_data**: all option values today, plus a new versioned `recomp` block (section 3) for anything rolled at generation time.
  - **DataPackage**: item and location names for other games, used in messages.

## 2. AP world options vs what already exists in the recomp

Status key:
- **Reuse**: the recomp already has this in C#. Many of these hooks switch on when they see the sotn.io patch bytes in RAM (for example `SecondCastleStart` checks `0x801BA3A4 == 0x34040020`, which is exactly the word the AP patch writes). So the mod can often turn them on by writing those same bytes into RAM on stage load, then later replace that with direct calls.
- **Data**: plain RAM table writes on stage load, no hook needed.
- **New hook**: needs new C# in the mod.
- **Client**: mod logic only, nothing written to game data.
- **Logic**: generation only, the mod does nothing.
- **Needs slot_data**: rolled at generation; needs the AP world change in section 3.

| AP option | Status | Notes |
|---|---|---|
| (always) item and relic placement at normal spots | Data | Same stage item tables and entity lists the built-in randomizer writes. Values from scouts. |
| (always) remote-item placeholders | Data | Money bag colour from scout flags. To be replaced with the AP look (section 5). |
| (always) boss drops, NZ1/RNZ1 walls, CHI turkey | Data | |
| (always) walls/rocks in NO1/NO3/NP3/RNO1/RNO3, Holy glasses | Reuse / New hook | Item id is a constant in code. The recomp already rewrote `EntityMermanRockLeftSide`, `EntityStairwayPiece` (NO3/NP3) and CEN `EntityPlatform`. NO1 `func_us_801BE880`, RNO1 `func_801A7B34`, RNO3 `func_801B2BF0` need new hooks. |
| (always) Vlad relics, Ring of Vlad, Trio, Gold ring, Jewel shop slot | Reuse, verify | Recomp has rewritten RBO2/3/4/7 room controllers, RNZ1 `func_801AC7CC`, RBO0 boss + `EntityLifeUpSpawn`, NO4 `func_us_801C8248`, LIB `func_us_801B29C4`. Need to confirm they read ids we can set, including "item instead of relic" cases. |
| (always) accessibility fixes | Reuse | `FunctionFixes` (Scylla door, Olrox, clock collision, Minotaur/Werewolf), `RemoveFlashes`, `MariaAlchemyCutsceneFix`, clock statue. On when `QualityOfLife.BugFixes` is on. |
| (always) Maria dialog skip after Hippogryph | New hook | BO5 `func_801A4E40`. |
| open_no4, open_are, unlocked_mode | Data | Set shortcut castle flags (`Progress.SetShortcut`), plus RNO3 tile data for unlocked_mode. |
| item_pool, boss_locations, powerful_items, no_logic | Logic | |
| enemysanity, enemy_scroll | Client | Bestiary bits at `0x8003BF7C`; Faerie scroll gate. |
| death_link, auto_heal | Client | Add a "safe to die" check (the BizHawk client lacks one). |
| difficult, enemy_mod | Data | Multiply vanilla EnemyDef stats. Fix the "easy = no drops" bug on the world side. |
| drop_mod increased / abundant | Data | EnemyDef drop rates. |
| drop_mod guaranteed | Reuse, verify | Recomp `func_800FF460` / `func_800FF494` hooks. |
| anti_freeze | Reuse | `RandoPatch.AntiFreeze` + QoL toggle. |
| fast_warp | Reuse | `RandoPatch.FastWarps`. |
| infinite_wing_smash | Reuse | `QualityOfLife.InfiniteWingSmash`. |
| remove_prologue | Reuse | `RandoPatch.SkipPrologue`. AP also clears time-attack records so bosses spawn. |
| my_purse | Reuse | `NO3_EntityCutscene_Pre` (checks the same `0x801BEFB0` word). |
| magic_vessels | Reuse + data | `MagicMaxUp_Pre`. The vessel graphics swap location still has to be found. |
| reverse_library | Reuse | Recomp's three reverse-library hooks. |
| starting_zone | Reuse (partial) + Needs slot_data | Recomp has second-castle start (`SecondCastleStart`, `OverrideIsRelicActive`). AP also has normal/any-castle rooms. Needs `start_room`. |
| rng_start_gear | Reuse + Needs slot_data | Recomp's starting-gear and Death sprite code. Needs `start_gear` so the result matches the seed. |
| random_shop, shop_prices | Reuse + Needs slot_data | Recomp's shop writer. Needs `shop`. |
| randomize_items (non-AP spots) | Data + Needs slot_data | Needs `nonpool_items`. |
| randomize_drop | Data + Needs slot_data | Recomp already writes EnemyDef drops. Needs `enemy_drops` + `global_drops`. |
| randomize_candles | Data + Needs slot_data | Needs `candles`. |
| enemy_stats | New hook + Needs slot_data | Stats are data. The "always show the name box" part patches `HitDetection` in 51 overlays: one wildcard hook. Needs `enemy_stats`. |
| skip_nz1 | New hook | NZ1 `EntityWallGear`, RNZ1 `func_801A81C8`. |
| random_music | Data + New hook + Needs slot_data | 30-byte table, plus 51 hard-coded boss/cutscene song ids. Needs `music`. |
| color_randomizer | Data + New hook + Needs slot_data | Mostly palettes; grav boots and Joseph's cloak have code parts. Needs `colors`. |
| alucard_palette, alucard_liner, relic_suprise | Data | |
| map_color | Data | RAM location still to be found. |

### Already in the recomp but not in the AP world (candidates for new options later)
- Japan-only familiar cards (Sprite card, Nosedevil card): the tracker already leaves item ids 323-324 free for them
- Transformation MP cost changes, free Gravity Boots
- Extra jumps (`ExtraJumpsOffset`)
- New final-door goals (alternate goal; the AP world has no goal option today)
- Clock statue always open
- Magic Mirror, Recycler, Target Confirmed preset items
- Prologue reward and shop randomization (built-in randomizer)

Keep comfort features (easy spell/wing/grav inputs, extra i-frames, flash removal, widescreen) as the player's own recomp settings, not AP options.

## 3. AP world fork changes

1. Move every random roll out of `generate_output` / `write_tokens` into `post_fill`, store on the world, and have both `write_tokens` and `fill_slot_data` read the stored values. (The two run at the same time in AP's thread pool, so rolling inside either one can't work.)
2. Add to slot_data:
   ```
   "recomp": {
     "version": 1,
     "nonpool_items": {...}, "start_gear": {...}, "enemy_stats": {...},
     "enemy_drops": {...}, "global_drops": {...}, "candles": {...},
     "shop": [...], "start_room": ..., "music": {...}, "colors": {...}
   }
   ```
   A few KB to tens of KB. The BizHawk patch is still produced exactly as today, so one seed works on both.
3. Fix the bugs confirmed in the notes (section 7): easy difficulty zeroing all drops; `tile_filter` editing the shared `io_items` table (candles mostly skipped); "Spike breaker"/"Gold ring"/"Silver ring" capitalisation in the drop filter; duplicate shop price address.
4. Bump the world version. The mod checks `recomp.version` on connect and refuses seeds it can't reproduce, with a plain message.

## 4. Mod parts

| Part | Hook point | Notes |
|---|---|---|
| Connection | background thread | Connect, RoomInfo, DataPackage, LocationScouts, LocationChecks, ReceivedItems, PrintJSON, Bounce (DeathLink), Set (DataStorage), StatusUpdate (goal). Queue results to the game thread; never touch game RAM from the socket thread. |
| Seed apply | `dra func_800F16D0` pre (stage load, same as built-in randomizer) | Item tiles, entity layouts, placeholders, boss drops, all "Data" rows. |
| Special spots | hooks listed in section 2 | Walls, rocks, Vlad relics, Trio, Holy glasses, Gold ring, Jewel slot, Maria skip. |
| Check detection | per frame (`dra EntityAlucard` pre) | Same flags as the BizHawk client: stage pickup bits `g_CastleFlags+0x100`, wall flags, boss time-attack records `0x8003CA28+4i`, relic bytes `0x80097964`, bestiary bits `0x8003BF7C`. |
| Item giving | per frame, only in a safe state | Inventory counts `0x8009798A+id`, order arrays `0x80097A8E`, relic bytes, HP/hearts. Not during menus, cutscenes, room transitions or death. |
| Save handling | `SaveCreatedEvent` / `SaveLoadedEvent` | Store seed + slot + received-item count with the save (the recomp's `SaveStamp` pattern). Refuse loading a save from a different seed. |
| Goal | RBO6 + Dracula HP `0x80076ED6` + room `0x6CE0` | Send StatusUpdate goal. |
| Tracker support | on room change | DataStorage `sotn_zone_<slot>` / `sotn_room_<slot>`, same values the BizHawk client writes. |
| UI | ImGui panel + `ToastNotifications.ShowText` | Server/slot/password, status, item log. |

Save storage note: the BizHawk client stores its received-item count in castle flags `0x118-0x119`, which fall inside the recomp's `Progress.ItemsCollectedIndex` range (`0xF4-0x182`). `Progress.RespawnItems()` would wipe it. Nothing calls it today, but storing our data in the save stamp instead avoids the problem.

## 5. Improvements over the BizHawk version

- AP items look like AP items: a dedicated placeholder id at AP spots, drawn with an Archipelago sprite by hooking the pickup's draw, instead of money bags and Secret boots. Colour or badge by progression/useful/filler. Money bags stay as the fallback.
- Real names: "Hookshot → Player2" when you pick up a remote item, "Got Soul of Bat from Player3" when you receive one. Pop-up plus a scrolling log panel.
- No gold added when picking up a remote item (money bags currently give $25-$400).
- Safe DeathLink: wait for a safe state; don't kill mid wing smash (known soft-lock).
- In-game hints and a `/missing` equivalent in the log panel.

## 6. Order of work

1. **Setup**: install .NET 10 SDK (only 2.1-8.0 on this machine now). Put the US bin/cue in `ref/SymphonyRecomp/disc`, run `windows_initial_build.bat`, confirm the game runs.
2. **Skeleton mod**: loads, logs stage ids and pickup flags, shows an ImGui panel.
3. **Milestone 1, stock AP world**: connect, scouts, placements, checks, receiving items, goal, DeathLink, saves. Only options that need no new slot_data (every "Needs slot_data" row off). This proves the whole loop with seeds generated by the existing AP world.
4. **AP world fork**: section 3.
5. **Milestone 2**: every remaining option.
6. **Milestone 3**: section 5 improvements; send the tracker fixes (location 394, enemysanity 400-540) to the tracker maintainer.
7. **Later**: new options from the recomp extras list.

## 7. Open questions

- Where the code lives: a standalone mod repo (drop-in folder for players) is the default; a fork of SymphonyRecomp only if the mod API can't reach something.
- Contributions: SymphonyRecomp and RecompOne refuse AI-written PRs and issues and ban repeat offenders, so this stays our own repo. Check fdelduque's stance before sending the AP world changes back.
- Recomp versions: the mod depends on recomp function names and some internal addresses. Pin a recomp release and check the mod on each new one.
