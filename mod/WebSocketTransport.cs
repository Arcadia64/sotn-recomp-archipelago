#nullable enable
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SotnArchipelago;

// Mods are compiled only against assemblies the game has already loaded, and the
// WebSocket client isn't one of them. It ships with every .NET runtime, so load it
// by name here and call it through reflection. Only text frames are used.
sealed class WebSocketTransport : IDisposable
{
    const int ReceiveChunk = 64 * 1024;

    static Type? _clientType;
    static Type? _messageType;
    static Type? _closeStatus;
    static MethodInfo? _connect;
    static MethodInfo? _send;
    static MethodInfo? _receive;
    static MethodInfo? _close;
    static PropertyInfo? _state;
    static Type? _deflateOptions;

    readonly object _socket;
    readonly SemaphoreSlim _sendLock = new(1, 1);

    WebSocketTransport(object socket) => _socket = socket;

    static void EnsureTypes()
    {
        if (_clientType != null) return;

        var client = Assembly.Load(new AssemblyName("System.Net.WebSockets.Client"));
        var core = Assembly.Load(new AssemblyName("System.Net.WebSockets"));

        var clientType = client.GetType("System.Net.WebSockets.ClientWebSocket", throwOnError: true)!;
        _messageType = core.GetType("System.Net.WebSockets.WebSocketMessageType", throwOnError: true)!;
        _closeStatus = core.GetType("System.Net.WebSockets.WebSocketCloseStatus", throwOnError: true)!;

        _connect = clientType.GetMethod("ConnectAsync", [typeof(Uri), typeof(CancellationToken)]);
        _send = clientType.GetMethod("SendAsync", [typeof(ArraySegment<byte>), _messageType, typeof(bool), typeof(CancellationToken)]);
        _receive = clientType.GetMethod("ReceiveAsync", [typeof(ArraySegment<byte>), typeof(CancellationToken)]);
        _close = clientType.GetMethod("CloseAsync", [_closeStatus, typeof(string), typeof(CancellationToken)]);
        _state = clientType.GetProperty("State");
        _deflateOptions = core.GetType("System.Net.WebSockets.WebSocketDeflateOptions");

        if (_connect == null || _send == null || _receive == null || _close == null || _state == null)
            throw new InvalidOperationException("ClientWebSocket is missing an expected method");

        _clientType = clientType;
    }

    public static async Task<WebSocketTransport> ConnectAsync(Uri uri, CancellationToken ct)
    {
        EnsureTypes();
        var socket = Activator.CreateInstance(_clientType!)!;
        EnableCompression(socket);
        try
        {
            await Invoke(_connect!, socket, uri, ct);
        }
        catch
        {
            (socket as IDisposable)?.Dispose();
            throw;
        }
        return new WebSocketTransport(socket);
    }

    // permessage-deflate, which Archipelago servers ask clients to support. Optional: if this .NET
    // lacks it the connection still works uncompressed.
    static void EnableCompression(object socket)
    {
        try
        {
            if (_deflateOptions == null) return;
            var options = _clientType!.GetProperty("Options")!.GetValue(socket)!;
            var prop = options.GetType().GetProperty("DangerousDeflateOptions");
            prop?.SetValue(options, Activator.CreateInstance(_deflateOptions));
        }
        catch (Exception ex)
        {
            Log.Info($"compression not available: {ex.Message}");
        }
    }

    // "Open" while connected; anything else means the connection is gone.
    public string State => _state!.GetValue(_socket)?.ToString() ?? "Unknown";

    public bool IsOpen => State == "Open";

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var textType = Enum.ToObject(_messageType!, 0); // WebSocketMessageType.Text
        await _sendLock.WaitAsync(ct);
        try
        {
            await Invoke(_send!, _socket, new ArraySegment<byte>(bytes), textType, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // Returns one whole text message, or null when the server closes the connection.
    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[ReceiveChunk];
        using var message = new MemoryStream();
        while (true)
        {
            var task = Invoke(_receive!, _socket, new ArraySegment<byte>(buffer), ct);
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            var type = result.GetType();
            int count = (int)type.GetProperty("Count")!.GetValue(result)!;
            bool end = (bool)type.GetProperty("EndOfMessage")!.GetValue(result)!;
            string kind = type.GetProperty("MessageType")!.GetValue(result)!.ToString()!;

            if (kind == "Close") return null;
            message.Write(buffer, 0, count);
            if (end) return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
        }
    }

    public async Task CloseAsync()
    {
        if (!IsOpen) return;
        try
        {
            var normal = Enum.ToObject(_closeStatus!, 1000); // WebSocketCloseStatus.NormalClosure
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Invoke(_close!, _socket, normal, "", timeout.Token);
        }
        catch
        {
            // Closing is best effort; Dispose below drops the connection anyway.
        }
    }

    public void Dispose() => (_socket as IDisposable)?.Dispose();

    static Task Invoke(MethodInfo method, object target, params object[] args)
    {
        try
        {
            return (Task)method.Invoke(target, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }
}
