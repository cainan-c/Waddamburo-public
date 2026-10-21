using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using SharpProspero.Numerics;
using SharpProspero.Security;

namespace Waddamburo.Platform.Prospero.Native;

/// <summary>RFC 6455 over the PS5 TCP/TLS stream; frame masks use PS5 hardware entropy.</summary>
public sealed class ProsperoWebSocket : WebSocket
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _send = new(1, 1), _receive = new(1, 1);
    private WebSocketState _state = WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeDescription;
    private int _remaining;
    private bool _final, _fragmented;
    private WebSocketMessageType _type;
    private const int MaxFrame = 1024 * 1024;
    internal ProsperoWebSocket(Stream stream) => _stream = stream;

    public static async Task<WebSocket> ConnectAsync(Uri uri, bool insecure, CancellationToken token)
    {
        if (uri.Scheme is not ("ws" or "wss") || uri.UserInfo.Length != 0)
            throw new WebSocketException("Only ws/wss URLs without embedded credentials are supported.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stream = await ProsperoNetworkStream.ConnectAsync(uri, insecure, timeout.Token).ConfigureAwait(false);
        try
        {
            byte[] nonce = new byte[16]; HardwareEntropy.Fill(nonce);
            string key = Convert.ToBase64String(nonce);
            string host = uri.IsDefaultPort ? uri.DnsSafeHost : uri.Authority;
            string path = uri.PathAndQuery.Length == 0 ? "/" : uri.PathAndQuery;
            var request = Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n");
            await stream.WriteAsync(request, timeout.Token).ConfigureAwait(false);
            using var headers = new MemoryStream();
            byte[] one = new byte[1]; int tail = 0;
            while (headers.Length < 32 * 1024)
            {
                if (await stream.ReadAsync(one, timeout.Token).ConfigureAwait(false) == 0)
                    throw new WebSocketException("The server closed during the WebSocket handshake.");
                headers.WriteByte(one[0]); tail = (tail << 8) | one[0];
                if (tail == 0x0d0a0d0a) break;
            }
            if (tail != 0x0d0a0d0a) throw new WebSocketException("WebSocket response headers exceed the limit.");
            ValidateHandshake(Encoding.ASCII.GetString(headers.ToArray()), key);
            return new ProsperoWebSocket(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    internal static void ValidateHandshake(string headers, string key)
    {
        var lines = headers.Split("\r\n");
        if (!lines[0].StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal))
            throw new WebSocketException("The server refused the WebSocket upgrade: " + lines[0]);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0)
            {
                string name = line[..colon].Trim();
                if (!fields.TryAdd(name, line[(colon + 1)..].Trim()))
                    throw new WebSocketException("Duplicate WebSocket handshake header: " + name);
            }
        }
        string accept = Convert.ToBase64String(Sha1.Hash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        if (!fields.TryGetValue("Sec-WebSocket-Accept", out string? actual) || actual != accept
            || !fields.TryGetValue("Upgrade", out string? upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase)
            || !fields.TryGetValue("Connection", out string? connection)
            || !connection.Split(',').Any(part => part.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
            || fields.ContainsKey("Sec-WebSocket-Extensions") || fields.ContainsKey("Sec-WebSocket-Protocol"))
            throw new WebSocketException("Invalid WebSocket upgrade response.");
    }

    public override WebSocketState State => _state;
    public override WebSocketCloseStatus? CloseStatus => _closeStatus;
    public override string? CloseStatusDescription => _closeDescription;
    public override string? SubProtocol => null;
    public override void Abort() { _state = WebSocketState.Aborted; _stream.Dispose(); }
    public override void Dispose() { _state = WebSocketState.Closed; _stream.Dispose(); }

    private async Task FrameAsync(byte opcode, ReadOnlyMemory<byte> payload, bool final, CancellationToken token)
    {
        if (payload.Length > MaxFrame) throw new WebSocketException("WebSocket frame exceeds the console limit.");
        await _send.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_state is WebSocketState.Aborted or WebSocketState.Closed) throw new WebSocketException("WebSocket is closed.");
            int head = payload.Length < 126 ? 2 : payload.Length <= ushort.MaxValue ? 4 : 10;
            var frame = new byte[head + 4 + payload.Length];
            frame[0] = (byte)(opcode | (final ? 0x80 : 0));
            frame[1] = (byte)(0x80 | (head == 2 ? payload.Length : head == 4 ? 126 : 127));
            if (head == 4) BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)payload.Length);
            if (head == 10) BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(2), (ulong)payload.Length);
            HardwareEntropy.Fill(frame.AsSpan(head, 4));
            payload.Span.CopyTo(frame.AsSpan(head + 4));
            for (int i = 0; i < payload.Length; i++) frame[head + 4 + i] ^= frame[head + (i & 3)];
            await _stream.WriteAsync(frame, token).ConfigureAwait(false);
        }
        finally { _send.Release(); }
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
    {
        if (type is not (WebSocketMessageType.Text or WebSocketMessageType.Binary) || !end)
            throw new NotSupportedException("Console realtime sends complete text/binary messages.");
        return FrameAsync(type == WebSocketMessageType.Text ? (byte)1 : (byte)2, buffer.AsMemory(), true, token);
    }

    private async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken token)
    {
        int at = 0;
        while (at < buffer.Length)
        {
            int read = await _stream.ReadAsync(buffer[at..], token).ConfigureAwait(false);
            if (read == 0) { _state = WebSocketState.Aborted; throw new WebSocketException("WebSocket transport closed without a close frame."); }
            at += read;
        }
    }

    public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        await _receive.WaitAsync(token).ConfigureAwait(false);
        try
        {
            while (_remaining == 0)
            {
                byte[] header = new byte[10];
                await ReadExactlyAsync(header.AsMemory(0, 2), token).ConfigureAwait(false);
                if ((header[0] & 0x70) != 0 || (header[1] & 0x80) != 0)
                    throw new WebSocketException("Invalid flags or masked server frame.");
                byte opcode = (byte)(header[0] & 15);
                bool final = (header[0] & 0x80) != 0;
                ulong length = (uint)(header[1] & 127);
                if (length == 126) { await ReadExactlyAsync(header.AsMemory(0, 2), token).ConfigureAwait(false); length = BinaryPrimitives.ReadUInt16BigEndian(header); }
                else if (length == 127) { await ReadExactlyAsync(header.AsMemory(0, 8), token).ConfigureAwait(false); length = BinaryPrimitives.ReadUInt64BigEndian(header); }
                if (length > MaxFrame) throw new WebSocketException("WebSocket frame exceeds the console limit.");
                if (opcode >= 8)
                {
                    if (!final || length > 125 || opcode is not (8 or 9 or 10)) throw new WebSocketException("Invalid control frame.");
                    var control = new byte[(int)length]; await ReadExactlyAsync(control, token).ConfigureAwait(false);
                    if (opcode == 9) { await FrameAsync(10, control, true, token).ConfigureAwait(false); continue; }
                    if (opcode == 10) continue;
                    if (length == 1) throw new WebSocketException("Invalid close frame.");
                    _closeStatus = length >= 2 ? (WebSocketCloseStatus)BinaryPrimitives.ReadUInt16BigEndian(control) : WebSocketCloseStatus.Empty;
                    _closeDescription = length > 2 ? Encoding.UTF8.GetString(control.AsSpan(2)) : null;
                    if (_state == WebSocketState.Open) await FrameAsync(8, control, true, token).ConfigureAwait(false);
                    _state = WebSocketState.Closed;
                    return new ValueWebSocketReceiveResult(0, WebSocketMessageType.Close, true);
                }
                if (opcode == 0 ? !_fragmented : _fragmented || opcode is not (1 or 2))
                    throw new WebSocketException("Invalid data-frame continuation.");
                if (opcode != 0) _type = opcode == 1 ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
                _remaining = (int)length; _final = final; _fragmented = !final;
                if (_remaining == 0) return new ValueWebSocketReceiveResult(0, _type, _final);
            }
            int count = Math.Min(buffer.Length, _remaining);
            await ReadExactlyAsync(buffer[..count], token).ConfigureAwait(false);
            _remaining -= count;
            return new ValueWebSocketReceiveResult(count, _type, _remaining == 0 && _final);
        }
        finally { _receive.Release(); }
    }
    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
    {
        var result = await ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
        return new WebSocketReceiveResult(result.Count, result.MessageType, result.EndOfMessage, _closeStatus, _closeDescription);
    }
    public override async Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token)
    {
        byte[] text = Encoding.UTF8.GetBytes(description ?? "");
        if (text.Length > 123) throw new ArgumentException("Close description exceeds 123 bytes.", nameof(description));
        byte[] body = new byte[text.Length + 2]; BinaryPrimitives.WriteUInt16BigEndian(body, (ushort)status); text.CopyTo(body, 2);
        await FrameAsync(8, body, true, token).ConfigureAwait(false); _state = WebSocketState.CloseSent;
    }
    public override async Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token)
    {
        await CloseOutputAsync(status, description, token).ConfigureAwait(false);
        byte[] buffer = new byte[4096];
        while (_state == WebSocketState.CloseSent) await ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
    }
}
