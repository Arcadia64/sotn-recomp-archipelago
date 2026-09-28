using System;
using System.Collections.Generic;

namespace SotnArchipelago;

static class Log
{
    const int MaxLines = 500;

    static readonly object _gate = new();
    static readonly List<string> _lines = [];

    public static void Info(string message) => Write(message);

    public static void Error(string message) => Write("ERROR: " + message);

    static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Console.WriteLine("[Archipelago] " + message);
        lock (_gate)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines) _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
    }

    public static string[] Snapshot()
    {
        lock (_gate) return _lines.ToArray();
    }

    public static void Clear()
    {
        lock (_gate) _lines.Clear();
    }
}
