#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace SotnArchipelago;

// Keeps our windows where you docked them. On its first frame SymphonyRecomp rebuilds the dock layout around the
// game picture if that's the only window open (HostWindow.DrawDockspace), which is always true at startup since
// mods load later, so docked Archipelago windows came back floating. The mod keeps its own copy of the layout
// (ImGui's settings text, in interface.ini as Archipelago.Layout) while its windows are open, and puts it back
// the first time they're drawn again. Without a copy yet (the first run with this), it uses the layout the recomp
// wrote to interface.ini when the game last closed. A panel with no window, always "open", registered before our
// windows so it runs before them each frame.
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
    const string InterfaceFile = "interface.ini";   // the recomp's, next to the game (ConfigManager)
    const int CheckEveryFrames = 60;
    const int SaveEveryFrames = 60 * 10;   // written to disk at most this often (and when the game closes)

    readonly Func<bool> _anyWindowOpen;
    readonly string? _lastExitLayout;   // the ImGui part of interface.ini as the game left it, read at load
    bool _restored;
    string _last = "";
    long _frame;
    long _savedAt = -SaveEveryFrames;

    // Windows whose docked-or-floating state gets logged once after a restore (LayoutKeeper.Report).
    static readonly HashSet<string> _toReport = [];

    // True for the frame the layout is restored in: our windows don't draw that frame.
    public static bool Holding { get; private set; }

    public LayoutKeeper(Func<bool> anyWindowOpen)
    {
        _anyWindowOpen = anyWindowOpen;
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
        if (_frame % CheckEveryFrames != 0 || !_anyWindowOpen()) return;

        string ini = ImGui.SaveIniSettingsToMemory();
        if (ini == _last) return;
        _last = ini;
        var view = RecompOne.Runtime.Runtime.View;
        view.SetString(Key, Convert.ToBase64String(Encoding.UTF8.GetBytes(ini)));
        if (_frame - _savedAt < SaveEveryFrames) return; // the recomp saves it with everything else on exit
        _savedAt = _frame;
        RecompOne.Runtime.Runtime.SaveView();
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
        string stored = RecompOne.Runtime.Runtime.View.GetString(Key, "");
        if (stored.Length > 0)
        {
            try { ini = Encoding.UTF8.GetString(Convert.FromBase64String(stored)); from = "the mod's copy"; }
            catch (FormatException) { RecompOne.Runtime.Runtime.View.SetString(Key, ""); }
        }
        if (ini == null && _lastExitLayout != null) { ini = _lastExitLayout; from = "interface.ini"; }
        if (ini == null) return;

        ImGui.LoadIniSettingsFromMemory(ini);
        Holding = true;
        _last = ini;
        var docked = DockedWindows(ini);
        Log.Info($"layout restored from {from}: " + (docked.Count > 0 ? $"docked {string.Join(", ", docked)}" : "no Archipelago window was docked"));
        foreach (var name in docked) _toReport.Add(name);
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
