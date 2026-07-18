namespace FeatureFlare.Application.Evaluation;

/// <summary>
/// Deterministic assignment of a value to one of <see cref="Scale"/> buckets.
/// <para>
/// <c>bucket = murmur3_x86_32(utf8("{key}.{salt}.{value}"), seed: 0) % 10000</c>
/// </para>
/// The same formula is implemented by every SDK, so a user lands in the same bucket no matter where the
/// flag is evaluated.
/// </summary>
public static class Bucketing
{
    public const int Scale = 10_000;

    public static int Bucket(string key, string salt, string value) =>
        (int)(MurmurHash3.Hash32($"{key}.{salt}.{value}") % Scale);

    /// <summary>
    /// Walks the cumulative weights and returns the index of the slot that contains <paramref name="bucket"/>.
    /// If weights sum to less than <see cref="Scale"/> the remainder falls into the last slot.
    /// </summary>
    public static int Pick<T>(IReadOnlyList<T> slots, Func<T, int> weight, int bucket)
    {
        if (slots.Count == 0)
        {
            return -1;
        }

        var cumulative = 0;
        for (var i = 0; i < slots.Count; i++)
        {
            cumulative += weight(slots[i]);
            if (bucket < cumulative)
            {
                return i;
            }
        }

        return slots.Count - 1;
    }
}
