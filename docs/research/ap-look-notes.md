# AP look for other players' items: research notes

Scope: how SotN (US, recomp) spawns, draws and collects stage pickups, and how the mod can:
1. draw an Archipelago icon instead of money bags / Secret boots,
2. show "Hookshot (Maria)" when one is picked up,
3. give no gold (and no placeholder item).

All line numbers are in `ref/SymphonyRecomp/generated/*.cs` unless another file is named. NO0 is used as the worked example; every stage and boss overlay has its own copy of the stage-side functions (table in section 1.6).

Checked against the actual disc: DRA.BIN was pulled out of Track 1 (LBA 299, loads at 0x800A0000) to read the item/relic def tables and the icon tables (script in the session scratchpad; not kept in the repo).

---

## 0. Short version

- Stop using money bags / Secret boots. Put a **dedicated placeholder item** in every spot that holds another player's item: accessory index 57, the "----" (empty accessory) entry, **item id 226, tile 0x162** (raw 0xE2 where the AP world writes raw ids). It is not in the AP item pool, can't be an enemy drop, and no item table uses it. Every such spot then spawns the normal **EntityEquipItemDrop**, which draws an icon from the item icon atlas and never gives gold. That covers (3).
- (1) Icon: write a 16x16 4bpp AP icon into **unused icon 319** and up to three AP palettes into **unused palettes 317-319** in DRA RAM. Add a **PreHook on `dra/LoadEquipIcon`** that swaps `(icon, palette) = (215, 215)` (the "----" icon) for `(319, AP palette)` when called from stage code. The game then uploads, caches, draws, blinks, z-sorts, restores after the menu, and handles widescreen as it does for any item. Works in every renderer.
- (2)+(3) Name and no item: add a **PreHook on `dra/AddToInventory`**. For `(a0=57, a1=2)` called from stage code, write "Hookshot (Maria)" in corner-text encoding to a RAM buffer, set **`c.S1`** to the buffer, and return **false** (don't add the item). In every stage overlay, `EntityEquipItemDrop` keeps the name pointer in `$s1` across the AddToInventory call and passes it to `BottomCornerText` right after. This was checked in all 50 stage/boss overlays.
- Both hooks are DRA functions reached through `g_api`, so there are no per-overlay hooks. The location being collected is found from `g_CurrentEntity` (0x8006C3B8): entity + 0xB4 holds the castle-flag index for item-table spots.

---

## 1. Section A: how a stage item-table pickup is spawned and drawn

### 1.1 Entity ids (every stage)
| id | entity | notes |
|---|---|---|
| 0x03 | EntityPrizeDrop | hearts, money bags, vessels, subweapons (sprite from the DRA item anim set) |
| 0x0A | EntityEquipItemDrop | equipment and usables (icon drawn as a GT4 prim) |
| 0x0B | EntityRelicOrb | relics (AP world "relic entity") |
| 0x0C | EntityHeartDrop | "item tile" layout entity: reads the stage item table and hands off to 0x03 or 0x0A (AP world "item entity") |

These match `patches/widescreen/HitDetectionWidescreenPatch.cs:59-61` (EPrizeDrop 0x03, EEquipItemDrop 0x0A) and the AP world's use of 0x0B/0x0C (`ap-world-notes.md` 3.3).

### 1.2 EntityHeartDrop: item-table dispatcher (`EntityHeartDrop_no0`, no0.cs 11987-12072, 0x801CAF9C)
Step 0:
- 12002: `entity+0xB4 (u16) = params + HEART_DROP_CASTLE_FLAG`. This is the castle-flag index; the offset is 0 in NO0 and 0xD0 in ARE, for example.
- 12005: reads flag byte `0x8003BEEC + (idx>>3)` (g_CastleFlags+0x100), bit `idx&7`. If the flag is set (already collected), DestroyEntity.
- 12021: `tile = u16[0x80180000 + 0x1100 + 2*params]` (NO0 item table 0x80181100, which matches LocationData).
- 12022-12034: `tile < 0x80`: `entity+0xB8 = EntityPrizeDrop_no0 (0x801C9220)`. Otherwise `entity+0xB8 = EntityEquipItemDrop_no0 (0x801C9C34)` and `tile -= 0x80`.
- 12036-12038: `params = tile + 0x8000` (bit 15 = persistent, never times out).

Every frame, 12041-12062: if `1 <= step < 5 && hitFlags (+0x48)`, set the castle flag (collected) and `step = 5`. Then 12064-12067 call `*(entity+0xB8)(self)`.
- The same offsets (+0xB4 flag index, +0xB8 delegate) are in ARE, RNO0, BO4 and RCEN (checked), so they hold everywhere.
- **Hook data**: for an item-table pickup, `u16(entity+0xB4)` is the castle-flag index. Location = the Detect.Loot entry with `Addresses[] = 0x8003BEEC + (idx>>3)`, `Bit = idx&7`, and the current stage in `Stages`. Example: NO0 loc 71 is flag 0 (0x8003BEEC bit 0); ARE loc 1 is 0x8003BF06 bit 0 = flag 208. Relic spots turned into items (layout id 0x0C, state = index) take the same path.

### 1.3 EntityPrizeDrop: money bags (`EntityPrizeDrop_no0`, no0.cs 9684-~10416, 0x801C9220)
- `itemId = params & 0x7FFF` (9691-9694). Prize index: 0/1 hearts, **2-11 gold ($1,$25,$50,$100,$250,$400,$700,$1000,$2000,$5000)**, 12 heart vessel, 13 "dummy", 14-22 subweapons, 23 life vessel (decomp `src/st/e_collect.h`).
- Sprite: 9698-9704, `AnimateEntity(g_SubweaponAnimPrizeDrop[itemId], self)` (anim table at 0x80181A10 in NO0, AnimateEntity = 0x801C7930). Frames come from `g_EInitObtainable` = ANIMSET_DRA(3) (InitializeEntity(0x80180A70) at 9765-9768). This is DRA's shared item sprite bank, so bag sprites are shared by every bag. There is no per-entity way to change a bag into another picture.
- Palette: 0, flashing to 0x815F while `unk6D >= 0x18` (9720-9741).
- Step 5 dispatch (10004-10057): `<2` CollectHeart (10013), `<12` **CollectGold (10022)**, `==12` CollectHeartVessel (10031), `<14` **CollectDummy (10040: just DestroyEntity)**, `<23` CollectSubweapon (10049), `==23` CollectLifeVessel (10056).

### 1.4 EntityEquipItemDrop: equipment (`EntityEquipItemDrop_no0`, no0.cs 10467-10919, 0x801C9C34)
- Step 0 (10516-10541): if `g_PlayableCharacter (0x8003C9A0) != 0` it turns into a PrizeDrop heart. Otherwise InitializeEntity(0x80180A70) and `ext+0x7C (timer) = 0`.
- Step 1 (10543-10702):
  - CheckCollision.
  - Finds a free icon slot in `g_ItemIconSlots[32]` (NO0 BSS 0x801DF54C) (10558-10581).
  - Sets the rare-drop castle flag if `(s16)entity+0x94 != 0` (10586-10604).
  - `AllocPrimitives(GT4,1)` (10606-10613). `slots[i] = 0x1E0`, `entity+0x8C = i` (10630-10640).
  - Reads icon and palette: hand item `equipDefs[id]` (stride 0x34): icon +0x2C, palette +0x2E (10641-10650). Accessory `accessoryDefs[id-169]` (stride 0x20): icon +0x18, palette +0x1A (10653-10660).
  - `g_api.LoadEquipIcon(icon, pal, slot)` through 0x8003C82C (10662-10666).
  - Prim (10667-10701):
    - `tpage 0x1A`, `clut 0x1D0+slot`
    - `u0 = (slot&7)*16+1`, `u1 = u0+14`, `v0 = (slot&0x18)*2+0x81`, `v1 = v0+14`
    - `priority 0x80`, `drawMode 6`
    - So only texels 1..14 of the 16x16 tile are drawn.
- Steps 2-4: fall, rest, blink-out. Without 0x8000 in params it times out after 240 + 80 frames (10772-10833). `BlinkItem` positions and flashes the prim each frame (10892-10908).
- **Step 5 = collect (label L801CA0F4, 10834-10890)**:
  1. If `BottomCornerTextTimer (0x80097410) != 0`: `g_api.FreePrimitives(*(0x80097414))`, then timer = 0 (10835-10847).
  2. `g_api.PlaySfx(0x67C)` (10849-10853).
  3. Def pointer: `equipDefs + id*0x34, a1=0` or `accessoryDefs + (id-169)*0x20, a1=2` (10854-10877).
  4. **`c.S1 = *(def+0)` = name pointer, loaded in the delay slot just before `g_api.AddToInventory(a0=id, a1=kind)` (0x8003C84C) (10878-10882).**
  5. `BottomCornerText_no0(S1, 1)` (10883-10886).
  6. DestroyEntity (10888-10890).
- Holy glasses (CEN) goes through the same step 5. RandoPatch's EntityPlatform (`patches/rando/RandoPatch.cs:3130-3145`) creates entity 0x0A at 0x8007C9A8 with `params = u16[0x8018FEA0]` and `step = 5`, so it is collected at once.
- Boss drops: e.g. `BO1_EntityLifeUpSpawn` (bo1.cs 19957-, tile split 20532-20575). `tile < 0x80` gives PrizeDrop; otherwise entity 0x0A (EquipItemDrop 0x801AD188) with `params = (tile-0x80) | 0x8000`. There is **no castle-flag field**; the boss overlay itself identifies the location (one boss drop per boss overlay).

### 1.5 LoadEquipIcon (DRA, `dra.cs` 11797-11914, 0x800EB534; g_api slot 0x8003C82C)
`LoadEquipIcon(a0=icon, a1=palette, a2=slot)`:
- Pixel cache: `u16 0x801374F8[slot]`. If it differs from icon, upload with `LoadTPage(0x800C5324 + icon*0x80, 4bpp, x = 0x280 + (slot&7)*4, y = 0x180 + (slot>>3)*16, 16, 16)` (11808-11840). **g_GfxEquipIcon = 0x800C5324: 320 icons of 16x16 4bpp = 128 bytes each (low nibble = left pixel).**
- Palette cache: `u16 0x80137538[slot]`. If it differs, copy 16 colours from **g_PalEquipIcon 0x800D88D4 + pal*0x20** (320 palettes, BGR555, colour 0 = transparent) into `g_Clut (0x8006CBCC) + (0x1D0+slot)*0x20` = 0x800705CC+slot*0x20. Then `LoadClut(0x800705CC, 0, 0xFD)` and `LoadClut(0x800707CC, 0, 0xFE)`: slots 0-15 go to VRAM CLUT row 253, slots 16-31 to row 254 (11842-11884). Palette id `0x1D0+slot` maps to the GPU CLUT through `g_ClutIds` (0x8003C104).
- If `u32 0x800973EC == 0`, it also records `0x80137478[slot]=icon` and `0x801374B8[slot]=pal` (11886-11898). These are the "saved" copies; `func_800EB6B4` (dra.cs 11916-) reloads all 32 slots from them, for restoring after the menu has borrowed the slots. It then updates the live caches (11901-11906).
- Only LoadEquipIcon references the two tables in DRA (grep for `0x5324`/`0x772C` in dra.cs: lines 11819 and 11856 only). The recomp's patched `RandoPatch.MenuHandle` also calls it, for slot 0x1F with RA 0x800FCBC4.

### 1.6 Per-overlay copies (method name @ address)
All stage and boss overlays have their own EntityHeartDrop / EntityEquipItemDrop / EntityPrizeDrop / CollectGold / BottomCornerText. Names follow two conventions, `Foo_no0` or `RNO0_Foo`. A few are unnamed (func_XXXXXXXX): rnz1 HeartDrop and EquipItemDrop, rdai HeartDrop and CollectGold, rbo2/rbo4 HeartDrop, rno0 PrizeDrop, bo1/bo5 PrizeDrop, rbo7 CollectGold. Examples:

| ovl | HeartDrop | EquipItemDrop | PrizeDrop | CollectGold | BottomCornerText |
|---|---|---|---|---|---|
| no0 | EntityHeartDrop_no0 @801CAF9C | EntityEquipItemDrop_no0 @801C9C34 | EntityPrizeDrop_no0 @801C9220 | CollectGold_no0 @801C8F10 | BottomCornerText_no0 @801C6C8C |
| are | _are @801BF288 | _are @801BDF20 | _are @801BD50C | _are @801BD1FC | _are @801C342C |
| rno0 | RNO0_EntityHeartDrop @801BDED8 | RNO0_EntityEquipItemDrop @801BCB70 | (unnamed) | RNO0_CollectGold @801BBE4C | RNO0_BottomCornerText @801C6120 |
| bo1 | BO1_EntityHeartDrop @801AE4F0 | BO1_EntityEquipItemDrop @801AD188 | (unnamed) | BO1_CollectGold @801AC464 | BO1_BottomCornerText @801B02EC |

(The full table for all ~50 overlays was produced during research; rerun the grep in section 9 if needed.) This is why per-overlay hooks are the wrong tool here: the DRA functions reached through `g_api` are shared by all of them.

---

## 2. Section B: where gold is added

`CollectGold_no0(a0 = prize index)`, no0.cs 9447-9505 (0x801C8F10), one copy per overlay:
1. `g_api.PlaySfx(0x6A9)` (9453-9459).
2. **`g_Status.gold (0x80097BF0) += c_GoldPrizes[idx-2]`** (table 0x801819E8 in NO0), clamped to 999999 = 0xF423F (9460-9474).
3. Free the old corner-text prims (9476-9490).
4. **`BottomCornerText(g_goldCollectTexts[idx-2], 1)`** (pointer table 0x801819C0 in NO0: "$1".."$5000") (9492-9497).
5. `DestroyEntity(g_CurrentEntity)` (9498-9500).

Knowing which slot or entity is being collected, from inside any hook during the pickup:
- `g_CurrentEntity` = **0x8006C3B8** (not 0x800733B8) is set to the entity just before its update by `UpdateStageEntities_no0` (no0.cs 2779). The widescreen replacement loop does the same (`patches/widescreen/UpdateWidescreenPatch.cs:147`).
- Item-table pickup: `u16(e+0x26) == 0x0C`, `u16(e+0xB4)` = flag index, then location as in 1.2. `u16(e+0x30) & 0x7FFF` = prize index or item id, `u16(e+0x2C)` = step (5 while collecting).

If money bags were kept, the options would be:
- a pre/post pair on every overlay's CollectGold: save the gold in the pre, write it back in the post, and point `g_goldCollectTexts[idx-2]` at our text for the duration;
- or place prize **13** ("CollectDummy": no gold, no text, just destroy; sprite frame 0x13).

Both are per-overlay and still look like bags, so they are not recommended. With the placeholder-item approach, CollectGold is never reached.

---

## 3. Section C: the pickup name popup

- Items and gold both use the stage's **BottomCornerText(str, leftAlign=1)** (NO0 no0.cs 6697-6940, 0x801C6C8C). It is a **bottom-left** box, not the top of the screen:
  - box x = 7 .. 7+width+0x24, y = 0xD0-0xDF (6799-6818); glyphs drawn at y 0xD4;
  - the timer is `0x80097410 = 0x130` frames (6933), prims at 0x80097414.
- The centred "Obtained <relic>" box is **EntityRelicOrb** (relics only). It renders ASCII/SJIS text through BlitChar and the 12x16 kanji font into a texture. Not needed here.
- Text source for items: `*(def+0)` = name pointer from equipDefs/accessoryDefs (1.4 step 5). For gold: the overlay's `g_goldCollectTexts[]`.
- **Encoding** (US corner text), confirmed from item names in DRA and from the copy loop at 6708-6743:
  - one byte per glyph = `ASCII - 0x20` ('A'=0x21, 'a'=0x41, '1'=0x11, '-'=0x0D, '.'=0x0E, '\''=0x07, '$'=0x04);
  - **space = 0x00** (drawn 4 px wide, other glyphs 8 px);
  - **terminator = 0xFF 0x00**. A 0xFF followed by a non-zero byte is skipped, not a terminator.
  - Glyph = 8x8 cell at tpage 0x1E, `u=(ch&0xF)*8`, `v=(ch&0xF0)>>1`.
- **Limits**:
  - BottomCornerText copies into a **64-byte stack buffer with no bounds check** (6708-6743). More than 64 glyphs overwrites saved S0/S1/RA. Cap at ~40.
  - At 256-px width the box fits about **26 glyphs**.
  - `wrapers/Text.cs Write()` uses the same `c-32` mapping but writes a single 0xFF terminator. Don't use it for this buffer; write `FF 00` yourself.
  - Non-ASCII characters in player/item names must be folded to ASCII or '?'.
  - '(' / ')' (0x08/0x09) are not used by any vanilla item name, so their glyphs are unverified. Check in game; fall back to `"Hookshot - Maria"` or `"Maria's Hookshot"`, since '-' and '\'' are proven by vanilla names.
- **Can a hook substitute arbitrary text? Yes.** In every stage overlay, EquipItemDrop step 5 loads the name into **$s1** before calling `g_api.AddToInventory`, and passes `$s1` to BottomCornerText after it returns. A grep of all overlays for the `g_api+0xD8` (0x37B4) call site followed by `c.S1 = m.ReadU32(c.V0)` found **1/1 in every stage/boss overlay** (are, bo0-7, cat, cen, chi, dai, dre, lib, mar, no0-4, np3, nz0, nz1, rare, rbo0-8, rcat, rcen, rchi, rdai, rlib, rno0-4, rnz0, rnz1, rtop, rwrp, st0, top, wrp; lib and rbo0 have a second, unrelated call site). So a PreHook on `dra/AddToInventory` can set `c.S1` to our buffer.
  - Register-independent alternative: a PreHook on `dra/PlaySfx` with `a0 == 0x67C`. It runs before the name is loaded; swap `accessoryDefs[57].name` to the buffer there, then restore it in the AddToInventory hook.
- RAM for the buffer: low RAM is free under the HLE BIOS. RandoPatch uses 0x8000C000-0x8000C004 and 0x8000D000-0x8000D08F (`patches/rando/RandoPatch.cs:27-34`). Suggest **0x8000E000** (64 bytes).
- Extra: the mod can show its own in-game corner text (e.g. "Received X from Y"). Find the current stage's BottomCornerText the way `WidescreenPatch.StageSym`/`CallStageRet` do (`patches/widescreen/ScreenBounds.cs:27-99`, `Dispatcher.Overlays` + `ActiveNames`), free the old prims first (`HitDetectionWidescreenPatch.cs:362-367`), and only call it during normal play.

---

## 4. Section D: icon storage and the options for an AP icon

### 4.1 Storage (DRA, resident)
- equipDefs = **0x800A4B04** (169 x 0x34), accessoryDefs = **0x800A7718** (90 x 0x20), relicDefs = **0x800A8720** (30 x 0x10: name +0, desc +4, icon +8, pal +0xA). These are the values in g_api 0x8003C830/0x8003C834/0x8003C850, confirmed from the DRA image at 0x800A00BC.
- Icons: 0x800C5324 + i*0x80, i < 320. Palettes: 0x800D88D4 + p*0x20, p < 320.
- Usage from the disc:
  - item/relic defs use icons up to 272 and palettes up to 279;
  - **icons 280-319 are all-zero (unused)**;
  - palettes 280-319 hold unused data;
  - also unused: icons 0,2,3,137-141,156-158,174-177,181-183,210-214,231-236,255,273-319.
- Placeholder "----" accessory 57 (id 226) uses icon 215 / palette 215, shared only with accessory 48 (also "----"). Secret boots (acc 88) uses icon 254 / palette 254 alone.
- Upload: LoadEquipIcon → LoadTPage/LoadClut → HLE LoadImage (`RecompOne.Runtime/sdk/LibGpu.cs:157`) → GPU/VRAM.

### 4.2 Option (i): our own icon in unused RAM slots, chosen by a LoadEquipIcon hook (recommended)
- Write 128 bytes of AP pixels at **0x800CF2A4** (icon 319).
- Write 16 colours each at **0x800DB074 / 0x800DB094 / 0x800DB0B4** (palettes 317/318/319: progression / useful / filler). Use `WriteU8`/`WriteU16`.
- Draw inside the 14x14 centre (texels 1..14); index 0 = transparent.
- Re-write on every stage load (the existing `SeedPlan.OnStageLoad` pre-hook) in case DRA is reloaded.
- `[PreHook("dra","LoadEquipIcon")]`: if `c.RA` is in 0x80180000-0x801FFFFF (stage or boss code; the menu and RandoPatch call from DRA RA) and `(c.A0, c.A1) == (215, 215)`, set `c.A0 = 319`, `c.A1 = 317 + class`.
- The cache, the saved copies used to restore after the menu, blinking, the despawn blink, z-order and widescreen all behave normally. Works with the software rasterizer too.

### 4.3 Option (ii): the recomp texture replacement system
- It is **hash based**: `TextureResolver.Resolve` (`Assets/Textures/TextureResolver.cs:133`) is called per textured prim by the GL backend (`Gpu/Backends/Common/GlCore.cs:330`). It hashes the VRAM tile (FNV-1a of the 4bpp indices plus the CLUT, `TextureTile.Hash`) and looks it up in pack tables (`AssetReplacerManager.ResolveTexture`, `AssetReplacerManager.cs:404`: exact (index, clut) or index-only).
- The only mod API (`Assets/AssetApi.cs`) covers XA audio registration and `ReloadPacks()`. There is **no runtime texture registration and no per-draw control**, and it is HLE (GL) only.
- Useful as an **add-on to (i)**: ship a pack entry keyed on the hash of our 4bpp AP icon (get it with the texture dumper). HLE users then see an HD, full-colour logo. It can't choose per entity, but it doesn't need to, because (i) already gives the AP pickups unique pixels.

### 4.4 Option (iii): ImGui overlay at the entity's position (not recommended)
- The game image is drawn by the **internal** `OutputPanel` (`Host/Window/Panels/Debug/OutputPanel.cs:6`, `ImGui.Image` at line 43, aspect-fitted in a dockable window). Its rect is not exposed.
- Nothing in the recomp maps game coordinates to window coordinates: `MapOverlayPanel` draws its own map in its own window.
- A mod would have to guess the Output window rect, then map `window = imgMin + (posX + Margin, posY) * imgSize / (256 + 2*Margin, 240)` (entity `posX.hi`/`posY.hi` at +0x02/+0x06 are screen-relative; `WidescreenPatch.Margin`).
- Problems: it draws over menus, fades and foreground; it lags a frame; there is no z-order; it breaks when the window is undocked or resized.

### 4.5 Option (iv), found during research: custom RGBA sprite inside the game's ordering table
- `GpuPrims.RegisterImage(rgba,w,h)` (`Gpu/Hle/GpuPrims.cs:70`), `GpuPrims.SetOrderingTable(ReadU32(0x8006C37C)+0x474, 0x200)`, and `GpuPrims.Sprite(order, image, x, y, w, h)` insert full-colour quads into the OT. `LibGpu.DrawOTag` emits them at that OT index (`sdk/LibGpu.cs:13-38`).
- Working example: `patches/extension/RoomFill.cs:146-157, 296`: `x = ReadU32(0x8006C39C) + screenX`, called from `TilemapRenderedEvent`.
- HLE only (`Gpu/GpuCustom.cs:20`). HLE is on whenever the GL backend is ready (`Host/Window/HostWindow.cs:360`).
- It gives z-sorted, widescreen-correct, full-colour art. But the mod must re-submit it every frame, hide the vanilla icon prim, copy the blink and despawn behaviour, and skip it when paused. Worth it only if the 16-colour icon from (i) isn't enough.

---

## 5. Section E: recommendation

### 5.1 Placement change (Placement.cs `Remote`)
Replace the bags and boots with one placeholder:
- tile **0x162** (226 + 0x80): item-table spots, relic spots turned into items, Gold ring, boss drops, CHI turkey (loc 40);
- raw **0xE2**: walls (`NoOffset`) and Holy glasses.

Everything then spawns EntityEquipItemDrop, which never calls CollectGold, so (3) is covered for gold. Keep the money bags behind a setting as the fallback (PLAN.md section 5). `tools/verify_placement.py` compares byte for byte with the AP patch, so remote placeholder writes need to be excluded or mapped back (run Placement in "bags" mode for the check).

### 5.2 Hooks (all DRA, one each)
```csharp
const uint CurEntity = 0x8006C3B8, AccDefs = 0x800A7718, TextBuf = 0x8000E000;
const uint PhAcc = 57; const ushort PhIcon = 215, PhPal = 215;   // "----", item 226
const int ApIcon = 319; // palettes 317 prog, 318 useful, 319 filler
static bool FromStage(CpuContext c) => c.RA >= 0x80180000 && c.RA < 0x80200000;

[PreHook("dra", "LoadEquipIcon")]      // (1)
static void Icon(CpuContext c, IMemory m) {
    if (!FromStage(c) || c.A0 != PhIcon || c.A1 != PhPal) return;
    var scout = ApLook.Resolve(m, m.ReadU32(CurEntity));   // may be null -> default palette
    c.A0 = ApIcon; c.A1 = (uint)(317 + Class(scout));
}

[PreHook("dra", "AddToInventory")]     // (2) + no placeholder item
static bool Collect(CpuContext c, IMemory m) {
    if (!FromStage(c) || c.A0 != PhAcc || c.A1 != 2) return true;
    if (c.S1 == m.ReadU32(AccDefs + PhAcc * 0x20)) {       // name pointer still in $s1
        WriteCornerText(m, TextBuf, Describe(ApLook.Resolve(m, m.ReadU32(CurEntity)))); // bytes c-0x20, ' '->0, FF 00, <=26 glyphs
        c.S1 = TextBuf;
    }
    return false;                                           // never add "----"
}
```
- `ApLook.Resolve(e)`:
  - `u16(e+0x26) == 0x0C` → `flag = u16(e+0xB4)` → Loot location for `(stage, flag)`. Build that map once in SeedPlan from `LocationInfo.Addresses/Bit/Stages`: `flag = (addr - 0x8003BEEC)*8 + bit`.
  - `== 0x0A` (direct drop: boss, wall, Holy glasses, Gold ring, turkey) → the remote non-table location(s) placed in the current stage. If there are several, prefer the one whose Break/KillTime flag is set and hasn't been shown yet. Otherwise use a generic "AP item".
- Text: `ItemName(scout.Item, scout.Player) + " (" + PlayerName(scout.Player) + ")"`, trimmed to 26 glyphs (shorten the item name first). Optionally also send a toast with the full text.
- Icon data: write it once at load and on each stage load (SeedPlan's existing stage-load pre-hook), before any pickup can load it.

### 5.3 Risks and checks
1. **$s1 assumption**: verified in all stage/boss overlays. The `c.S1 == name ptr` guard means a mismatch only shows "----" and never corrupts anything. The PlaySfx(0x67C) pointer-swap variant is the fallback.
2. **g_CurrentEntity** is valid only during entity updates. Both hooks also require stage RA plus the placeholder arguments. ItemGiver's own `GameApi.AddToInventory` calls (RA arbitrary) never use (57, 2), because id 226 isn't in ItemData.
3. **Unused icon/palette slots**: only LoadEquipIcon reads the tables. If the icon pixels are changed after being uploaded to a slot, the slot cache (0x801374F8) keeps the old ones until the slot is reused. Write the data before gameplay, or clear the 32 cache entries to 0xFFFF after rewriting.
4. **Text**: 64-byte stack buffer (hard cap); ~26 visible glyphs; parentheses glyph unverified; fold non-ASCII.
5. **Direct drops** (walls, bosses, glasses, ring, turkey) carry no flag, so the location comes from the stage. It may be ambiguous if several remote walls in one stage are broken and left lying. Fallback text only; the icon is unaffected.
6. **Placeholder choice**: "----" is never in the pool, never an enemy drop (matters with `randomize_drop` full), and never in a stage table, so the checks by arguments alone are unambiguous. Secret boots would clash with real Secret boots and random enemy drops.
7. Non-Alucard characters: EquipItemDrop step 0 turns into a heart (vanilla behaviour; AP is Alucard only).
8. Other hooks: HookManager allows several pre-hooks per function; only `Replace` conflicts. `config/sotn.json` has no build-time patch on LoadEquipIcon, AddToInventory, PlaySfx, EquipItemDrop, HeartDrop or BottomCornerText.
9. Remote items at relic spots whose vanilla item is Jewel of Open or a Vlad relic, and the Librarian slot, stay unsupported (unchanged).
10. Optional polish: an HD texture-pack entry keyed on the AP icon hash (4.3), or the GpuPrims sprite (4.5).

---

## 6. Key addresses

| what | address |
|---|---|
| g_CurrentEntity | 0x8006C3B8 |
| g_Entities | 0x800733D8 (stride 0xBC) |
| entity: step / params / hitFlags / id / prim | +0x2C / +0x30 / +0x48 / +0x26 / +0x64 |
| HeartDrop flag index / delegate | +0xB4 (u16) / +0xB8 (u32) |
| EquipItemDrop timer / aliveTimer / iconSlot / rare flag | +0x7C / +0x80 (u8) / +0x8C / +0x94 |
| g_CastleFlags / pickup flags base | 0x8003BDEC / 0x8003BEEC |
| g_Status.gold | 0x80097BF0 |
| corner text timer / prims | 0x80097410 / 0x80097414 |
| g_api LoadEquipIcon / equipDefs / accessoryDefs / AddToInventory / relicDefs | 0x8003C82C / 0x8003C830 / 0x8003C834 / 0x8003C84C / 0x8003C850 |
| DRA LoadEquipIcon / AddToInventory / PlaySfx | 0x800EB534 (dra.cs 11797) / 0x800FD874 (32597) / 0x801347F8 (102054) |
| equipDefs / accessoryDefs / relicDefs | 0x800A4B04 / 0x800A7718 / 0x800A8720 |
| g_GfxEquipIcon / g_PalEquipIcon | 0x800C5324 (320 x 0x80) / 0x800D88D4 (320 x 0x20) |
| icon slot caches: live icon / live pal / saved icon / saved pal | 0x801374F8 / 0x80137538 / 0x80137478 / 0x801374B8 |
| g_Clut / icon-slot palettes | 0x8006CBCC / 0x800705CC (+slot*0x20), VRAM CLUT rows 0xFD/0xFE |
| icon-slot VRAM | x 0x280+(slot&7)*4, y 0x180+(slot>>3)*16 (tpage 0x1A) |
| PlaySfx ids | item pickup 0x67C, gold 0x6A9 |
| proposed AP data | icon 319 @0x800CF2A4, palettes 317-319 @0x800DB074/094/0B4, text buffer 0x8000E000 |

## 7. Not done / open
- The 8x8 font sheet wasn't located on the disc (not in DRA; F_GAME.BIN layout unclear), so glyph coverage beyond the characters used in vanilla names is unverified.
- The icon art itself (16-colour, 14x14 visible) still needs to be drawn.
- Direct-drop location resolution for stages with several remote walls needs an in-game test (NZ1/RNZ1 wall tables, NO3 rocks).

## 8. Hook attribute reminder
`[PreHook("dra", "LoadEquipIcon")]` and `[PreHook("dra", "AddToInventory")]`: DRA method names have no suffix. A pre-hook returning `false` skips the original; post-hooks still run (`RecompOne.Runtime/Modding/HookManager.cs:157-171`). Names are resolved by `SymbolRegistry.Resolve` on the exact C# method name. Per-overlay names (if ever needed) are `Foo_no0` or `RNO0_Foo`; use `Address = 0x...` for unnamed ones.

## 9. Grep used for the per-overlay table
```
cd ref/SymphonyRecomp/generated
for f in *.cs; do for n in EntityHeartDrop EntityEquipItemDrop EntityPrizeDrop CollectGold BottomCornerText; do
  grep -oE "public static void ([A-Z0-9]+_)?${n}(_[a-z0-9]+)?\(" $f | head -1; done; done
# $s1 check:
grep -A5 "0x37B4u" <ovl>.cs | grep "c.S1 = m.ReadU32(c.V0);"
```
