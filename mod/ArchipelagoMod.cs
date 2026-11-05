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
          "menu.archipelago.connection": {"en": "Connection"},
          "menu.archipelago.map": {"en": "Map"},
          "menu.archipelago.items": {"en": "Items"},
          "menu.archipelago.messages": {"en": "Text client"},
          "menu.archipelago.options": {"en": "Show window options"},
          "panel.archipelago": {"en": "Archipelago"},
          "panel.archipelago.map": {"en": "Archipelago map"},
          "panel.archipelago.items": {"en": "Archipelago items"},
          "panel.archipelago.messages": {"en": "Archipelago text client"}
        }}
        """;

    readonly ArchipelagoPanel _panel = new();
    readonly MapPanel _map = new();
    readonly ItemsPanel _items = new();
    readonly MessagesPanel _messages = new();
    readonly KeyboardFixes _keyboard = new();
    readonly LayoutKeeper _layout;

    public ArchipelagoMod() => _layout = new LayoutKeeper(() => Array.Exists(Windows, w => w.IsOpen));

    IPanel[] Windows => [_panel, _map, _items, _messages];

    public void OnLoad()
    {
        Log.Info("loading");
        Localization.Merge(Strings);
        PanelManager.Register(_layout); // before the windows: it puts their docked layout back first
        foreach (var window in Windows)
        {
            RestoreOpen(window, defaultOpen: window == _panel);
            PanelManager.Register(window);
        }
        PanelManager.Register(_keyboard);
        ConnectGate.ShowPanel = () => _panel.IsOpen = true;
        _panel.OtherWindows = [("Map", _map), ("Items", _items), ("Text client", _messages)];
        AddMenu();
        Event.AddListener<VSyncEvent>(OnVSync);
        _panel.ConnectOnStart();
    }

    public void OnUnload()
    {
        ApClient.Disconnect();
        ConnectGate.ShowPanel = null;
        Event.RemoveListener<VSyncEvent>(OnVSync);
        MenuRegistry.Remove(MenuKey);
        foreach (var window in Windows) RemovePanel(window);
        RemovePanel(_keyboard);
        RemovePanel(_layout);
        Log.Info("unloaded");
    }

    public void DrawSettings() => _panel.DrawDetails();

    // The menu bar's Archipelago menu: each window on or off.
    // The menu and panel API differs between recomp versions, so call whichever this build has.
    // v0.5.1b: Menu(key, order) places menus by order (Randomizer is 200), and there's no
    // PanelManager.Unregister. Later builds: Menu(key) with After(key), and Unregister exists.
    const int RandomizerOrder = 200;

    void AddMenu()
    {
        var registry = typeof(MenuRegistry);
        var menu = registry.GetMethod("Menu", [typeof(string), typeof(int)]) is { } byOrder
            ? byOrder.Invoke(null, [MenuKey, RandomizerOrder]) as MenuBuilder // same order, added later: just after it
            : registry.GetMethod("Menu", [typeof(string)])?.Invoke(null, [MenuKey]) as MenuBuilder;
        if (menu == null)
        {
            Log.Error("couldn't add the Archipelago menu to this recomp build");
            return;
        }
        typeof(MenuBuilder).GetMethod("After", [typeof(string)])?.Invoke(menu, ["menu.randomizer"]);
        Toggle(menu, "menu.archipelago.connection", _panel);
        Toggle(menu, "menu.archipelago.map", _map);
        Toggle(menu, "menu.archipelago.items", _items);
        Toggle(menu, "menu.archipelago.messages", _messages);
        // Off: the windows show only their content (for streaming).
        menu.Check("menu.archipelago.options", () => UiOptions.Show, show => UiOptions.Show = show);
    }

    static void Toggle(MenuBuilder menu, string key, IPanel panel) => menu.Check(key, () => panel.IsOpen, open => panel.IsOpen = open);

    // Open again if it was open last time (the recomp restores its own windows before mods load). The first
    // time, only the Connection window is open; it has buttons for the others.
    static void RestoreOpen(IPanel panel, bool defaultOpen)
    {
        panel.IsOpen = ModSettings.View.PanelOpen(panel, defaultOpen);
    }

    static void RemovePanel(IPanel panel)
    {
        var unregister = typeof(PanelManager).GetMethod("Unregister", [typeof(IPanel)]);
        if (unregister != null) unregister.Invoke(null, [panel]);
        else if (PanelManager.Panels is List<IPanel> panels) panels.Remove(panel);
    }

    // What runs every frame, in order. If one part fails the others still run, and its error goes in our log once
    // (left to the recomp, the rest of the frame is skipped and the error only reaches the console).
    static readonly (string Name, Action<VSyncEvent> Run)[] FrameParts =
    [
        ("GameWatch", e => GameWatch.Tick(e.Memory, e.Frame)),
        ("SeedPlan.Update", e => SeedPlan.Update()),
        ("CheckTracker", e => CheckTracker.Tick(e.Memory, e.Frame)),
        ("ItemGiver", e => ItemGiver.Tick(e.Memory, e.Frame)),
        ("Traps", e => Traps.Tick(e.Memory, e.Frame)),
        ("TickGoal", e => CheckTracker.TickGoal(e.Memory)),
        ("DeathLink", e => { while (ApClient.TryDequeueDeath(out var cause)) GameRules.OnDeathLink(cause); }),
        ("GameRules", e => GameRules.Tick(e.Memory, e.Frame)),
        ("Fixes", e => Fixes.Tick(e.Memory)),
        ("ApplyResidentFiles", e => SeedPlan.ApplyResidentFiles(e.Memory)),
        ("SeedCache", e => SeedCache.Tick(e.Frame)),
        ("UseForSave", e => SeedCache.UseForSave(e.Memory, e.Frame)),
        ("SyncDra", e => SeedPlan.SyncDra(e.Memory)),
        ("RecompRando", e => RecompRando.Guard(e.Memory)),
        ("PrizeTableSync", e => PrizeTableSync.Tick(e.Memory)),
        ("Toasts", e => { while (ApClient.TryDequeueToast(out var toast)) ToastNotifications.ShowText(toast.Title, toast.Message, null, toast.Seconds); }),
    ];

    static readonly HashSet<string> _failedParts = [];

    static void OnVSync(VSyncEvent e)
    {
        foreach (var (name, run) in FrameParts)
        {
            try
            {
                run(e);
            }
            catch (Exception ex)
            {
                if (_failedParts.Add(name)) Log.Error($"{name} failed: {ex}");
            }
        }
    }
}
