using System.Globalization;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class SearchProfileTests
{
    [Fact]
    public void Profile_has_the_locked_identity_record_shape_and_ids()
    {
        var profile = new LeanCorpusSearchProfile();
        Assert.Equal("leancorpus-search", profile.Descriptor.ProfileId);
        Assert.Equal(1, profile.Descriptor.ProfileVersion);
        Assert.Equal(42UL, profile.Descriptor.DefaultSeed);
        Assert.Equal(20_000, profile.Descriptor.DefaultCount);
        Assert.Equal(
            [
                nameof(SearchRecord.Ordinal), nameof(SearchRecord.Id), nameof(SearchRecord.Locale), nameof(SearchRecord.Archetype),
                nameof(SearchRecord.Title), nameof(SearchRecord.Body), nameof(SearchRecord.Category), nameof(SearchRecord.Region),
                nameof(SearchRecord.PersonName), nameof(SearchRecord.Company), nameof(SearchRecord.Email), nameof(SearchRecord.Url),
                nameof(SearchRecord.TimestampUtc), nameof(SearchRecord.PriceMinor), nameof(SearchRecord.Active),
                nameof(SearchRecord.LatitudeE6), nameof(SearchRecord.LongitudeE6)
            ],
            typeof(SearchRecord).GetProperties().Select(static property => property.Name).ToArray());

        var records = profile.Generate(new DataForgeGenerationOptions(42, 3)).ToArray();
        Assert.Equal(["search-00000000", "search-00000001", "search-00000002"], records.Select(static record => record.Id).ToArray());
        Assert.Equal([0L, 1L, 2L], records.Select(static record => record.Ordinal).ToArray());
    }

    [Fact]
    public void Increasing_record_count_preserves_the_earlier_canonical_prefix()
    {
        var profile = new LeanCorpusSearchProfile();
        var prefix1k = WritePrefix(profile, new DataForgeGenerationOptions(42, 1_000), 1_000);
        var prefix10k = WritePrefix(profile, new DataForgeGenerationOptions(42, 10_000), 1_000);
        var prefix20k = WritePrefix(profile, new DataForgeGenerationOptions(42, 20_000), 1_000);

        Assert.Equal(prefix1k, prefix10k);
        Assert.Equal(prefix1k, prefix20k);

        Assert.Equal(GetRecord(profile, 1_000, 0), GetRecord(profile, 10_000, 0));
        Assert.Equal(GetRecord(profile, 1_000, 999), GetRecord(profile, 10_000, 999));
        Assert.Equal(GetRecord(profile, 10_000, 9_999), GetRecord(profile, 20_000, 9_999));
    }

    [Fact]
    public void Every_pinned_Bogus_locale_is_available_and_reproducible()
    {
        foreach (var locale in LeanCorpusSearchProfile.LocaleIds)
        {
            var first = DataForgeFakerFactory.Create(locale, 123);
            var second = DataForgeFakerFactory.Create(locale, 123);
            Assert.Equal(first.FirstName(), second.FirstName());
            Assert.Equal(first.City(), second.City());
        }
    }

    [Fact]
    public void Current_culture_does_not_change_search_record_or_canonical_bytes()
    {
        var profile = new LeanCorpusSearchProfile();
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            var frenchCultureHash = WritePrefix(profile, new DataForgeGenerationOptions(42, 250), 250);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var britishCultureHash = WritePrefix(profile, new DataForgeGenerationOptions(42, 250), 250);

            Assert.Equal(frenchCultureHash, britishCultureHash);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
        }
    }

    [Fact]
    public void Phrase_anchor_blocks_remain_contiguous_and_reserved_no_hit_is_not_generated()
    {
        var profile = new LeanCorpusSearchProfile();
        var bodies = profile.Generate(new DataForgeGenerationOptions(42, 20_000)).Select(static record => record.Body).ToArray();

        Assert.Contains(bodies, static body => body.Contains("new york", StringComparison.Ordinal));
        Assert.Contains(bodies, static body => body.Contains("new york stock", StringComparison.Ordinal));
        Assert.Contains(bodies, static body => body.Contains("said public government", StringComparison.Ordinal));
        Assert.DoesNotContain(bodies, static body => body.Contains("zzzznomatch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Small_fixtures_contain_every_suggester_original()
    {
        var profile = new LeanCorpusSearchProfile();
        var records = profile.Generate(new DataForgeGenerationOptions(42, 20)).ToArray();
        var bodies = records.SelectMany(static record => record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var original in new[]
                 {
                     "government", "president", "market", "company", "million", "financial", "reported", "political", "economic", "hospital",
                     "computer", "network", "message", "article", "because", "people", "national", "through", "without", "believe"
                 })
        {
            Assert.Contains(original, bodies);
        }
    }

    [Fact]
    public void Repeated_generation_has_the_same_canonical_hash()
    {
        var profile = new LeanCorpusSearchProfile();
        var options = new DataForgeGenerationOptions(42, 1_000);
        Assert.Equal(WritePrefix(profile, options, 1_000), WritePrefix(profile, options, 1_000));
    }

    private static string WritePrefix(LeanCorpusSearchProfile profile, DataForgeGenerationOptions options, int count)
    {
        using var stream = new MemoryStream();
        using var writer = new CanonicalJsonWriter(stream);
        var records = profile.Generate(options).Take(count);
        foreach (var record in records)
        {
            profile.CanonicalRecordWriter.Write(writer, record);
            writer.WriteLine();
        }
        return Convert.ToHexString(writer.GetSha256()).ToLowerInvariant();
    }

    private static SearchRecord GetRecord(LeanCorpusSearchProfile profile, int count, int ordinal) =>
        profile.Generate(new DataForgeGenerationOptions(42, count)).ElementAt(ordinal);
}
