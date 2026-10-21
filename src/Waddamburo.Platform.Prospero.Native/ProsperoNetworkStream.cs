using System.Buffers.Binary;
using SharpProspero.Interop.Net;
using SharpProspero.Interop.Sysmodule;
using NativeSocket = SharpProspero.Interop.Net.Socket;

namespace Waddamburo.Platform.Prospero.Native;

/// <summary>A cancellable PS5 TCP/TLS stream, used by the realtime WebSocket transport.</summary>
internal sealed class ProsperoNetworkStream : Stream
{
    private readonly object _lifetime = new();
    private int _pool = -1, _socket = -1, _sslContext = -1, _ssl = -1, _active;
    private bool _disposed;
    private ProsperoTrustRoots? _trust;

    public static Task<ProsperoNetworkStream> ConnectAsync(Uri uri, bool insecure, CancellationToken token)
        => Task.Run(() => Connect(uri, insecure, token), token);

    private static void Check(int value, string operation)
    {
        if (value < 0) throw new IOException($"PS5 {operation} failed (0x{value:x8}).");
    }

    private static unsafe ProsperoNetworkStream Connect(Uri uri, bool insecure, CancellationToken token)
    {
        var stream = new ProsperoNetworkStream();
        try
        {
            Sysmodule.sceSysmoduleLoadModuleInternal(0x80000009);
            NetPool.sceNetInit();
            stream._pool = NetPool.sceNetPoolCreate("waddamburo_ws", 64 * 1024, 0); Check(stream._pool, "network pool");
            uint address = 0;
            int resolver = Resolver.sceNetResolverCreate("waddamburo_ws", stream._pool, 0); Check(resolver, "DNS resolver");
            try
            {
                using var cancelDns = token.Register(() => Resolver.sceNetResolverAbort(resolver, 0));
                token.ThrowIfCancellationRequested();
                Check(Resolver.sceNetResolverStartNtoa(resolver, uri.DnsSafeHost, &address, 5, 1, 0), "DNS resolution");
            }
            finally { Resolver.sceNetResolverDestroy(resolver); }
            token.ThrowIfCancellationRequested();
            stream._socket = NativeSocket.sceNetSocket("waddamburo_ws", NativeSocket.AfInet, NativeSocket.SockStream, 0);
            Check(stream._socket, "TCP socket");
            using var cancelConnect = token.Register(stream.AbortSocket);
            var endpoint = new SceNetSockaddrIn { Len = 16, Family = NativeSocket.AfInet,
                Port = BinaryPrimitives.ReverseEndianness((ushort)uri.Port), Addr = address };
            token.ThrowIfCancellationRequested();
            Check(NativeSocket.sceNetConnect(stream._socket, (SceNetSockaddr*)&endpoint, 16), "TCP connect");
            if (uri.Scheme == "wss")
            {
                Sysmodule.sceSysmoduleLoadModuleInternal(0x80000018);
                stream._sslContext = Ssl.sceSslInit(1024 * 1024); Check(stream._sslContext, "TLS context");
                stream._trust = new ProsperoTrustRoots();
                stream._trust.Load(stream._sslContext, http: false);
                stream._ssl = Ssl.sceSslCreateConnection(stream._sslContext, stream._socket, uri.DnsSafeHost);
                Check(stream._ssl, "TLS connection/SNI");
                const uint verify = Ssl.FlagServerVerify | Ssl.FlagCnCheck | Ssl.FlagKnownCaCheck
                    | Ssl.FlagNotAfterCheck | Ssl.FlagNotBeforeCheck;
                Check(insecure ? Ssl.sceSslDisableVerifyOption(stream._ssl, verify)
                    : Ssl.sceSslEnableVerifyOption(stream._ssl, verify), "TLS verification policy");
                // Do not pass the SDK ordinal enum into the native protocol-version setter.
                Console.Error.WriteLine("PS5 WebSocket TLS protocol: firmware defaults (no SDK version override).");
                Check(Ssl.sceSslConnect(stream._ssl), "TLS handshake");
            }
            token.ThrowIfCancellationRequested();
            return stream;
        }
        catch
        {
            stream.Dispose();
            token.ThrowIfCancellationRequested();
            throw;
        }
    }

    private void AbortSocket()
    {
        lock (_lifetime) if (_socket >= 0) NativeSocket.sceNetSocketAbort(_socket, 0);
    }

    private void BeginIo()
    {
        lock (_lifetime) { ObjectDisposedException.ThrowIf(_disposed, this); _active++; }
    }
    private void EndIo()
    {
        lock (_lifetime) { _active--; if (_disposed && _active == 0) Cleanup(); }
    }
    private void Cleanup()
    {
        if (_ssl >= 0) { Ssl.sceSslDeleteConnection(_ssl); _ssl = -1; }
        if (_sslContext >= 0) { Ssl.sceSslTerm(_sslContext); _sslContext = -1; }
        _trust?.Dispose(); _trust = null;
        if (_socket >= 0) { NativeSocket.sceNetSocketClose(_socket); _socket = -1; }
        if (_pool >= 0) { NetPool.sceNetPoolDestroy(_pool); _pool = -1; }
    }
    protected override void Dispose(bool disposing)
    {
        lock (_lifetime)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_socket >= 0) NativeSocket.sceNetSocketAbort(_socket, 0);
                // Never free a TLS context while a blocked read/write is still using it.
                if (_active == 0) Cleanup();
            }
        }
        base.Dispose(disposing);
    }

    private unsafe int ReadNative(Memory<byte> buffer, CancellationToken token)
    {
        BeginIo();
        try
        {
            using var cancel = token.Register(AbortSocket);
            token.ThrowIfCancellationRequested();
            using var pin = buffer.Pin();
            int read = _ssl >= 0 ? Ssl.sceSslRead(_ssl, pin.Pointer, (nuint)buffer.Length, 1)
                : NativeSocket.sceNetRecv(_socket, pin.Pointer, (nuint)buffer.Length, 0);
            token.ThrowIfCancellationRequested(); Check(read, "socket receive"); return read;
        }
        finally { EndIo(); }
    }
    private unsafe void WriteNative(ReadOnlyMemory<byte> buffer, CancellationToken token)
    {
        BeginIo();
        try
        {
            using var cancel = token.Register(AbortSocket);
            using var pin = buffer.Pin();
            int offset = 0;
            while (offset < buffer.Length)
            {
                token.ThrowIfCancellationRequested();
                int sent = _ssl >= 0 ? Ssl.sceSslWrite(_ssl, (byte*)pin.Pointer + offset, (nuint)(buffer.Length - offset), 0)
                    : NativeSocket.sceNetSend(_socket, (byte*)pin.Pointer + offset, (nuint)(buffer.Length - offset), 0);
                token.ThrowIfCancellationRequested(); Check(sent, "socket send");
                if (sent == 0) throw new IOException("The PS5 socket closed during a write.");
                offset += sent;
            }
        }
        finally { EndIo(); }
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => buffer.IsEmpty ? ValueTask.FromResult(0) : new(Task.Run(() => ReadNative(buffer, cancellationToken), cancellationToken));
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => new(Task.Run(() => WriteNative(buffer, cancellationToken), cancellationToken));
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        => WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
    public override int Read(byte[] buffer, int offset, int count) => ReadNative(buffer.AsMemory(offset, count), default);
    public override void Write(byte[] buffer, int offset, int count) => WriteNative(buffer.AsMemory(offset, count), default);
    public override bool CanRead => !_disposed;
    public override bool CanWrite => !_disposed;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
