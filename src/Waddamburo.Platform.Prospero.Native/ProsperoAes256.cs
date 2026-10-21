using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using HardwareAes = System.Runtime.Intrinsics.X86.Aes;

namespace Waddamburo.Platform.Prospero.Native;

/// <summary>AES-256-CBC/PKCS7 for local Nijiiro assets; no platform crypto library.</summary>
public static class ProsperoAes256
{
    private static readonly byte[] Sbox = MakeSbox();
    private static readonly byte[] Inverse = MakeInverse();
    private static byte Multiply(byte a, byte b)
    {
        int x = a, y = b, result = 0;
        for (int i = 0; i < 8; i++)
        {
            if ((y & 1) != 0) result ^= x;
            x = ((x << 1) ^ ((x & 0x80) != 0 ? 0x11b : 0)) & 255;
            y >>= 1;
        }
        return (byte)result;
    }
    private static byte[] MakeSbox()
    {
        var table = new byte[256];
        for (int value = 0; value < 256; value++)
        {
            byte inverse = 0;
            if (value != 0)
            {
                byte result = 1, factor = (byte)value;
                for (int power = 254; power != 0; power >>= 1)
                { if ((power & 1) != 0) result = Multiply(result, factor); factor = Multiply(factor, factor); }
                inverse = result;
            }
            int transformed = inverse ^ 0x63;
            for (int shift = 1; shift <= 4; shift++) transformed ^= ((inverse << shift) | (inverse >> (8 - shift))) & 255;
            table[value] = (byte)transformed;
        }
        return table;
    }
    private static byte[] MakeInverse()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++) table[Sbox[i]] = (byte)i;
        return table;
    }

    public static byte[] DecryptCbc(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext)
        => DecryptCore(key, iv, ciphertext, HardwareAes.IsSupported && Sse2.IsSupported);

    internal static byte[] DecryptCbcSoftware(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext)
        => DecryptCore(key, iv, ciphertext, false);

    private static byte[] DecryptCore(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, ReadOnlySpan<byte> ciphertext, bool hardware)
    {
        if (key.Length != 32 || iv.Length != 16 || ciphertext.IsEmpty || ciphertext.Length % 16 != 0)
            throw new CryptographicException("Invalid AES-256-CBC asset dimensions.");
        byte[] expanded = new byte[240]; key.CopyTo(expanded);
        Span<byte> word = stackalloc byte[4]; byte rcon = 1;
        for (int at = 32; at < expanded.Length;)
        {
            expanded.AsSpan(at - 4, 4).CopyTo(word);
            if (at % 32 == 0)
            {
                byte first = word[0]; word[0] = (byte)(Sbox[word[1]] ^ rcon);
                word[1] = Sbox[word[2]]; word[2] = Sbox[word[3]]; word[3] = Sbox[first];
                rcon = Multiply(rcon, 2);
            }
            else if (at % 32 == 16) for (int i = 0; i < 4; i++) word[i] = Sbox[word[i]];
            for (int i = 0; i < 4; i++, at++) expanded[at] = (byte)(expanded[at - 32] ^ word[i]);
        }
        var output = ciphertext.ToArray();
        Span<byte> shifted = stackalloc byte[16];
        try
        {
            for (int offset = 0; offset < output.Length; offset += 16)
            {
                if (hardware)
                {
                    var block = Sse2.Xor(Vector128.LoadUnsafe(ref output[offset]), Vector128.LoadUnsafe(ref expanded[224]));
                    for (int round = 13; round > 0; round--)
                        block = HardwareAes.Decrypt(block, HardwareAes.InverseMixColumns(Vector128.LoadUnsafe(ref expanded[round * 16])));
                    block = HardwareAes.DecryptLast(block, Vector128.LoadUnsafe(ref expanded[0]));
                    ReadOnlySpan<byte> chain = offset == 0 ? iv : ciphertext.Slice(offset - 16, 16);
                    block = Sse2.Xor(block, Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(chain)));
                    block.StoreUnsafe(ref output[offset]);
                    continue;
                }
                Span<byte> state = output.AsSpan(offset, 16);
                for (int i = 0; i < 16; i++) state[i] ^= expanded[224 + i];
                for (int round = 13; round >= 0; round--)
                {
                    state.CopyTo(shifted);
                    for (int column = 0; column < 4; column++)
                        for (int row = 0; row < 4; row++)
                            state[column * 4 + row] = (byte)(Inverse[shifted[((column - row + 4) & 3) * 4 + row]] ^ expanded[round * 16 + column * 4 + row]);
                    if (round == 0) continue;
                    for (int column = 0; column < 4; column++)
                    {
                        int at = column * 4;
                        byte a = state[at], b = state[at + 1], c = state[at + 2], d = state[at + 3];
                        state[at] = (byte)(Multiply(a, 14) ^ Multiply(b, 11) ^ Multiply(c, 13) ^ Multiply(d, 9));
                        state[at + 1] = (byte)(Multiply(a, 9) ^ Multiply(b, 14) ^ Multiply(c, 11) ^ Multiply(d, 13));
                        state[at + 2] = (byte)(Multiply(a, 13) ^ Multiply(b, 9) ^ Multiply(c, 14) ^ Multiply(d, 11));
                        state[at + 3] = (byte)(Multiply(a, 11) ^ Multiply(b, 13) ^ Multiply(c, 9) ^ Multiply(d, 14));
                    }
                }
                ReadOnlySpan<byte> previous = offset == 0 ? iv : ciphertext.Slice(offset - 16, 16);
                for (int i = 0; i < 16; i++) state[i] ^= previous[i];
            }
            int padding = output[^1];
            if (padding is < 1 or > 16) throw new CryptographicException("Invalid AES asset padding.");
            for (int i = 1; i <= padding; i++)
                if (output[^i] != padding) throw new CryptographicException("Invalid AES asset padding.");
            Array.Resize(ref output, output.Length - padding);
            return output;
        }
        catch { CryptographicOperations.ZeroMemory(output); throw; }
        finally { CryptographicOperations.ZeroMemory(expanded); word.Clear(); shifted.Clear(); }
    }
}
