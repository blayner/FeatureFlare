using System.Text.Json;
using FeatureFlare.Sdk.Hashing;

namespace FeatureFlare.Sdk.Tests;

/// <summary>Same vectors as the server and the TypeScript SDK (spec/hash-vectors.json).</summary>
public sealed class HashVectorTests
{
    private static readonly JsonElement Vectors =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "hash-vectors.json"))).RootElement;

    [Fact]
    public void Murmur3_matches_every_shared_vector()
    {
        foreach (var v in Vectors.GetProperty("hashVectors").EnumerateArray())
        {
            var input = v.GetProperty("input").GetString()!;
            MurmurHash3.Hash32(input, v.GetProperty("seed").GetUInt32())
                .ShouldBe(v.GetProperty("hash").GetUInt32(), $"input '{input}'");
        }
    }

    [Fact]
    public void Buckets_match_every_shared_vector()
    {
        foreach (var v in Vectors.GetProperty("bucketVectors").EnumerateArray())
        {
            Bucketing.Bucket(v.GetProperty("key").GetString()!, v.GetProperty("salt").GetString()!, v.GetProperty("value").GetString()!)
                .ShouldBe(v.GetProperty("bucket").GetInt32());
        }
    }
}
