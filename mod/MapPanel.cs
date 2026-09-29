#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using Sotn;

namespace SotnArchipelago;

// The seed's locations on the castle map, drawn like the pause-screen map: rooms you've explored filled in
// the seed's map colour (map_color), the rest dark, walls from the recomp's own map images (its Map overlay,
// loaded with its own loader). Each location is a dot: reachable in logic now, not yet, or checked; where you
// are blinks; hovering a dot names its locations. Positions come from the game's stage data (MapData.g.cs),
// reachability from the seed's own logic (MapLogic), explored rooms from the game's map data.
public sealed class MapPanel : IPanel
{
    public string Name => "Archipelago map";
    public string TitleKey => "panel.archipelago.map";
    public bool IsOpen { get; set; }

    // The recomp's map images are 320x255; a map cell is 5 pixels and the top 3 rows are cut off
    // (MapOverlayPanel). The reverse castle's cells sit 7 rows lower than its image.
    const float ImageWidth = 320, ImageHeight = 255, CellPixels = 5, TopCut = 15;
    const int ReverseRowShift = 7;

    // ImGui colours are 0xAABBGGRR.
    const uint InLogic = 0xFF46E05A, OutOfLogic = 0xFF4A4AE8, NoLogic = 0xFF40C8F0, Checked = 0xFF9A9A9A;
    const uint DotOutline = 0xFF101010, Background = 0xFF140A08, UnexploredWalls = 0x55FFFFFF;

    enum View { WhereIAm, Castle, Inverted }
    static readonly string[] ViewNames = ["Where I am", "Castle", "Inverted castle"];

    const string ViewKey = "Archipelago.Map.View", CheckedKey = "Archipelago.Map.ShowChecked", ItemsKey = "Archipelago.Map.ShowItems";
    const string UnexploredKey = "Archipelago.Map.WholeCastle", CountsKey = "Archipelago.Map.ShowCounts";
    View _view;
    bool _showChecked;
    bool _showItems;
    bool _showUnexplored;
    bool _showCounts;

    uint _castleTexture, _invertedTexture;
    bool _texturesTried;

    // Locations by (inverted castle, x, y) for this seed.
    Dictionary<(bool Inverted, int X, int Y), List<long>> _spots = [];
    int _spotsFor = -1;

    public MapPanel()
    {
        var settings = RecompOne.Runtime.Runtime.View;
        _view = (View)Math.Clamp(settings.GetInt(ViewKey, 0), 0, 2);
        _showChecked = settings.GetBool(CheckedKey, true);
        _showItems = settings.GetBool(ItemsKey, false);
        _showUnexplored = settings.GetBool(UnexploredKey, true);
        _showCounts = settings.GetBool(CountsKey, true);
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(720, 660), ImGuiCond.FirstUseEver);
        bool open = IsOpen;
        if (!ImGui.Begin(Name, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        if (!ApClient.HasSeed)
            ImGui.TextDisabled("Connect to see your seed's locations.");
        else
        {
            RefreshSpots();
            var you = WhereYouAre();
            bool inverted = _view switch { View.Castle => false, View.Inverted => true, _ => you?.Inverted ?? false };
            if (UiOptions.Show) DrawControls();
            if (_showCounts) DrawSummary();
            DrawMap(inverted, you);
        }

        IsOpen = open;
        ImGui.End();
    }

    // ---- controls ----

    void DrawControls()
    {
        int view = (int)_view;
        ImGui.SetNextItemWidth(150);
        bool changed = ImGui.Combo("##view", ref view, ViewNames, ViewNames.Length);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Which castle to show: the one you're in, or either.");
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Show unexplored rooms", ref _showUnexplored);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("On: the rooms you haven't explored yet are drawn faintly, with their locations. Off: only the rooms you've explored and their locations, as on the pause map.");
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Show checked", ref _showChecked);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Also show locations you've already checked (grey).");
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Show what's there", ref _showItems);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Spoilers: when you hover a location, say which item is there and whose it is.");
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Show progress", ref _showCounts);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Above the map: how many locations you've checked, and how many of the rest you can reach now.");
        if (!changed) return;
        _view = (View)view;
        var settings = RecompOne.Runtime.Runtime.View;
        settings.SetInt(ViewKey, view);
        settings.SetBool(CheckedKey, _showChecked);
        settings.SetBool(ItemsKey, _showItems);
        settings.SetBool(UnexploredKey, _showUnexplored);
        settings.SetBool(CountsKey, _showCounts);
        RecompOne.Runtime.Runtime.SaveView();
    }

    void DrawSummary()
    {
        var reachable = MapLogic.Reachable;
        bool logic = MapLogic.Available;
        int left = 0, inLogic = 0, enemies = 0, enemiesInLogic = 0;
        foreach (var loc in LocationData.All)
        {
            if (!ApClient.IsMissing(loc.Id)) continue;
            bool reach = reachable.Contains(loc.Id);
            if (loc.Kind == Detect.Enemy) { enemies++; if (reach) enemiesInLogic++; }
            else { left++; if (reach) inLogic++; }
        }

        ImGui.Text($"{ApClient.CheckedCount} of {ApClient.LocationCount} checked");
        if (logic)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.Text($"{inLogic} of the {left} left reachable now" + (enemies > 0 ? $"; enemies: {enemiesInLogic} of {enemies}" : ""));
        }

        // The colours explained, with the other window options.
        if (UiOptions.Show)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
        }
        if (UiOptions.Show && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            Legend(logic ? InLogic : NoLogic, logic ? "Reachable now" : "Not checked");
            if (logic) Legend(OutOfLogic, "Blocked: needs items you don't have yet");
            Legend(Checked, "Checked");
            Legend(0xFFFFFFFF, "You are here", square: true);
            ImGui.EndTooltip();
        }
        if (!logic) ImGui.TextDisabled("This seed has no logic data, so reachability isn't shown.");
    }

    static void Legend(uint colour, string label, bool square = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var at = ImGui.GetCursorScreenPos();
        float size = ImGui.GetTextLineHeight();
        var centre = at + new Vector2(size / 2, size / 2);
        if (square) draw.AddRectFilled(centre - new Vector2(size / 3, size / 3), centre + new Vector2(size / 3, size / 3), colour);
        else
        {
            draw.AddCircleFilled(centre, size / 3 + 1, DotOutline);
            draw.AddCircleFilled(centre, size / 3, colour);
        }
        ImGui.Dummy(new Vector2(size, size));
        ImGui.SameLine();
        ImGui.Text(label);
    }

    // ---- the map ----

    void DrawMap(bool inverted, (bool Inverted, int X, int Y)? you)
    {
        LoadTextures();
        var avail = ImGui.GetContentRegionAvail();
        float scale = MathF.Max(0.5f, MathF.Min(avail.X / ImageWidth, avail.Y / ImageHeight));
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImageWidth * scale, ImageHeight * scale);
        var draw = ImGui.GetWindowDrawList();
        var (fill, walls) = MapColours();

        float cell = CellPixels * scale;
        int shift = inverted ? ReverseRowShift : 0;
        Vector2 Corner(int x, int y) => origin + new Vector2(x * cell, (y - shift) * cell - TopCut * scale);

        draw.AddRectFilled(origin, origin + size, Background);
        draw.PushClipRect(origin, origin + size, true);

        // Explored rooms filled, their walls bright; the rest of the castle's walls faint (Show unexplored rooms)
        // or not drawn, as on the pause map.
        var explored = Explored(inverted);
        var exploredCells = explored.ToHashSet();
        foreach (var (x, y) in explored) draw.AddRectFilled(Corner(x, y), Corner(x, y) + new Vector2(cell, cell), fill);
        uint texture = inverted ? _invertedTexture : _castleTexture;
        if (texture != 0)
        {
            if (_showUnexplored) draw.AddImage((nint)texture, origin, origin + size, Vector2.Zero, Vector2.One, UnexploredWalls);
            foreach (var (x, y) in explored)
            {
                var uv0 = new Vector2(x * CellPixels / ImageWidth, ((y - shift) * CellPixels - TopCut) / ImageHeight);
                var uv1 = uv0 + new Vector2(CellPixels / ImageWidth, CellPixels / ImageHeight);
                draw.AddImage((nint)texture, Corner(x, y), Corner(x, y) + new Vector2(cell, cell), uv0, uv1, walls);
            }
        }
        else draw.AddText(origin + new Vector2(8, 8), 0xFFFFFFFF, "(the recomp's castle map image couldn't be loaded)");

        // Where you are: a blinking square, as on the pause map.
        if (you is { } me && me.Inverted == inverted)
        {
            float pulse = 0.55f + 0.45f * MathF.Abs(MathF.Sin((float)ImGui.GetTime() * 4f));
            var corner = Corner(me.X, me.Y);
            var grow = new Vector2(MathF.Max(1f, cell * 0.15f), MathF.Max(1f, cell * 0.15f));
            draw.AddRectFilled(corner - grow - Vector2.One, corner + new Vector2(cell, cell) + grow + Vector2.One, 0xFF000000);
            draw.AddRectFilled(corner - grow, corner + new Vector2(cell, cell) + grow, ((uint)(pulse * 255) << 24) | 0x00FFFFFF);
        }

        // Locations.
        var reachable = MapLogic.Reachable;
        bool logic = MapLogic.Available;
        var mouse = ImGui.GetMousePos();
        bool mouseOnMap = ImGui.IsWindowHovered() && mouse.X >= origin.X && mouse.Y >= origin.Y
                          && mouse.X < origin.X + size.X && mouse.Y < origin.Y + size.Y;
        List<long>? hovered = null;
        float radius = MathF.Max(3f, cell * 0.42f);
        foreach (var ((spotInverted, x, y), ids) in _spots)
        {
            if (spotInverted != inverted) continue;
            if (!_showUnexplored && !exploredCells.Contains((x, y))) continue; // revealed with its room
            var open = ids.Where(ApClient.IsMissing).ToList();
            if (open.Count == 0 && !_showChecked) continue;
            uint colour = open.Count == 0 ? Checked
                : !logic ? NoLogic
                : open.Any(reachable.Contains) ? InLogic
                : OutOfLogic;
            var centre = Corner(x, y) + new Vector2(cell / 2, cell / 2);
            draw.AddCircleFilled(centre, radius + 1.5f, DotOutline);
            draw.AddCircleFilled(centre, radius, colour);
            if (open.Count > 1 && radius >= 6)
            {
                string count = open.Count.ToString();
                draw.AddText(centre - ImGui.CalcTextSize(count) / 2, DotOutline, count);
            }
            if (mouseOnMap && Vector2.Distance(mouse, centre) <= radius + 2) hovered = ids;
        }

        draw.PopClipRect();
        ImGui.Dummy(size);
        if (hovered != null) DrawTooltip(hovered, reachable, logic);
    }

    void DrawTooltip(List<long> ids, IReadOnlySet<long> reachable, bool logic)
    {
        ImGui.BeginTooltip();
        foreach (long id in ids)
        {
            bool open = ApClient.IsMissing(id);
            uint colour = !open ? Checked : !logic ? NoLogic : reachable.Contains(id) ? InLogic : OutOfLogic;
            string text = LocationData.Get(id)?.Name ?? $"Location {id}";
            if (_showItems && ApClient.TryGetScout(id, out var item))
                text += item.Player == ApClient.Slot
                    ? $": {ApClient.ItemName(item.Item, item.Player)}"
                    : $": {ApClient.PlayerName(item.Player)}'s {ApClient.ItemName(item.Item, item.Player)}";
            if (!open) text += " (checked)";
            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(colour), text);
        }
        ImGui.EndTooltip();
    }

    void RefreshSpots()
    {
        if (_spotsFor == ApClient.ConnectionId) return;
        _spotsFor = ApClient.ConnectionId;
        _spots = [];
        foreach (var (id, (x, y, stage)) in MapData.Cells)
        {
            if (!ApClient.IsMissing(id) && !ApClient.IsChecked(id)) continue; // not in this seed
            var key = ((stage & InvertedStageBit) != 0, (int)x, (int)y);
            if (!_spots.TryGetValue(key, out var list)) _spots[key] = list = [];
            list.Add(id);
        }
    }

    // ---- the game's map data ----

    // Explored rooms: the pause map's own record, 2 bits per cell, 4 cells per byte, 16 bytes per row; the
    // inverted castle's from 0x400 (DRA func_800F2014 marks a cell). Its y is the room table's (no shift).
    const uint ExploredAddr = 0x8006BB74;
    const uint InvertedOffset = 0x400;

    static List<(int X, int Y)> Explored(bool inverted)
    {
        var cells = new List<(int, int)>();
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null || !Game.Available) return cells;
        uint start = ExploredAddr + (inverted ? InvertedOffset : 0);
        for (int i = 0; i < 64 * 16; i++)
        {
            int bits = m.ReadU8(start + (uint)i);
            if (bits == 0) continue;
            for (int k = 0; k < 4; k++)
                if ((bits >> ((3 - k) * 2) & 3) != 0) cells.Add(((i & 15) * 4 + k, i >> 4));
        }
        return cells;
    }

    // The seed's map colours (map_color; Rom.py map_color writes these PlayStation colours): room fill and
    // walls. Walls tint the recomp's grey outlines, so vanilla's grey is white here.
    static readonly ushort[] FillColours = [0xFDCA, 0xB000, 0x0050, 0x80CA, 0x0900, 0xE318, 0xB008, 0xFF1F, 0x1000, 0x0000];
    const ushort VanillaWalls = 0xE318;

    static (uint Fill, uint Walls) MapColours()
    {
        int option = ApClient.OptionInt("map_color");
        ushort fill = option >= 0 && option < FillColours.Length ? FillColours[option] : FillColours[0];
        if (fill == 0) fill = 0x1084; // "invisible": still tell explored rooms apart here, just barely
        ushort walls = option switch { 5 => 0xFFFF, 7 => 0xFD0F, _ => VanillaWalls };
        return (Abgr(fill, 1f), walls == VanillaWalls ? 0xFFFFFFFF : Abgr(walls, 255f / 197f));
    }

    static uint Abgr(ushort psx, float boost)
    {
        uint Channel(int shift) => (uint)Math.Min(255f, ((psx >> shift) & 31) * 255f / 31f * boost);
        return 0xFF000000 | Channel(10) << 16 | Channel(5) << 8 | Channel(0);
    }

    // ---- where you are (as the recomp's MapOverlayPanel works it out) ----

    (bool Inverted, int X, int Y)? _lastPosition;

    // Where you are, or where you were last while the game is paused, in a menu or the pause map, or between
    // rooms (Position has no answer then); nothing once you leave the game.
    (bool Inverted, int X, int Y)? WhereYouAre()
    {
        var now = Position();
        if (now != null) _lastPosition = now;
        else
        {
            var m = RecompOne.Runtime.Runtime.Mem;
            if (m == null || !Game.Available || m.ReadU8(Game.GameStateAddr) != (byte)GameState.Play) _lastPosition = null;
        }
        return _lastPosition;
    }

    const int InvertedStageBit = 0x20;
    const uint StageAddr = 0x800974A0, RoomLeftAddr = 0x800730B0, RoomTopAddr = 0x800730B4;
    const uint CameraXAddr = 0x800973F0, CameraYAddr = 0x800973F4;
    const uint PlayerStepAddr = 0x80073404, PlayStateAddr = 0x80073060, MapModeAddr = 0x8003C9A4, WarpingAddr = 0x80097C98;
    const int PrologueStage = 0x1F, EntranceFirstVisit = 0x41, CutsceneStage = 0x38;

    static (bool Inverted, int X, int Y)? Position()
    {
        var m = RecompOne.Runtime.Runtime.Mem;
        if (m == null || !Game.Available) return null;
        int stage = m.ReadU8(StageAddr);
        int x = m.ReadU8(RoomLeftAddr) + m.ReadU16(CameraXAddr) / 256;
        int y = m.ReadU8(RoomTopAddr) + m.ReadU16(CameraYAddr) / 256;
        if (stage == PrologueStage || stage == CutsceneStage) return null;
        if (stage == EntranceFirstVisit && (y > 41 || x < 2)) return null; // the prologue's part of the entrance
        if (m.ReadU8(PlayerStepAddr) == 0x12 || m.ReadU8(PlayStateAddr) != 3 || m.ReadU8(MapModeAddr) != 1
            || m.ReadU8(WarpingAddr) != 0) return null;
        if (x < 0 || x >= 64 || y < 0 || y >= 64) return null;
        return ((stage & InvertedStageBit) != 0, x, y);
    }

    // ---- the recomp's castle map images ----

    void LoadTextures()
    {
        if (_texturesTried) return;
        _texturesTried = true;
        _castleTexture = LoadRecompMap("Castle1");
        _invertedTexture = LoadRecompMap("Castle2");
    }

    // Recompiled.MapOverlayPanel.LoadTexture(name): reads the image embedded in the game and uploads it.
    static uint LoadRecompMap(string name)
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Recompiled.MapOverlayPanel")).FirstOrDefault(t => t != null);
            var load = type?.GetMethod("LoadTexture", BindingFlags.NonPublic | BindingFlags.Static, [typeof(string)]);
            if (load == null)
            {
                Log.Error("map: the recomp's map image loader wasn't found; drawing without the castle image");
                return 0;
            }
            return load.Invoke(null, [name]) is uint texture ? texture : 0;
        }
        catch (Exception ex)
        {
            Log.Error($"map: couldn't load the castle image {name}: {ex.Message}");
            return 0;
        }
    }
}
