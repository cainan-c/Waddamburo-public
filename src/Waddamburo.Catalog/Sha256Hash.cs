using System.Security.Cryptography;

namespace Waddamburo.Catalog;

/// <summary>SHA-256 for catalog identities and application cache keys, using the selected platform provider.</summary>
public static class Sha256Hash
{
    private static Func<byte[], byte[]> _provider = static bytes => SHA256.HashData(bytes);

    public static void SetProvider(Func<byte[], byte[]> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Volatile.Write(ref _provider, provider);
    }

    public static byte[] Compute(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var digest = Volatile.Read(ref _provider)(bytes);
        if (digest.Length != 32)
            throw new InvalidOperationException("The SHA-256 provider must return a 32-byte digest.");
        return digest;
    }
}
