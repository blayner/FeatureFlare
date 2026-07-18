using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace FeatureFlare.Application.Evaluation;

/// <summary>
/// MurmurHash3 x86 32-bit. Chosen for bucketing because it is fast, well distributed and trivial to
/// implement identically in every SDK language. Strings are always hashed as UTF-8.
/// Must stay byte-for-byte compatible with sdks/typescript/src/murmur3.ts and the .NET SDK
/// (see spec/hash-vectors.json).
/// </summary>
public static class MurmurHash3
{
    private const uint C1 = 0xcc9e2d51;
    private const uint C2 = 0x1b873593;

    public static uint Hash32(string input, uint seed = 0)
    {
        var maxBytes = Encoding.UTF8.GetMaxByteCount(input.Length);
        if (maxBytes <= 256)
        {
            Span<byte> stackBuffer = stackalloc byte[maxBytes];
            var written = Encoding.UTF8.GetBytes(input, stackBuffer);
            return Hash32(stackBuffer[..written], seed);
        }

        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);
        try
        {
            var written = Encoding.UTF8.GetBytes(input, rented);
            return Hash32(rented.AsSpan(0, written), seed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static uint Hash32(ReadOnlySpan<byte> data, uint seed = 0)
    {
        var h1 = seed;
        var length = data.Length;
        var blocks = length / 4;

        for (var i = 0; i < blocks; i++)
        {
            var k1 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i * 4, 4));
            k1 *= C1;
            k1 = BitOperations.RotateLeft(k1, 15);
            k1 *= C2;

            h1 ^= k1;
            h1 = BitOperations.RotateLeft(h1, 13);
            h1 = (h1 * 5u) + 0xe6546b64u;
        }

        var tail = data[(blocks * 4)..];
        uint k = 0;
        if (tail.Length > 0)
        {
            if (tail.Length == 3)
            {
                k ^= (uint)tail[2] << 16;
            }

            if (tail.Length >= 2)
            {
                k ^= (uint)tail[1] << 8;
            }

            k ^= tail[0];
            k *= C1;
            k = BitOperations.RotateLeft(k, 15);
            k *= C2;
            h1 ^= k;
        }

        h1 ^= (uint)length;
        return FMix(h1);
    }

    private static uint FMix(uint h)
    {
        h ^= h >> 16;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        return h;
    }
}
