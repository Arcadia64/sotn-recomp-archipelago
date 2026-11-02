#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using RecompOne.Runtime.Host.Window;

namespace SotnArchipelago;

// The recomp's settings store, for the mod's own keys and windows. SymphonyRecomp 1.0 replaced Runtime.View
// (kept in interface.ini) with Config.Settings (settings.json), and mods are compiled against whichever version
// is installed, so the store is found through reflection and one mod zip works with both. On 1.0 this also takes
// over what an older version left in interface.ini (server, slot, window layout) the first time it runs.
sealed class ModSettings
{
    const string InterfaceFile = "interface.ini", RecompSection = "[RecompOne]", KeyPrefix = "Archipelago.";
    static readonly Assembly Recomp = typeof(RecompOne.Runtime.Runtime).Assembly;

    public static readonly ModSettings View = new();

    readonly Func<string, string, string> _getString;
    readonly Action<string, string> _setString;
    readonly Func<string, bool, bool> _getBool;
    readonly Action<string, bool> _setBool;
    readonly Func<string, int, int> _getInt;
    readonly Action<string, int> _setInt;
    readonly Action _save;
    readonly Func<IPanel, bool, bool> _panelOpen;

    ModSettings()
    {
        if (Recomp.GetType("RecompOne.Runtime.Config.Settings") is { } settings)
        {
            T Bind<T>(string name, Type value) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), settings
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == name && m.IsGenericMethodDefinition).MakeGenericMethod(value));
            _getString = Bind<Func<string, string, string>>("Get", typeof(string));
            _setString = Bind<Action<string, string>>("Set", typeof(string));
            _getBool = Bind<Func<string, bool, bool>>("Get", typeof(bool));
            _setBool = Bind<Action<string, bool>>("Set", typeof(bool));
            _getInt = Bind<Func<string, int, int>>("Get", typeof(int));
            _setInt = Bind<Action<string, int>>("Set", typeof(int));
            _save = (Action)Delegate.CreateDelegate(typeof(Action), settings.GetMethod("SaveIfDirty")!);
            var has = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), settings.GetMethod("Has")!);
            TakeOverInterfaceFile(has);

            var apply = Recomp.GetType("RecompOne.Runtime.Host.Window.InterfaceLayout")!.GetMethod("ApplyPanels")!;
            _panelOpen = (panel, fallback) =>
            {
                panel.IsOpen = fallback;
                apply.Invoke(null, [new[] { panel }]);
                return panel.IsOpen;
            };
            return;
        }

        var view = typeof(RecompOne.Runtime.Runtime).GetProperty("View")!;
        object Store() => view.GetValue(null)!;
        object? Call(string method, params object[] args) => Store().GetType().GetMethod(method)!.Invoke(Store(), args);
        _getString = (key, fallback) => (string)Call("GetString", key, fallback)!;
        _setString = (key, value) => Call("SetString", key, value);
        _getBool = (key, fallback) => (bool)Call("GetBool", key, fallback)!;
        _setBool = (key, value) => Call("SetBool", key, value);
        _getInt = (key, fallback) => (int)Call("GetInt", key, fallback)!;
        _setInt = (key, value) => Call("SetInt", key, value);
        var saveView = typeof(RecompOne.Runtime.Runtime).GetMethod("SaveView")!;
        _save = () => saveView.Invoke(null, null);
        _panelOpen = (panel, fallback) =>
        {
            var panels = (IDictionary)Store().GetType().GetProperty("Panels")!.GetValue(Store())!;
            var state = panels[panel.Name];
            return state == null ? fallback : (bool)state.GetType().GetProperty("Open")!.GetValue(state)!;
        };
    }

    public string GetString(string key, string fallback = "") => _getString(key, fallback);
    public void SetString(string key, string value) => _setString(key, value);
    public bool GetBool(string key, bool fallback = false) => _getBool(key, fallback);
    public void SetBool(string key, bool value) => _setBool(key, value);
    public int GetInt(string key, int fallback = 0) => _getInt(key, fallback);
    public void SetInt(string key, int value) => _setInt(key, value);

    public static void Save() => View._save();

    // Whether a window was open when the game last closed.
    public bool PanelOpen(IPanel panel, bool fallback) => _panelOpen(panel, fallback);

    void TakeOverInterfaceFile(Func<string, bool> has)
    {
        try
        {
            if (!File.Exists(InterfaceFile)) return;
            bool section = false;
            foreach (var raw in File.ReadAllLines(InterfaceFile))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith('[')) section = line == RecompSection;
                int equals = line.IndexOf('=');
                if (!section || equals < 0 || !line.StartsWith(KeyPrefix)) continue;
                string key = line[..equals];
                if (!has(key)) _setString(key, line[(equals + 1)..]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"couldn't read the old settings in {InterfaceFile}: {ex.Message}");
        }
    }
}
