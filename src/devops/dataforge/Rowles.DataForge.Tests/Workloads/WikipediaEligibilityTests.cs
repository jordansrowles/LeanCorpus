using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class WikipediaEligibilityTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accepts_exact_eligible_shape_and_counts_whitespace_tokens()
    {
        var text = string.Concat(Enumerable.Repeat("wordtoken ", 50));
        var result = WikipediaEligibility.Evaluate(0, 17, false, 99, Timestamp, text);

        Assert.True(result.IsEligible);
        Assert.Equal(50, result.TokenCount);
        Assert.Equal(text.TrimEnd(), result.Text);
    }

    [Theory]
    [InlineData(1, false, 0, WikipediaEligibilityRejection.NonMainNamespace)]
    [InlineData(0, true, 0, WikipediaEligibilityRejection.Redirect)]
    [InlineData(0, false, 0, WikipediaEligibilityRejection.MissingRevision)]
    public void Returns_stable_rejection_reasons(int namespaceId, bool redirect, ulong revisionId, WikipediaEligibilityRejection expected)
    {
        var result = WikipediaEligibility.Evaluate(namespaceId, 1, redirect, revisionId, Timestamp, "text");
        Assert.Equal(expected, result.Rejection);
    }

    [Fact]
    public void Enforces_raw_and_normalised_size_and_token_limits()
    {
        Assert.Equal(WikipediaEligibilityRejection.RawTooSmall,
            WikipediaEligibility.Evaluate(0, 1, false, 1, Timestamp, new string('x', 255)).Rejection);
        Assert.Equal(WikipediaEligibilityRejection.TooFewTokens,
            WikipediaEligibility.Evaluate(0, 1, false, 1, Timestamp, new string('x', 300)).Rejection);
        Assert.Equal(WikipediaEligibilityRejection.RawTooLarge,
            WikipediaEligibility.Evaluate(0, 1, false, 1, Timestamp, new string('a', 4 * 1024 * 1024 + 1)).Rejection);
    }

    [Fact]
    public void Rejects_unpaired_surrogates_as_normalisation_failure()
    {
        var result = WikipediaEligibility.Evaluate(0, 1, false, 1, Timestamp, new string('\ud800', 200));
        Assert.Equal(WikipediaEligibilityRejection.NormalisationFailed, result.Rejection);
    }
}
