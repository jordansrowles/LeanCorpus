using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class E2eProfileTests
{
    [Fact]
    public void Reserved_records_have_fixed_ordinals_ids_and_scenarios()
    {
        var profile = new LeanCorpusE2eProfile();
        var records = profile.Generate(new DataForgeGenerationOptions(42, 256)).ToArray();
        Assert.Equal(256, records.Length);
        Assert.Equal(new[]
        {
            "e2e-exact", "e2e-phrase", "e2e-filter", "e2e-unicode", "e2e-long",
            "e2e-product", "e2e-contact", "e2e-technical"
        }, records.Take(8).Select(static record => record.Id));
        Assert.Equal(Enumerable.Range(0, 256).Select(static ordinal => (long)ordinal),
            records.Select(static record => record.Ordinal));
        Assert.Contains("forgeexactanchor", records[0].Body, StringComparison.Ordinal);
        Assert.Equal("scenario-exact", records[0].Category);
        Assert.Contains("deterministic corpus replay", records[1].Body, StringComparison.Ordinal);
        Assert.Equal("scenario-phrase", records[1].Category);
        Assert.Contains("filtered document", records[2].Body, StringComparison.Ordinal);
        Assert.True(records[2].Active);
        Assert.Equal("scenario-filter", records[2].Category);
        Assert.Contains("café naïve Ελληνικά 日本語 العربية", records[3].Body, StringComparison.Ordinal);
        Assert.Equal("scenario-unicode", records[3].Category);
        Assert.True(records[4].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 1_000);
        Assert.Equal("scenario-long", records[4].Category);
        Assert.Equal("product", records[5].Category);
        Assert.True(records[5].PriceMinor > 0);
        Assert.Equal("contact", records[6].Category);
        Assert.Contains("async await span memory indexing", records[7].Body, StringComparison.Ordinal);
        Assert.Equal("technical", records[7].Category);
        Assert.All(records.Skip(1), static record => Assert.DoesNotContain("forgeexactanchor", record.Body, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Background_records_are_prefix_stable()
    {
        var profile = new LeanCorpusE2eProfile();
        var first = profile.Generate(new DataForgeGenerationOptions(42, 32)).ToArray();
        var prefix = profile.Generate(new DataForgeGenerationOptions(42, 256)).Take(32).ToArray();
        Assert.Equal(first, prefix);
        Assert.Equal("e2e-00000008", first[8].Id);
    }

    [Fact]
    public void Minimum_count_and_parameter_contract_are_enforced()
    {
        var profile = new LeanCorpusE2eProfile();
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Generate(new DataForgeGenerationOptions(42, 31)).ToArray());
        Assert.Throws<ArgumentException>(() => profile.Generate(new DataForgeGenerationOptions(42, 32,
            new Dictionary<string, string> { ["unexpected"] = "value" })).ToArray());
    }
}
