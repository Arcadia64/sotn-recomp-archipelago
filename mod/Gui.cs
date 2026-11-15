#nullable enable
using System.Runtime.InteropServices;

namespace SotnArchipelago;

// Inside the namespace on purpose: names from here are found before the stand-ins in Compat.cs.
using Hexa.NET.ImGui;

// The ImGui calls that are written differently for ImGui.NET (SymphonyRecomp up to v1.1) and Hexa.NET.ImGui
// (from v1.2), in a form that compiles against either.
static unsafe class Gui
{
    // A texture as AddImage takes it: the texture's number itself in ImGui.NET, an ImTextureRef around it in Hexa.
    public static ImTextureRef Tex(uint texture) => new ImTextureRef(null, (ImTextureID)(nint)texture);

    // ImGui's settings text: where each window is and what it's docked to.
    public static string SaveIni() => Text(ImGui.SaveIniSettingsToMemory());

    static string Text(string text) => text;                                      // ImGui.NET hands back a string
    static string Text(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? "";  // Hexa hands back ImGui's buffer
}
