#nullable enable
using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace SotnArchipelago;

// Typing in the recomp's windows (ours and its own). A panel with no window, always "open", so the recomp
// calls it every frame.
//
// Ctrl+V / C / X / A / Z and Shift+arrow selection: the recomp's ImGui backend (Silk.NET's ImGuiController
// 2.22) sends every key with io.AddKeyEvent, but Ctrl, Shift, Alt and Super only as the old io.KeyCtrl-style
// flags. ImGui 1.90 works the modifiers out from the ImGuiMod_* keys instead once a backend uses AddKeyEvent,
// and nothing sends those, so to ImGui a modifier is never held: Ctrl+V is just "v". This sends them, from
// the left/right keys the backend does send; ImGui takes them at the start of the next frame.
//
// The game doesn't read the keyboard while a text box has it: the recomp passes every key to the game too,
// so typing a message would move Alucard or open the menu. While one does, the game's pads read as idle.
sealed class KeyboardFixes : IFloatingPanel
{
    public string Name => "Archipelago keyboard fixes";
    public bool IsOpen { get => true; set { } }

    static volatile bool _typing;

    public void Draw()
    {
        var io = ImGui.GetIO();
        Send(io, ImGuiKey.ModCtrl, ImGuiKey.LeftCtrl, ImGuiKey.RightCtrl);
        Send(io, ImGuiKey.ModShift, ImGuiKey.LeftShift, ImGuiKey.RightShift);
        Send(io, ImGuiKey.ModAlt, ImGuiKey.LeftAlt, ImGuiKey.RightAlt);
        Send(io, ImGuiKey.ModSuper, ImGuiKey.LeftSuper, ImGuiKey.RightSuper);
        _typing = io.WantTextInput;
    }

    // ImGui drops an event that repeats the key's current state, so sending every frame is fine.
    static void Send(ImGuiIOPtr io, ImGuiKey mod, ImGuiKey left, ImGuiKey right) =>
        io.AddKeyEvent(mod, ImGui.IsKeyDown(left) || ImGui.IsKeyDown(right));

    // g_pads: two pads of { pressed, previous, tapped, repeat } (u16 each), filled by ReadPads.
    const uint PadsAddr = 0x80097490, PadsBytes = 16;

    [PostHook("dra", "ReadPads")]
    static void AfterReadPads(CpuContext c, IMemory m)
    {
        if (!_typing) return;
        for (uint i = 0; i < PadsBytes; i += 4) m.WriteU32(PadsAddr + i, 0);
    }
}
