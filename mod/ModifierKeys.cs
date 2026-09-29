#nullable enable
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace SotnArchipelago;

// Makes Ctrl+V / C / X / A / Z and Shift+arrow selection work in text boxes. The recomp's ImGui backend
// (Silk.NET's ImGuiController 2.22) sends every key with io.AddKeyEvent, but Ctrl, Shift, Alt and Super only
// as the old io.KeyCtrl-style flags. ImGui 1.90 works the modifiers out from the ImGuiMod_* keys instead once
// a backend uses AddKeyEvent, and nothing sends those, so to ImGui a modifier is never held: Ctrl+V is just
// "v". This sends them the way ImGui expects, from the left/right keys the backend does send. It's a panel
// with no window, always "open", so the recomp calls it every frame; ImGui takes the events at the start of
// the next frame. It applies to every window, the recomp's own included.
sealed class ModifierKeys : IFloatingPanel
{
    public string Name => "Archipelago keyboard modifiers";
    public bool IsOpen { get => true; set { } }

    public void Draw()
    {
        var io = ImGui.GetIO();
        Send(io, ImGuiKey.ModCtrl, ImGuiKey.LeftCtrl, ImGuiKey.RightCtrl);
        Send(io, ImGuiKey.ModShift, ImGuiKey.LeftShift, ImGuiKey.RightShift);
        Send(io, ImGuiKey.ModAlt, ImGuiKey.LeftAlt, ImGuiKey.RightAlt);
        Send(io, ImGuiKey.ModSuper, ImGuiKey.LeftSuper, ImGuiKey.RightSuper);
    }

    // ImGui drops an event that repeats the key's current state, so sending every frame is fine.
    static void Send(ImGuiIOPtr io, ImGuiKey mod, ImGuiKey left, ImGuiKey right) =>
        io.AddKeyEvent(mod, ImGui.IsKeyDown(left) || ImGui.IsKeyDown(right));
}
