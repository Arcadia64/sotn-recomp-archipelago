#nullable enable
using System;
using System.Text;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace SotnArchipelago;

// Keeps our windows where you docked them. On its first frame SymphonyRecomp rebuilds the dock layout around the
// game picture if that's the only window open (HostWindow.DrawDockspace), which is always true at startup since
// mods load later, so docked Archipelago windows came back floating. The mod keeps its own copy of the layout
// (ImGui's settings text, in interface.ini as Archipelago.Layout) while its windows are open, and puts it back
// the first time they're drawn again. A panel with no window, always "open", registered before our windows so it
// runs before them each frame.
sealed class LayoutKeeper(Func<bool> anyWindowOpen) : IFloatingPanel
{
    public string Name => "Archipelago layout";
    public bool IsOpen { get => true; set { } }

    const string Key = "Archipelago.Layout";
    const int CheckEveryFrames = 60;
    const int SaveEveryFrames = 60 * 10;   // written to disk at most this often (and when the game closes)

    bool _restored;
    string _last = "";
    long _frame;
    long _savedAt = -SaveEveryFrames;

    public void Draw()
    {
        _frame++;
        if (!_restored)
        {
            _restored = true;
            if (anyWindowOpen()) Restore();
            return;
        }
        if (_frame % CheckEveryFrames != 0 || !anyWindowOpen()) return;

        string ini = ImGui.SaveIniSettingsToMemory();
        if (ini == _last) return;
        _last = ini;
        var view = RecompOne.Runtime.Runtime.View;
        view.SetString(Key, Convert.ToBase64String(Encoding.UTF8.GetBytes(ini)));
        if (_frame - _savedAt < SaveEveryFrames) return; // the recomp saves it with everything else on exit
        _savedAt = _frame;
        RecompOne.Runtime.Runtime.SaveView();
    }

    void Restore()
    {
        string stored = RecompOne.Runtime.Runtime.View.GetString(Key, "");
        if (stored.Length == 0) return;
        try
        {
            string ini = Encoding.UTF8.GetString(Convert.FromBase64String(stored));
            ImGui.LoadIniSettingsFromMemory(ini);
            _last = ini;
            Log.Info("window layout restored");
        }
        catch (FormatException)
        {
            RecompOne.Runtime.Runtime.View.SetString(Key, "");
        }
    }
}
