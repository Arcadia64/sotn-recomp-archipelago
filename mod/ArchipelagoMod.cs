using System;
using System.Collections.Generic;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Modding;

namespace SotnArchipelago;

public sealed class ArchipelagoMod : IMod
{
    const string MenuKey = "menu.archipelago";

    // The recomp's UI looks text up by key; register ours (English only for now).
    const string Strings = """
        {"strings": {
          "menu.archipelago": {"en": "Archipelago"},
          "panel.archipelago": {"en": "Archipelago"}
        }}
        """;

    readonly ArchipelagoPanel _panel = new();

    public void OnLoad()
    {
        Log.Info("loading");
        Localization.Merge(Strings);
        PanelManager.Register(_panel);
        AddBarItem(MenuKey, TogglePanel);
        Event.AddListener<VSyncEvent>(OnVSync);
    }

    public void OnUnload()
    {
        ApClient.Disconnect();
        Event.RemoveListener<VSyncEvent>(OnVSync);
        MenuRegistry.Remove(MenuKey);
        RemovePanel(_panel);
        Log.Info("unloaded");
    }

    public void DrawSettings() => _panel.DrawStatus();

    void TogglePanel() => _panel.IsOpen = !_panel.IsOpen;

    // The menu and panel API differs between recomp versions, so call whichever this build has.
    // v0.5.1b: BarItem(key, onClick, order) places items by order (Randomizer is 200) and returns
    // nothing, and there's no PanelManager.Unregister. Later builds: BarItem(key, onClick) returns a
    // MenuBuilder with After(key), and Unregister exists.
    const int RandomizerOrder = 200;

    static void AddBarItem(string key, Action onClick)
    {
        var registry = typeof(MenuRegistry);
        var byOrder = registry.GetMethod("BarItem", [typeof(string), typeof(Action), typeof(int)]);
        if (byOrder != null)
        {
            byOrder.Invoke(null, [key, onClick, RandomizerOrder]); // same order, added later: just after it
            return;
        }
        var builder = registry.GetMethod("BarItem", [typeof(string), typeof(Action)])!.Invoke(null, [key, onClick]);
        builder?.GetType().GetMethod("After", [typeof(string)])?.Invoke(builder, ["menu.randomizer"]);
    }

    static void RemovePanel(IPanel panel)
    {
        var unregister = typeof(PanelManager).GetMethod("Unregister", [typeof(IPanel)]);
        if (unregister != null) unregister.Invoke(null, [panel]);
        else if (PanelManager.Panels is List<IPanel> panels) panels.Remove(panel);
    }

    static void OnVSync(VSyncEvent e)
    {
        GameWatch.Tick(e.Memory, e.Frame);
        SeedPlan.Update();
        CheckTracker.Tick(e.Memory, e.Frame);
        ItemGiver.Tick(e.Memory, e.Frame);
        while (ApClient.TryDequeueDeath(out var cause)) GameRules.OnDeathLink(cause);
        GameRules.Tick(e.Memory, e.Frame);
        Fixes.Tick(e.Memory);
        SeedPlan.ApplyResidentFiles(e.Memory);

        while (ApClient.TryDequeueToast(out var toast))
            ToastNotifications.ShowText(toast.Title, toast.Message);
    }
}
