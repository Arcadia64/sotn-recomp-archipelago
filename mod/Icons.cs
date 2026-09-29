#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SotnArchipelago;

// Item icons from the recomp's own tracker (Recompiled.TrackerIcons, 287 of them, relics and items), by item
// name: "Soul of bat" is SoulOfBat.png. Called by reflection so the mod still loads if a build lacks them.
static class Icons
{
    static MethodInfo? _tryGet;
    static bool _looked;
    static readonly Dictionary<string, uint> _textures = [];

    // The icon's texture, or 0 if there isn't one.
    public static uint Get(string itemName)
    {
        if (_textures.TryGetValue(itemName, out uint texture)) return texture;
        texture = 0;
        try
        {
            var method = Method();
            if (method != null)
            {
                object?[] args = [itemName, 0u];
                if (method.Invoke(null, args) is true) texture = (uint)args[1]!;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"icon for {itemName}: {ex.Message}");
        }
        _textures[itemName] = texture;
        return texture;
    }

    static MethodInfo? Method()
    {
        if (_looked) return _tryGet;
        _looked = true;
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Recompiled.TrackerIcons")).FirstOrDefault(t => t != null);
        _tryGet = type?.GetMethod("TryGetTexture", BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(uint).MakeByRefType()]);
        if (_tryGet == null) Log.Info("the recomp's item icons weren't found; the item tracker shows names instead");
        return _tryGet;
    }
}
