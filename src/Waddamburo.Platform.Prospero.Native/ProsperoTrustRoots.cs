using System.Runtime.InteropServices;
using System.Text;
using SharpProspero.Interop.Net;
using NativeHttp = SharpProspero.Interop.Net.Http;

namespace Waddamburo.Platform.Prospero.Native;

/// <summary>Owns the native PEM records and pointer table until their HTTP/TLS context terminates.</summary>
internal sealed unsafe class ProsperoTrustRoots : IDisposable
{
    private static readonly Lazy<byte[][]> Roots = new(ReadRoots);
    private void* _allocation;
    private SceSslData** _pointers;
    private readonly int _count;

    private static byte[][] ReadRoots()
    {
        using var stream = typeof(ProsperoTrustRoots).Assembly.GetManifestResourceStream("Waddamburo.CaRoots.pem")
            ?? throw new IOException("The console CA root bundle is missing.");
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var pem = reader.ReadToEnd();
        const string begin = "-----BEGIN CERTIFICATE-----", end = "-----END CERTIFICATE-----";
        var roots = new List<byte[]>();
        int at = 0;
        while ((at = pem.IndexOf(begin, at, StringComparison.Ordinal)) >= 0)
        {
            at += begin.Length;
            int stop = pem.IndexOf(end, at, StringComparison.Ordinal);
            if (stop < 0) throw new InvalidDataException("Invalid console CA root bundle.");
            // Validate the embedded payload, then keep the complete PEM framing required by
            // sceHttpsLoadCert/sceSslLoadCert. Raw DER is rejected with 0x8095f005.
            string base64 = Convert.ToBase64String(Convert.FromBase64String(pem[at..stop]));
            var record = new StringBuilder().Append(begin).Append("\r\n");
            for (int line = 0; line < base64.Length; line += 64)
                record.Append(base64, line, Math.Min(64, base64.Length - line)).Append("\r\n");
            record.Append(end).Append("\r\n");
            roots.Add(Encoding.ASCII.GetBytes(record.ToString()));
            at = stop + end.Length;
        }
        if (roots.Count == 0) throw new InvalidDataException("The console CA root bundle is empty.");
        return roots.ToArray();
    }

    public ProsperoTrustRoots()
    {
        try
        {
            var roots = Roots.Value;
            nuint descriptorBytes = checked((nuint)roots.Length * (nuint)sizeof(SceSslData));
            nuint tableBytes = checked((nuint)roots.Length * (nuint)sizeof(SceSslData*));
            nuint total = checked(descriptorBytes + tableBytes);
            foreach (byte[] root in roots) total = checked(total + (nuint)root.Length + 1);
            _allocation = NativeMemory.AllocZeroed(total);
            if (_allocation == null) throw new OutOfMemoryException();
            var certificates = (SceSslData*)_allocation;
            var pointers = (SceSslData**)((byte*)_allocation + descriptorBytes);
            byte* data = (byte*)_allocation + descriptorBytes + tableBytes;
            for (int i = 0; i < roots.Length; i++)
            {
                byte[] root = roots[i];
                root.AsSpan().CopyTo(new Span<byte>(data, root.Length));
                certificates[i] = new SceSslData { Ptr = data, Size = (nuint)root.Length };
                pointers[i] = &certificates[i];
                data += root.Length + 1; // NUL padding is outside the declared PEM size.
            }
            _pointers = pointers;
            _count = roots.Length;
        }
        catch { Dispose(); throw; }
    }

    public void Load(int context, bool http)
    {
        ObjectDisposedException.ThrowIf(_allocation == null, this);
        // Caller holds this owner before the native call, including when import partially
        // succeeds and then fails. Storage must outlive context teardown on that path too.
        int result = http ? NativeHttp.sceHttpsLoadCert(context, _count, _pointers, null, null)
            : Ssl.sceSslLoadCert(context, _count, _pointers, null, null);
        if (result < 0) throw new IOException($"PS5 PEM CA root import failed (0x{result:x8}; {_count} roots).");
        Console.Error.WriteLine($"PS5 {(http ? "HTTP" : "WebSocket TLS")} imported {_count} PEM CA roots.");
    }

    public void Dispose()
    {
        NativeMemory.Free(_allocation);
        _allocation = null;
        _pointers = null;
    }
}
