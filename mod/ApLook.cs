#nullable enable
using System.Collections.Generic;
using System.Text;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;
using Sotn;

namespace SotnArchipelago;

// Other players' items in the castle: the "----" placeholder (Placement.PlaceholderItem) drawn with
// an Archipelago icon coloured by the item's classification, named "<player>'s <item>" when picked
// up, and never added to the inventory (so no gold and no junk). Research notes: ref/ap-look-notes.md.
static class ApLook
{
    const uint CurrentEntityAddr = 0x8006C3B8;   // g_CurrentEntity, set before each entity update
    const uint AccessoryDefsPtr = 0x8003C834;    // g_api.accessoryDefs
    const uint IconsAddr = 0x800C5324;           // g_GfxEquipIcon: 320 icons, 16x16 4bpp (0x80 bytes each)
    const uint PalettesAddr = 0x800D88D4;        // g_PalEquipIcon: 320 palettes of 16 BGR555 colours
    const uint TextBuffer = 0x8000E000;          // free low RAM (RandoPatch uses 0x8000C000/D000)
    const uint PickupFlagsBase = 0x8003BEEC;     // g_CastleFlags + 0x100: stage pickup flags

    const int PlaceholderAccessory = 57;         // body item index of "----" (item 226)
    const int AccessoryKind = 2;                 // AddToInventory kind EquipItemDrop passes for accessories
    const uint PlaceholderIcon = 215, PlaceholderPalette = 215;
    const int ApIcon = 319;                      // icons 280-319 are blank on the disc
    const int TrapPalette = 316, ProgressionPalette = 317, UsefulPalette = 318, FillerPalette = 319;

    const ushort EntityHeartDrop = 0x0C;         // item-table pickup (flag index at +0xB4)
    const int ReverseClockTower = 0x2D;
    const uint ReverseClockTowerHeartDrop = 0x801B3A54;
    const int MaxTextGlyphs = 26;                // what fits in the bottom-left box

    static bool Active => SeedPlan.Ready;

    static bool FromStage(CpuContext c) => c.RA >= 0x80180000 && c.RA < 0x80200000;

    // ---- icon and palettes ----

    // LoadEquipIcon keeps, per icon slot (32), the icon and palette ids it last uploaded, and skips
    // the upload when they match. 0xFFFF matches nothing, so the next load re-uploads.
    const uint LiveIconIds = 0x801374F8, LivePaletteIds = 0x80137538;
    const int IconSlots = 32;

    // Written on every stage load (DRA data, read only by LoadEquipIcon), before any pickup loads it.
    // If the data in RAM was different (first time, or a mod update changed the art), the slot caches
    // are cleared so pickups already cached with the old art get the new one.
    public static void WriteArt(IMemory m)
    {
        bool changed = false;
        uint icon = IconsAddr + ApIcon * 0x80u;
        var pixels = IconPixels();
        for (int i = 0; i < pixels.Length; i++) changed |= Set8(m, icon + (uint)i, pixels[i]);

        // Loot-style colours: grey filler, blue useful, purple progression, red trap (ItemClass).
        WritePalette(m, TrapPalette, 0xE0, 0x50, 0x48);
        WritePalette(m, ProgressionPalette, 0xAF, 0x99, 0xEF);
        WritePalette(m, UsefulPalette, 0x6D, 0x8B, 0xE8);
        WritePalette(m, FillerPalette, 0x8C, 0x8C, 0x8C);

        if (!changed && !_palettesChanged) return;
        _palettesChanged = false;
        for (int slot = 0; slot < IconSlots; slot++)
        {
            m.WriteU16(LiveIconIds + (uint)slot * 2, 0xFFFF);
            m.WriteU16(LivePaletteIds + (uint)slot * 2, 0xFFFF);
        }
    }

    static bool _palettesChanged;

    static bool Set8(IMemory m, uint addr, byte value)
    {
        if (m.ReadU8(addr) == value) return false;
        m.WriteU8(addr, value);
        return true;
    }

    // 16x16, index per pixel: a round badge with "AP" on it. Only texels 1..14 are drawn.
    // . transparent  o outline  b class colour  h highlight  s shadow  W letters  d letter shadow
    static readonly string[] Art =
    [
        "................",
        "......oooo......",
        "....oohhbboo....",
        "...ohhhbbbbbo...",
        "..ohhhbbbbbbbo..",
        "..ohWWbbbWWWbo..",
        ".ohWbdWbbWddWbo.",
        ".ohWWWWdbWWWbdo.",
        ".obWddWdbWdddso.",
        ".obWdbWdbWdbsso.",
        "..obdbbdbbdsso..",
        "..obbbbbbbssso..",
        "...obbbbbssso...",
        "....oobbssoo....",
        "......oooo......",
        "................",
    ];

    static byte[] IconPixels()
    {
        var bytes = new byte[0x80];
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x += 2)
                bytes[y * 8 + x / 2] = (byte)(Index(Art[y][x]) | Index(Art[y][x + 1]) << 4);
        return bytes;
    }

    static int Index(char c) => c switch
    {
        'o' => 1, 'b' => 2, 'h' => 3, 's' => 4, 'W' => 5, 'd' => 6, _ => 0,
    };

    static void WritePalette(IMemory m, int palette, int r, int g, int b)
    {
        uint at = PalettesAddr + (uint)palette * 0x20;
        ushort[] colours =
        [
            0x0000,                                  // transparent
            Bgr(0x10, 0x10, 0x20),                   // outline
            Bgr(r, g, b),                            // class colour
            Bgr(Mix(r, 255, 45), Mix(g, 255, 45), Mix(b, 255, 45)),
            Bgr(Mix(r, 0, 40), Mix(g, 0, 40), Mix(b, 0, 40)),
            Bgr(0xFF, 0xFF, 0xFF),                   // letters
            Bgr(0x18, 0x18, 0x30),                   // letter shadow
        ];
        for (int i = 0; i < 16; i++)
        {
            ushort value = i < colours.Length ? colours[i] : (ushort)0;
            if (m.ReadU16(at + (uint)i * 2) == value) continue;
            m.WriteU16(at + (uint)i * 2, value);
            _palettesChanged = true;
        }
    }

    static int Mix(int from, int to, int percent) => from + (to - from) * percent / 100;

    // PSX 15-bit colour, blue high. Bit 15 set keeps pure black opaque (0x0000 is transparent).
    static ushort Bgr(int r, int g, int b) => (ushort)(0x8000 | (b >> 3) << 10 | (g >> 3) << 5 | r >> 3);

    static int PaletteFor(NetworkItem? item)
    {
        if (item is not { } i) return FillerPalette;
        if (i.Progression) return ProgressionPalette;
        if (i.Trap) return TrapPalette;
        return i.Useful ? UsefulPalette : FillerPalette;
    }

    // ---- hooks ----

    [PreHook("dra", "LoadEquipIcon")]
    static void OnLoadEquipIcon(CpuContext c, IMemory m)
    {
        if (!Active || !FromStage(c) || c.A0 != PlaceholderIcon || c.A1 != PlaceholderPalette) return;
        var item = Resolve(m, m.ReadU32(CurrentEntityAddr), remember: false);
        c.A0 = ApIcon;
        c.A1 = (uint)PaletteFor(item);
    }

    [PreHook("dra", "AddToInventory")]
    static bool OnAddToInventory(CpuContext c, IMemory m)
    {
        if (!Active || !FromStage(c)) return true;
        long item = c.A1 == 0 ? c.A0 : c.A0 + 169;
        SpecialSpots.OnPickup(m, item);
        if (c.A0 != PlaceholderAccessory || c.A1 != AccessoryKind) return true;

        // EquipItemDrop has the item's name in $s1 and shows it after this returns.
        var scout = Resolve(m, m.ReadU32(CurrentEntityAddr), remember: true);
        uint nameAddr = m.ReadU32(m.ReadU32(AccessoryDefsPtr) + PlaceholderAccessory * 0x20u);
        if (c.S1 == nameAddr)
        {
            WriteCornerText(m, TextBuffer, Describe(scout));
            c.S1 = TextBuffer;
        }
        return false; // never add "----" to the inventory
    }

    // ---- which location a pickup is ----

    static readonly HashSet<long> _shownDirect = [];

    public static void Reset() => _shownDirect.Clear();

    // The scouted item for the pickup entity: item-table pickups carry their flag index; other drops
    // (walls, boss drops, Holy glasses) are matched to this stage's direct-drop spots.
    static NetworkItem? Resolve(IMemory m, uint entity, bool remember)
    {
        if (entity == 0) return null;
        int stage = m.ReadU8(Game.StageIdAddr);
        // Item-table pickups carry their flag index at +0xB4. So does Darkwing Bat's drop, which the
        // recomp's reward orb runs as an item-table pickup under entity id 0x0A (see SpecialSpots).
        bool itemTable = m.ReadU16(entity + 0x26) == EntityHeartDrop
            || stage == ReverseClockTower && m.ReadU32(entity + 0x28) == ReverseClockTowerHeartDrop;
        if (itemTable)
        {
            int flag = m.ReadU16(entity + 0xB4);
            return SeedPlan.LocationForPickupFlag(flag) is { } loc && ApClient.TryGetScout(loc, out var s) ? s : null;
        }

        var candidates = SeedPlan.DirectDropsIn(stage);
        long? best = null;
        foreach (var id in candidates)
        {
            if (_shownDirect.Contains(id)) continue;
            var loc = LocationData.Get(id);
            if (loc != null && CheckTracker.IsCollectedAsVanilla(m, loc)) { best = id; break; }
            best ??= id;
        }
        if (best is not { } chosen) return null;
        if (remember) _shownDirect.Add(chosen);
        return ApClient.TryGetScout(chosen, out var scout) ? scout : null;
    }

    public static int PickupFlagIndex(uint flagByte, int bit) => (int)(flagByte - PickupFlagsBase) * 8 + bit;

    // ---- name text ----

    static string Describe(NetworkItem? scout)
    {
        if (scout is not { } s) return "Archipelago item";
        string player = Clean(ApClient.PlayerName(s.Player));
        string item = Clean(ApClient.ItemName(s.Item, s.Player));
        string text = $"{player}'s {item}";
        if (text.Length <= MaxTextGlyphs) return text;
        // Keep the item name whole where possible; shorten the player name first.
        int room = MaxTextGlyphs - item.Length - 3;
        if (room >= 3) return $"{player[..System.Math.Min(player.Length, room)]}'s {item}";
        return item.Length <= MaxTextGlyphs ? item : item[..MaxTextGlyphs];
    }

    // Glyphs proven by vanilla item names; anything else becomes a space.
    static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
            sb.Append(ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or ' ' or '-' or '.' or '\'' ? ch : ' ');
        return sb.ToString().Trim();
    }

    // Bottom corner text: one byte per glyph = ASCII - 0x20 (space = 0), ended by FF 00.
    static void WriteCornerText(IMemory m, uint at, string text)
    {
        int n = System.Math.Min(text.Length, MaxTextGlyphs);
        for (int i = 0; i < n; i++) m.WriteU8(at + (uint)i, (byte)(text[i] - 0x20));
        m.WriteU8(at + (uint)n, 0xFF);
        m.WriteU8(at + (uint)n + 1, 0x00);
    }
}
