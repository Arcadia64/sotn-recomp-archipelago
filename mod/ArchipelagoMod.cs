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
        MenuRegistry.BarItem(MenuKey, TogglePanel).After("menu.randomizer");
        Event.AddListener<VSyncEvent>(OnVSync);
    }

    public void OnUnload()
    {
        ApClient.Disconnect();
        Event.RemoveListener<VSyncEvent>(OnVSync);
        MenuRegistry.Remove(MenuKey);
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
        Fixes.Tick(e.Memory);
        SeedPlan.ApplyResidentFiles(e.Memory);

        while (ApClient.TryDequeueToast(out var toast))
            ToastNotifications.ShowText(toast.Title, toast.Message);
    }
}
