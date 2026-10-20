using System.Buffers.Binary;
using SharpProspero.Security;

namespace Waddamburo.Platform.Prospero.Native;

internal sealed class ShaderDiskCache(string root)
{
    private const int HeaderSize = 44, MaxSize = 64 * 1024 * 1024;
    private readonly object _gate = new();
    private string FileName(ulong id) => Path.Combine(root, id.ToString("x16") + ".bin");
    public uint ReadSize(ulong id)
    {
        lock (_gate)
        {
            try
            {
                using var f = File.OpenRead(FileName(id));
                Span<byte> h = stackalloc byte[HeaderSize]; f.ReadExactly(h);
                int size = BinaryPrimitives.ReadInt32LittleEndian(h[8..]);
                return h[..8].SequenceEqual("WADSHD01"u8) && size > 0 && size <= MaxSize && f.Length == HeaderSize + size ? (uint)size : 0;
            }
            catch (Exception) { return 0; }
        }
    }
    public bool TryRead(ulong id, Span<byte> destination)
    {
        lock (_gate)
        {
            try
            {
                if (destination.Length <= 0 || destination.Length > MaxSize) return false;
                using var f = File.OpenRead(FileName(id));
                Span<byte> h = stackalloc byte[HeaderSize]; f.ReadExactly(h);
                if (!h[..8].SequenceEqual("WADSHD01"u8) || BinaryPrimitives.ReadInt32LittleEndian(h[8..]) != destination.Length || f.Length != HeaderSize + destination.Length) return false;
                f.ReadExactly(destination);
                return Sha256.Hash(destination).AsSpan().SequenceEqual(h[12..]);
            }
            catch (Exception) { return false; }
        }
    }
    public void Write(ulong id, ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            try
            {
                if (data.IsEmpty || data.Length > MaxSize) return;
                Directory.CreateDirectory(root);
                using var f = new FileStream(FileName(id), FileMode.Create, FileAccess.Write, FileShare.None);
                Span<byte> h = stackalloc byte[HeaderSize]; "WADSHD01"u8.CopyTo(h);
                BinaryPrimitives.WriteInt32LittleEndian(h[8..], data.Length);
                Sha256.Hash(data).CopyTo(h[12..]);
                f.Write(h); f.Write(data); f.Flush(flushToDisk: true);
            }
            catch (Exception error) { Console.Error.WriteLine("Shader cache save failed: " + error.Message); }
        }
    }
}
