// One source for every SymphonyRecomp version. The game compiles the mod when it starts, against whatever it ships:
// up to v1.1 that is ImGui.NET (namespace ImGuiNET) with the panels in RecompOne.Runtime.Host.Window; from v1.2 it is
// Hexa.NET.ImGui with the panels in RecompOne.UI. A using for a namespace that doesn't exist is a compile error, so
// all four are declared here (empty) and imported everywhere; the names then come from whichever the game has.
global using ImGuiNET;
global using Hexa.NET.ImGui;
global using RecompOne.Runtime.Host.Window;
global using RecompOne.UI;

namespace ImGuiNET { }
namespace Hexa.NET.ImGui { }
namespace RecompOne.Runtime.Host.Window { }
namespace RecompOne.UI { }

// Stand-ins for ImGui.NET, where a texture is passed as a plain number. Only Gui.cs names them, and there Hexa's
// own types of the same names are found first when the game has them.
readonly struct ImTextureID
{
    public readonly nint Value;
    ImTextureID(nint value) => Value = value;
    public static explicit operator ImTextureID(nint value) => new(value);
}

readonly unsafe struct ImTextureRef
{
    readonly nint _id;
    public ImTextureRef(void* data, ImTextureID id) => _id = id.Value;
    public static implicit operator nint(ImTextureRef texture) => texture._id;
}
