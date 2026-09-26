using System.Text.Json;
using FeatureFlare.Application.Evaluation;

namespace FeatureFlare.Domain.Tests;

/// <summary>
/// spec/hash-vectors.json is shared with the TypeScript and .NET SDK test suites. If these tests pass in
/// every implementation, users land in the same bucket regardless of where a flag is evaluated.
/// </summary>
public sealed class HashVectorTests
{
    private static readonly JsonDocument Vectors =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hash-vectors.json")));

    public static TheoryData<string, uint, uint> HashCases()
    {
        var data = new TheoryData<string, uint, uint>();
        foreach (var v in Vectors.RootElement.GetProperty("hashVectors").EnumerateArray())
        {
            data.Add(v.GetProperty("input").GetString()!, v.GetProperty("seed").GetUInt32(), v.GetProperty("hash").GetUInt32());
        }

        return data;
    }

    public static TheoryData<string, string, string, int> BucketCases()
    {
        var data = new TheoryData<string, string, string, int>();
        foreach (var v in Vectors.RootElement.GetProperty("bucketVectors").EnumerateArray())
        {
            data.Add(
                v.GetProperty("key").GetString()!,
                v.GetProperty("salt").GetString()!,
                v.GetProperty("value").GetString()!,
                v.GetProperty("bucket").GetInt32());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(HashCases))]
    public void Murmur3_matches_shared_vectors(string input, uint seed, uint expected) =>
        MurmurHash3.Hash32(input, seed).ShouldBe(expected);

    [Theory]
    [MemberData(nameof(BucketCases))]
    public void Bucket_matches_shared_vectors(string key, string salt, string value, int expected) =>
        Bucketing.Bucket(key, salt, value).ShouldBe(expected);

    [Fact]
    public void Long_inputs_use_pooled_buffer_and_still_match_span_overload()
    {
        var input = new string('x', 1_000) + "ç";
        MurmurHash3.Hash32(input).ShouldBe(MurmurHash3.Hash32(System.Text.Encoding.UTF8.GetBytes(input)));
    }

    [Fact]
    public void Pick_walks_cumulative_weights_and_sends_remainder_to_last_slot()
    {
        int[] weights = [2_500, 2_500, 4_000];

        Bucketing.Pick(weights, w => w, 0).ShouldBe(0);
        Bucketing.Pick(weights, w => w, 2_499).ShouldBe(0);
        Bucketing.Pick(weights, w => w, 2_500).ShouldBe(1);
        Bucketing.Pick(weights, w => w, 8_999).ShouldBe(2);
        Bucketing.Pick(weights, w => w, 9_999).ShouldBe(2); // remainder
    }
}
