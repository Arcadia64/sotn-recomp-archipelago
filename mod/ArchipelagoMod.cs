using System;
using System.Linq;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Modding;

namespace SotnArchipelago;

public sealed class ArchipelagoMod : IMod
{
    const string MenuLabel = "Archipelago";

    readonly ArchipelagoPanel _panel = new();

    public void OnLoad()
    {
        Log.Info("loading");
        ReportAssemblies();

        PanelManager.Register(_panel);
        MenuRegistry.BarItem(MenuLabel, TogglePanel).After("menu.randomizer");
        Event.AddListener<VSyncEvent>(OnVSync);
    }

    public void OnUnload()
    {
        ApClient.Disconnect();
        Event.RemoveListener<VSyncEvent>(OnVSync);
        MenuRegistry.Remove(MenuLabel);
        PanelManager.Unregister(_panel);
        Log.Info("unloaded");
    }

    public void DrawSettings() => _panel.DrawStatus();

    void TogglePanel() => _panel.IsOpen = !_panel.IsOpen;

    static void OnVSync(VSyncEvent e)
    {
        GameWatch.Tick(e.Memory, e.Frame);
        SeedPlan.Update();
        CheckTracker.Tick(e.Memory, e.Frame);
        ItemGiver.Tick(e.Memory, e.Frame);
        while (ApClient.TryDequeueDeath(out var cause)) GameRules.OnDeathLink(cause);
        GameRules.Tick(e.Memory, e.Frame);

        while (ApClient.TryDequeueToast(out var toast))
            ToastNotifications.ShowText(toast.Title, toast.Message);
    }

    // Mods are compiled only against assemblies the game already has loaded,
    // so log which of the ones we plan to use are available.
    static void ReportAssemblies()
    {
        string[] wanted =
        [
            "System.Text.Json",
            "System.Net.WebSockets",
            "System.Net.WebSockets.Client",
            "System.Net.Sockets",
            "System.Net.Security",
            "System.Private.Uri",
            "System.Collections",
            "System.Collections.Concurrent",
        ];
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in wanted)
            Log.Info($"assembly {name}: {(loaded.Contains(name) ? "loaded" : "NOT loaded")}");
    }
}
