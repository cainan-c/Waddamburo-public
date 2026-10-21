namespace Waddamburo.Catalog;

/// <summary>A user-supplied asset key whose diagnostic representation never exposes its bytes.</summary>
public sealed class NijiiroDecryptionKey : IEquatable<NijiiroDecryptionKey>
{
    private readonly byte[] _bytes;
    private NijiiroDecryptionKey(byte[] bytes) => _bytes = bytes;
    public static NijiiroDecryptionKey Parse(string hex)
    {
        if (hex.Length != 64 || hex.Any(static c => !char.IsAsciiHexDigit(c)))
            throw new FormatException("A Nijiiro key must contain exactly 64 hexadecimal characters.");
        return new(Convert.FromHexString(hex));
    }
    public byte[] ToArray() => (byte[])_bytes.Clone();
    public bool Equals(NijiiroDecryptionKey? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    public override bool Equals(object? other) => other is NijiiroDecryptionKey key && Equals(key);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte value in _bytes) hash.Add(value);
        return hash.ToHashCode();
    }
    public override string ToString() => "[configured]";
}
