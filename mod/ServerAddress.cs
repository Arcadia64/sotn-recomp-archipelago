#nullable enable
using System;

namespace SotnArchipelago;

// What's in the Server box, as the addresses to try.
static class ServerAddress
{
    const string ConnectCommand = "/connect "; // how a room's page gives its address
    const string DefaultPort = ":38281";

    // Null if it can't be a server address. With no scheme, secure first, like the official client.
    public static Uri[]? Parse(string server)
    {
        string text = server.Trim();
        if (text.StartsWith(ConnectCommand, StringComparison.OrdinalIgnoreCase)) text = text[ConnectCommand.Length..].Trim();
        if (text.Contains("://")) return Uris(text);
        if (!text.Contains(':')) text += DefaultPort;
        return Uris("wss://" + text, "ws://" + text);
    }

    static Uri[]? Uris(params string[] texts)
    {
        var uris = new Uri[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            if (!Uri.TryCreate(texts[i], UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.Scheme is not ("ws" or "wss")) return null;
            uris[i] = uri;
        }
        return uris;
    }
}
