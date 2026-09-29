#nullable enable

namespace SotnArchipelago;

// "Show window options" in the Archipelago menu: off, the map, items and messages windows show only their
// content (no checkboxes, filters or sliders), for a clean look on stream.
static class UiOptions
{
    const string Key = "Archipelago.ShowWindowOptions";
    static bool? _show;

    public static bool Show
    {
        get => _show ??= RecompOne.Runtime.Runtime.View.GetBool(Key, true);
        set
        {
            _show = value;
            RecompOne.Runtime.Runtime.View.SetBool(Key, value);
            RecompOne.Runtime.Runtime.SaveView();
        }
    }

    // A section header that remembers whether it's open (ImGui itself forgets when the game closes).
    public static bool Section(string label, string id, bool defaultOpen)
    {
        string key = "Archipelago.Section." + id;
        var view = RecompOne.Runtime.Runtime.View;
        bool stored = view.GetBool(key, defaultOpen);
        ImGuiNET.ImGui.SetNextItemOpen(stored, ImGuiNET.ImGuiCond.Once);
        bool open = ImGuiNET.ImGui.CollapsingHeader($"{label}###{id}");
        if (open != stored)
        {
            view.SetBool(key, open);
            RecompOne.Runtime.Runtime.SaveView();
        }
        return open;
    }
}
