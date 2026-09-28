using System;
using System.Collections.Generic;
using System.Numerics;

namespace SotnArchipelago;

static class Log
{
    const int MaxLines = 500;

    public readonly record struct Line(string Text, Vector4? Colour);

    static readonly object _gate = new();
    static readonly List<Line> _lines = [];

    public static void Info(string message, Vector4? colour = null) => Write(message, colour);

    public static void Error(string message) => Write("ERROR: " + message, new Vector4(1f, 0.45f, 0.4f, 1f));

    // An item message in the colour Archipelago clients use for its classification.
    public static void Item(string message, NetworkItem item) => Write(message, ItemClass.Colour(item));

    static void Write(string message, Vector4? colour)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine("[Archipelago] " + message);
        lock (_gate)
        {
            _lines.Add(new Line(line, colour));
            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
    }

    public static Line[] Snapshot()
    {
        lock (_gate) return _lines.ToArray();
    }

    public static void Clear()
    {
        lock (_gate) _lines.Clear();
    }
}

// Item colours, matching the in-game AP badge (ApLook): gold progression, blue useful, grey
// filler, red trap. Archipelago's own plum/slate blue/cyan were too close on a small badge.
static class ItemClass
{
    public static string Name(NetworkItem item) =>
        item.Progression ? "Progression" : item.Trap ? "Trap" : item.Useful ? "Useful" : "Filler";

    public static Vector4 Colour(NetworkItem item) =>
        item.Progression ? Rgb(0xF0, 0xB0, 0x20) : item.Trap ? Rgb(0xE0, 0x40, 0x40)
        : item.Useful ? Rgb(0x5A, 0x8C, 0xF5) : Rgb(0xB4, 0xB4, 0xB4);

    static Vector4 Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
