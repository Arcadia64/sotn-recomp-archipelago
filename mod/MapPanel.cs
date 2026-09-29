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

// The seed's locations on the castle map: checked, reachable in logic now (green) or not yet (red), with
// where you are and the location names on hover. The map images are the recomp's own (its Map overlay,
// Misc > Overlays), loaded with its own loader; positions come from the game's stage data (MapData.g.cs)
// and reachability from the seed's logic (MapLogic).
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
    const uint InLogic = 0xFF40D040, OutOfLogic = 0xFF4040E0, NoLogic = 0xFF40C0E0, Checked = 0xFF808080;
    const uint Outline = 0xFF101010, You = 0xFFE040E0, Background = 0xFF301810;

    enum View { FollowMe, Normal, Reverse }

    const string ViewKey = "Archipelago.Map.View", CheckedKey = "Archipelago.Map.ShowChecked", ItemsKey = "Archipelago.Map.ShowItems";
    View _view;
    bool _showChecked;
    bool _showItems;

    uint _normalTexture, _reverseTexture;
    bool _texturesTried;

    // Locations by (reverse castle, x, y) for this connection's seed.
    Dictionary<(bool Reverse, int X, int Y), List<long>> _spots = [];
    int _spotsFor = -1;

    public MapPanel()
    {
        var settings = RecompOne.Runtime.Runtime.View;
        _view = (View)Math.Clamp(settings.GetInt(ViewKey, 0), 0, 2);
        _showChecked = settings.GetBool(CheckedKey, true);
        _showItems = settings.GetBool(ItemsKey, false);
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(720, 640), ImGuiCond.FirstUseEver);
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
            var you = Position();
            bool reverse = _view switch { View.Normal => false, View.Reverse => true, _ => you?.Reverse ?? false };
            DrawToolbar();
            DrawSummary();
            DrawMap(reverse, you);
        }

        IsOpen = open;
        ImGui.End();
    }

    void DrawToolbar()
    {
        int view = (int)_view;
        bool changed = ImGui.RadioButton("Follow me", ref view, 0);
        ImGui.SameLine();
        changed |= ImGui.RadioButton("Castle", ref view, 1);
        ImGui.SameLine();
        changed |= ImGui.RadioButton("Inverted castle", ref view, 2);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Show checked", ref _showChecked);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Show items", ref _showItems);
        if (!changed) return;
        _view = (View)view;
        var settings = RecompOne.Runtime.Runtime.View;
        settings.SetInt(ViewKey, view);
        settings.SetBool(CheckedKey, _showChecked);
        settings.SetBool(ItemsKey, _showItems);
        RecompOne.Runtime.Runtime.SaveView();
    }

    void DrawSummary()
    {
        var reachable = MapLogic.Reachable;
        int open = 0, inLogic = 0, enemies = 0, enemiesInLogic = 0;
        foreach (var loc in LocationData.All)
        {
            if (!ApClient.IsMissing(loc.Id)) continue;
            bool reach = reachable.Contains(loc.Id);
            if (loc.Kind == Detect.Enemy)
            {
                enemies++;
                if (reach) enemiesInLogic++;
            }
            else
            {
                open++;
                if (reach) inLogic++;
            }
        }
        string text = $"Checked {ApClient.CheckedCount}/{ApClient.LocationCount}.";
        text += MapLogic.Available ? $"  In logic now: {inLogic} of {open} left" : "  (This seed has no logic data: reachability isn't shown.)";
        if (enemies > 0) text += MapLogic.Available ? $"; enemysanity {enemiesInLogic} of {enemies}" : $"; enemysanity {enemies} left";
        ImGui.TextWrapped(text);
        ImGui.TextDisabled("Green: in logic now.  Red: not yet.  Grey: checked.  Pink: you.");
    }

    void DrawMap(bool reverse, (bool Reverse, int X, int Y)? you)
    {
        LoadTextures();
        var avail = ImGui.GetContentRegionAvail();
        float scale = MathF.Max(0.5f, MathF.Min(avail.X / ImageWidth, avail.Y / ImageHeight));
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImageWidth * scale, ImageHeight * scale);
        var draw = ImGui.GetWindowDrawList();

        draw.AddRectFilled(origin, origin + size, Background);
        uint texture = reverse ? _reverseTexture : _normalTexture;
        if (texture != 0) draw.AddImage((nint)texture, origin, origin + size);
        else draw.AddText(origin + new Vector2(8, 8), 0xFFFFFFFF, "(the recomp's castle map image couldn't be loaded)");

        float cell = CellPixels * scale;
        Vector2 CellCorner(int x, int y) =>
            origin + new Vector2(x * cell, (y - (reverse ? ReverseRowShift : 0)) * cell - TopCut * scale);

        var reachable = MapLogic.Reachable;
        bool logic = MapLogic.Available;
        var mouse = ImGui.GetMousePos();
        bool mouseOnMap = ImGui.IsWindowHovered() && mouse.X >= origin.X && mouse.Y >= origin.Y
                          && mouse.X < origin.X + size.X && mouse.Y < origin.Y + size.Y;
        List<long>? hovered = null;
        float radius = MathF.Max(3f, cell * 0.45f);

        foreach (var ((spotReverse, x, y), ids) in _spots)
        {
            if (spotReverse != reverse) continue;
            var open = ids.Where(ApClient.IsMissing).ToList();
            if (open.Count == 0 && !_showChecked) continue;
            uint colour = open.Count == 0 ? Checked
                : !logic ? NoLogic
                : open.Any(reachable.Contains) ? InLogic
                : OutOfLogic;
            var centre = CellCorner(x, y) + new Vector2(cell / 2, cell / 2);
            draw.AddCircleFilled(centre, radius + 1, Outline);
            draw.AddCircleFilled(centre, radius, colour);
            if (open.Count > 1 && radius >= 6)
            {
                string count = open.Count.ToString();
                draw.AddText(centre - ImGui.CalcTextSize(count) / 2, Outline, count);
            }
            if (mouseOnMap && Vector2.Distance(mouse, centre) <= radius + 2) hovered = ids;
        }

        if (you is { } me && me.Reverse == reverse)
        {
            var corner = CellCorner(me.X, me.Y);
            draw.AddRect(corner - Vector2.One, corner + new Vector2(cell + 1, cell + 1), You, 0, ImDrawFlags.None, 2f);
        }

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
            var key = ((stage & ReverseStageBit) != 0, (int)x, (int)y);
            if (!_spots.TryGetValue(key, out var list)) _spots[key] = list = [];
            list.Add(id);
        }
    }

    // ---- where you are (as the recomp's MapOverlayPanel works it out) ----

    const int ReverseStageBit = 0x20;
    const uint StageAddr = 0x800974A0, RoomLeftAddr = 0x800730B0, RoomTopAddr = 0x800730B4;
    const uint CameraXAddr = 0x800973F0, CameraYAddr = 0x800973F4;
    const uint PlayerStepAddr = 0x80073404, PlayStateAddr = 0x80073060, MapModeAddr = 0x8003C9A4, WarpingAddr = 0x80097C98;
    const int PrologueStage = 0x1F, EntranceFirstVisit = 0x41, CutsceneStage = 0x38;

    static (bool Reverse, int X, int Y)? Position()
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
        return ((stage & ReverseStageBit) != 0, x, y);
    }

    // ---- the recomp's castle map images ----

    void LoadTextures()
    {
        if (_texturesTried) return;
        _texturesTried = true;
        _normalTexture = LoadRecompMap("Castle1");
        _reverseTexture = LoadRecompMap("Castle2");
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
