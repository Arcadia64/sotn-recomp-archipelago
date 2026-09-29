#nullable enable
using System.Numerics;
using ImGuiNET;

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

    // A section title that folds: an arrow, the title and a thin line after it (like ImGui's SeparatorText,
    // not its filled header bar). Remembers whether it's open (ImGui itself forgets when the game closes).
    public static bool Section(string label, string id, bool defaultOpen)
    {
        string key = "Archipelago.Section." + id;
        var view = RecompOne.Runtime.Runtime.View;
        bool stored = view.GetBool(key, defaultOpen);
        ImGui.SetNextItemOpen(stored, ImGuiCond.Once);

        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, 0x18FFFFFF);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, 0x28FFFFFF);
        bool open = ImGui.TreeNodeEx($"{label}###{id}", ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);
        ImGui.PopStyleColor(2);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        float from = min.X + ImGui.GetTreeNodeToLabelSpacing() + ImGui.CalcTextSize(label).X + ImGui.GetStyle().ItemSpacing.X;
        float y = (min.Y + max.Y) / 2;
        if (from < max.X) ImGui.GetWindowDrawList().AddLine(new Vector2(from, y), new Vector2(max.X, y), ImGui.GetColorU32(ImGuiCol.Separator));

        if (open != stored)
        {
            view.SetBool(key, open);
            RecompOne.Runtime.Runtime.SaveView();
        }
        return open;
    }
}
