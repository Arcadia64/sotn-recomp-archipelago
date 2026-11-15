#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace SotnArchipelago;

// Keeps our windows where you docked them. On its first frame SymphonyRecomp rebuilds the dock layout around the
// game picture if that's the only window open (HostWindow.DrawDockspace), which is always true at startup since
// mods load later, so docked Archipelago windows came back floating. The mod keeps its own copy of the layout
// (ImGui's settings text, in interface.ini as Archipelago.Layout) while its windows are open, and puts it back
// the first time they're drawn again. Without a copy yet (the first run with this), it uses the layout the recomp
// wrote to interface.ini when the game last closed, and with nothing saved at all (a first run, or the recomp's
// files were reset) it docks them itself: the map on the right of the game picture, the items under it and the
// text client under that. The Connection window floats. That column keeps its share of the game window's width
// until you resize it yourself: ImGui alone gives every extra pixel to the game picture, and the game starts in
// a small window, so the column would stay as narrow as it was made. A panel with no window, always "open",
// registered before our windows so it runs before them each frame.
//
// The layout is loaded partway through a frame, after the recomp's DockSpace() call for that frame. A window
// docked into the rebuilt nodes that same frame finds its dockspace not submitted this frame and ImGui turns the
// node into a floating one (BeginDocked: orphaned root). So our windows skip the frame of the restore (Holding);
// the next frame DockSpace() binds the rebuilt root first and they dock where they were.
sealed class LayoutKeeper : IFloatingPanel
{
    public string Name => "Archipelago layout";
    public bool IsOpen { get => true; set { } }

    const string Key = "Archipelago.Layout";
    const string InterfaceFile = "interface.ini";   // the recomp's, next to the game
    const int CheckEveryFrames = 60;
    const int SaveEveryFrames = 60 * 10;   // written to disk at most this often (and when the game closes)

    // The default layout: this much of the game window's width for our column, and of the column's height for
    // the map and the items; the text client gets the rest.
    const float ColumnWidth = 0.30f, MapHeight = 0.52f, ItemsHeight = 0.12f;

    // The default layout's column while it's still the width we gave it: its dock node (kept in the settings, so
    // it carries on next time), that width, and the game window's width it was worked out for.
    const string ColumnKey = "Archipelago.LayoutColumn";
    uint _column;   // 0: there's none, or it has been resized by hand
    float _columnWidth, _areaWidth;
    bool _columnJustRestored;   // its width in the saved layout was ours: no need to check before the first resize

    readonly Func<bool> _anyWindowOpen;
    readonly string _map, _items, _messages;   // the windows of the default layout, by name
    readonly string? _lastExitLayout;   // the ImGui part of interface.ini as the game left it, read at load
    bool _restored;
    string _last = "";
    long _frame;
    long _savedAt = -SaveEveryFrames;

    // Windows whose docked-or-floating state gets logged once after a restore (LayoutKeeper.Report).
    static readonly HashSet<string> _toReport = [];

    // True for the frame the layout is restored in: our windows don't draw that frame.
    public static bool Holding { get; private set; }

    public LayoutKeeper(Func<bool> anyWindowOpen, string map, string items, string messages)
    {
        _anyWindowOpen = anyWindowOpen;
        (_map, _items, _messages) = (map, items, messages);
        _lastExitLayout = ReadLastExitLayout();
    }

    public void Draw()
    {
        _frame++;
        Holding = false;
        if (!_restored)
        {
            _restored = true;
            if (_anyWindowOpen()) Restore();
            return;
        }
        if (_column != 0) KeepColumnShare();
        if (_frame % CheckEveryFrames != 0 || !_anyWindowOpen()) return;

        string ini = Gui.SaveIni();
        if (ini == _last) return;
        _last = ini;
        if (_column != 0 && !_columnJustRestored && !ColumnIsOurWidth(ini)) ForgetColumn();
        var view = ModSettings.View;
        view.SetString(Key, Convert.ToBase64String(Encoding.UTF8.GetBytes(ini)));
        if (_frame - _savedAt < SaveEveryFrames) return; // the recomp saves it with everything else on exit
        _savedAt = _frame;
        ModSettings.Save();
    }

    // Called by each window right after it begins: logs, once after a restore, whether it came back docked.
    public static void Report(string window)
    {
        if (!_toReport.Remove(window)) return;
        Log.Info($"layout: {window} is {(ImGui.IsWindowDocked() ? "docked" : "floating")}");
    }

    void Restore()
    {
        string? ini = null, from = null;
        string stored = ModSettings.View.GetString(Key, "");
        if (stored.Length > 0)
        {
            try { ini = Encoding.UTF8.GetString(Convert.FromBase64String(stored)); from = "the mod's copy"; }
            catch (FormatException) { ModSettings.View.SetString(Key, ""); }
        }
        if (ini == null && _lastExitLayout != null) { ini = _lastExitLayout; from = "interface.ini"; }
        if (ini == null)
        {
            DockDefault();
            return;
        }

        ini = Rebind(ini, Gui.SaveIni());
        ImGui.LoadIniSettingsFromMemory(ini);
        Holding = true;
        _last = ini;
        uint column = (uint)ModSettings.View.GetInt(ColumnKey, 0);
        if (column != 0 && Node(ini, column) != null) (_column, _areaWidth, _columnJustRestored) = (column, 0, true);
        var docked = DockedWindows(ini);
        Log.Info($"layout restored from {from}: " + (docked.Count > 0 ? $"docked {string.Join(", ", docked)}" : "no Archipelago window was docked"));
        foreach (var name in docked) _toReport.Add(name);
    }

    // Nothing saved: splits our column off the right of the game's dock area (the game picture keeps the rest) and
    // the column into three, top to bottom. The windows dock there when they're next drawn, now or when opened.
    unsafe void DockDefault()
    {
        if (HostDock(Gui.SaveIni()) is not { } host) return;
        try
        {
            uint area = Convert.ToUInt32(host.Id, 16), picture, column, map, below, items, messages;
            column = igDockBuilderSplitNode(area, DirRight, ColumnWidth, null, &picture);
            map = igDockBuilderSplitNode(column, DirUp, MapHeight, null, &below);
            items = igDockBuilderSplitNode(below, DirUp, ItemsHeight / (1 - MapHeight), null, &messages);
            foreach (var (window, node) in new[] { (_map, map), (_items, items), (_messages, messages) })
            {
                fixed (byte* name = Encoding.UTF8.GetBytes(window + "\0")) igDockBuilderDockWindow(name, node);
                _toReport.Add(window);
            }
            igDockBuilderFinish(area);
            Holding = true;
            if (Node(Gui.SaveIni(), column) is { } made)
            {
                (_column, _columnWidth, _areaWidth) = (column, made.Width, ImGui.GetMainViewport().Size.X);
                ModSettings.View.SetInt(ColumnKey, (int)column);
            }
            Log.Info("layout: nothing saved, so the map, items and text client are docked on the right");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or FormatException)
        {
            Log.Error($"couldn't dock the windows: {ex.Message}");
        }
    }

    // The game window changed width: the column gets its share of the new width, unless it was resized by hand
    // since (then it's left alone from here on, as any other saved layout is).
    void KeepColumnShare()
    {
        float width = ImGui.GetMainViewport().Size.X;
        if (width <= 0 || width == _areaWidth) return;
        string ini = Gui.SaveIni();
        if (Node(ini, _column) is not { } node || (!_columnJustRestored && !ColumnIsOurWidth(ini)))
        {
            ForgetColumn();
            return;
        }
        (_areaWidth, _columnWidth, _columnJustRestored) = (width, MathF.Round(width * ColumnWidth), false);
        try
        {
            igDockBuilderSetNodeSize(_column, new Vector2(_columnWidth, node.Height));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Error($"couldn't resize the docked windows: {ex.Message}");
            ForgetColumn();
        }
    }

    bool ColumnIsOurWidth(string ini) => Node(ini, _column) is { } node && MathF.Abs(node.Width - _columnWidth) <= 1.5f;

    void ForgetColumn()
    {
        _column = 0;
        ModSettings.View.SetInt(ColumnKey, 0);
    }

    // A dock node's size as the layout text has it: "DockNode ID=0x00000002 Parent=0x... SizeRef=384,684 ...".
    static (float Width, float Height)? Node(string ini, uint id)
    {
        string want = "ID=0x" + id.ToString("X8");
        foreach (var raw in ini.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] != "DockNode" || parts[1] != want) continue;
            var size = parts.FirstOrDefault(p => p.StartsWith("SizeRef="))?[8..].Split(',');
            if (size is { Length: 2 } && int.TryParse(size[0], out int width) && int.TryParse(size[1], out int height))
                return (width, height);
        }
        return null;
    }

    // ImGui's dock builder isn't in ImGui.NET, so these go to the game's cimgui directly, as the recomp's own
    // DockBuilder does.
    const int DirRight = 1, DirUp = 2;

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    static extern unsafe uint igDockBuilderSplitNode(uint node, int direction, float share, uint* atDirection, uint* atOpposite);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    static extern unsafe void igDockBuilderDockWindow(byte* windowName, uint node);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    static extern void igDockBuilderFinish(uint node);

    [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
    static extern void igDockBuilderSetNodeSize(uint node, Vector2 size);

    // Points a saved layout at the game's dock area as it is now. The area's ID is a hash, and ImGui changed how it
    // hashes (SymphonyRecomp v1.2 came with the newer ImGui), so a layout saved before an update like that names
    // an area that no longer exists and every window docked in it would come back floating. The nodes inside the
    // area are numbered, not hashed, and windows go by name, so only the area's own ID and its window's change.
    static string Rebind(string saved, string live)
    {
        var was = HostDock(saved);
        var now = HostDock(live);
        if (was == null || now == null || was.Value.Id == now.Value.Id) return saved;
        Log.Info($"layout: saved for dock area {was.Value.Id}, which is {now.Value.Id} in this version; moved over");
        var lines = saved.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("DockSpace ") && !trimmed.StartsWith("DockNode ")) continue;
            lines[i] = lines[i].Replace("ID=" + was.Value.Id, "ID=" + now.Value.Id)
                .Replace("Parent=" + was.Value.Id, "Parent=" + now.Value.Id)
                .Replace("Window=" + was.Value.Window, "Window=" + now.Value.Window);
        }
        return string.Join('\n', lines);
    }

    // The ID of the dock area the game's host window owns, and that window's ID, as the layout text has them:
    // "DockSpace ID=0x... Window=0x... ...". Other DockSpace lines (no Window) are areas nothing owns any more.
    static (string Id, string Window)? HostDock(string ini)
    {
        foreach (var raw in ini.Split('\n'))
        {
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "DockSpace") continue;
            string? id = parts.FirstOrDefault(p => p.StartsWith("ID="))?[3..];
            string? window = parts.FirstOrDefault(p => p.StartsWith("Window="))?[7..];
            if (id != null && window != null) return (id, window);
        }
        return null;
    }

    // Our windows that the layout text has docked ([Window][Archipelago ...] sections with a DockId).
    static List<string> DockedWindows(string ini)
    {
        var docked = new List<string>();
        foreach (var block in ini.Replace("\r", "").Split("\n\n"))
        {
            var lines = block.Trim().Split('\n');
            if (lines.Length == 0 || !lines[0].StartsWith("[Window][Archipelago")) continue;
            if (lines.Any(l => l.StartsWith("DockId="))) docked.Add(lines[0]["[Window][".Length..^1]);
        }
        return docked;
    }

    // The ImGui settings part of interface.ini (everything outside its [RecompOne] section), if it has any of our
    // windows.
    static string? ReadLastExitLayout()
    {
        try
        {
            if (!File.Exists(InterfaceFile)) return null;
            var sb = new StringBuilder();
            bool recompSection = false;
            foreach (var raw in File.ReadAllLines(InterfaceFile))
            {
                string line = raw.TrimEnd('\r');
                if (line == "[RecompOne]") { recompSection = true; continue; }
                if (line.StartsWith('[')) recompSection = false;
                if (!recompSection) sb.Append(line).Append('\n');
            }
            string ini = sb.ToString();
            return ini.Contains("[Window][Archipelago") ? ini : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
