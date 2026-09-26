using System.Text.Json;
using FeatureFlare.Application.Evaluation;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Domain.Tests;

public sealed class ClauseMatcherTests
{
    [Theory]
    [InlineData(ClauseOperator.In, "BR", "BR", true)]
    [InlineData(ClauseOperator.In, "br", "BR", false)]
    [InlineData(ClauseOperator.Contains, "ana@acme.com", "@acme", true)]
    [InlineData(ClauseOperator.StartsWith, "ana@acme.com", "ana", true)]
    [InlineData(ClauseOperator.EndsWith, "ana@acme.com", ".com", true)]
    [InlineData(ClauseOperator.Matches, "ana@acme.com", "^[a-z]+@acme\\.com$", true)]
    [InlineData(ClauseOperator.Matches, "ana@acme.com", "(invalid", false)]
    [InlineData(ClauseOperator.LessThan, "9", "10", true)]
    [InlineData(ClauseOperator.LessThanOrEqual, "10", "10", true)]
    [InlineData(ClauseOperator.GreaterThan, "10.5", "10", true)]
    [InlineData(ClauseOperator.GreaterThanOrEqual, "abc", "10", false)]
    [InlineData(ClauseOperator.SemVerEqual, "2", "2.0.0", true)]
    [InlineData(ClauseOperator.SemVerLessThan, "2.0.0-beta.2", "2.0.0", true)]
    [InlineData(ClauseOperator.SemVerGreaterThan, "2.0.0-beta.11", "2.0.0-beta.2", true)]
    [InlineData(ClauseOperator.SemVerGreaterThan, "not-a-version", "1.0.0", false)]
    public void Operators(ClauseOperator op, string actual, string expected, bool matches) =>
        ClauseMatcher.MatchValue(op, actual, expected).ShouldBe(matches);

    [Fact]
    public void Missing_attribute_never_matches_even_when_negated()
    {
        var clause = new Clause("country", ClauseOperator.In, ["BR"], Negate: true);

        ClauseMatcher.Matches(clause, TestData.Context("u1")).ShouldBeFalse();
        ClauseMatcher.Matches(clause, TestData.Context("u1", ("country", "US"))).ShouldBeTrue();
        ClauseMatcher.Matches(clause, TestData.Context("u1", ("country", "BR"))).ShouldBeFalse();
    }

    [Fact]
    public void Array_attributes_match_when_any_element_matches()
    {
        var clause = TestData.Clause("roles", ClauseOperator.In, "admin");

        ClauseMatcher.Matches(clause, TestData.Context("u1", ("roles", new[] { "viewer", "admin" }))).ShouldBeTrue();
    }

    [Fact]
    public void Json_attributes_are_normalised_like_the_sdks()
    {
        var attributes = JsonDocument.Parse("""{ "beta": true, "seats": 2.0, "tags": ["a", 1] }""").RootElement;
        var context = TestData.Context(
            "u1",
            ("beta", attributes.GetProperty("beta")),
            ("seats", attributes.GetProperty("seats")),
            ("tags", attributes.GetProperty("tags")));

        ClauseMatcher.Matches(TestData.Clause("beta", ClauseOperator.In, "true"), context).ShouldBeTrue();
        ClauseMatcher.Matches(TestData.Clause("seats", ClauseOperator.In, "2"), context).ShouldBeTrue();
        ClauseMatcher.Matches(TestData.Clause("tags", ClauseOperator.In, "1"), context).ShouldBeTrue();
    }

    [Fact]
    public void Key_attribute_refers_to_the_context_key()
    {
        ClauseMatcher.Matches(TestData.Clause("key", ClauseOperator.In, "u1", "u2"), TestData.Context("u2")).ShouldBeTrue();
    }
}
