using System.Globalization;
using System.Text.RegularExpressions;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class SearchProfileQualificationTests
{
    [Fact]
    public void Seed42_twenty_thousand_record_profile_meets_locked_distribution_and_search_bands()
    {
        var profile = new LeanCorpusSearchProfile();
        var records = profile.Generate(new DataForgeGenerationOptions(42, 20_000)).ToArray();
        Assert.Equal(20_000, records.Length);

        var lengths = records.Select(static record => record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToArray();
        AssertShare(lengths.Count(static length => length <= 40), records.Length, 0.13, 0.17, "short length");
        AssertShare(lengths.Count(static length => length is >= 50 and <= 150), records.Length, 0.67, 0.73, "medium length");
        AssertShare(lengths.Count(static length => length is > 150 and <= 500), records.Length, 0.12, 0.16, "long length");
        AssertShare(lengths.Count(static length => length > 500), records.Length, 0.005, 0.015, "very-long length");

        var documentFrequencies = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["said"] = 0,
            ["government"] = 0,
            ["people"] = 0,
            ["market"] = 0,
            ["quasarneedle"] = 0,
            ["zzzznomatch"] = 0
        };
        var vocabulary = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var tokens = Tokenise(record.Body);
            foreach (var token in tokens)
                vocabulary.Add(token);
            foreach (var term in documentFrequencies.Keys.ToArray())
            {
                if (tokens.Contains(term, StringComparer.Ordinal))
                    documentFrequencies[term]++;
            }
        }

        AssertShare(documentFrequencies["said"], records.Length, 0.30, 0.42, "said DF");
        AssertShare(documentFrequencies["government"], records.Length, 0.12, 0.22, "government DF");
        AssertShare(documentFrequencies["people"], records.Length, 0.05, 0.10, "people DF");
        AssertShare(documentFrequencies["market"], records.Length, 0.015, 0.05, "market DF");
        AssertShare(documentFrequencies["quasarneedle"], records.Length, 0.0003, 0.003, "quasarneedle DF");
        Assert.Equal(0, documentFrequencies["zzzznomatch"]);

        Assert.Contains(records, static record => record.Body.Contains("new york", StringComparison.Ordinal));
        Assert.Contains(records, static record => record.Body.Contains("new york stock", StringComparison.Ordinal));
        Assert.Contains(records, static record => record.Body.Contains("said public government", StringComparison.Ordinal));
        Assert.True(records.Count(static record => record.Body.Contains("new york", StringComparison.Ordinal)) >=
                    records.Count(static record => record.Body.Contains("new york stock", StringComparison.Ordinal)));
        Assert.Equal(32, records.Select(static record => record.Category).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(16, records.Select(static record => record.Region).Distinct(StringComparer.Ordinal).Count());

        if (string.Equals(Environment.GetEnvironmentVariable("DATAFORGE_QUALIFICATION_REPORT"), "1", StringComparison.Ordinal))
        {
            using var canonicalWriter = new CanonicalJsonWriter(Stream.Null);
            foreach (var record in records)
            {
                profile.CanonicalRecordWriter.Write(canonicalWriter, record);
                canonicalWriter.WriteLine();
            }
            var contentSha256 = Convert.ToHexString(canonicalWriter.GetSha256()).ToLowerInvariant();
            var shortCount = lengths.Count(static length => length <= 40);
            var mediumCount = lengths.Count(static length => length is >= 50 and <= 150);
            var longCount = lengths.Count(static length => length is > 150 and <= 500);
            var veryLongCount = lengths.Count(static length => length > 500);
            Console.WriteLine(string.Join(", ",
                $"contentSha256={contentSha256}",
                $"logicalBytes={canonicalWriter.BytesWritten.ToString(CultureInfo.InvariantCulture)}",
                $"short={FormatShare(shortCount, records.Length)}",
                $"medium={FormatShare(mediumCount, records.Length)}",
                $"long={FormatShare(longCount, records.Length)}",
                $"very-long={FormatShare(veryLongCount, records.Length)}",
                $"said={FormatShare(documentFrequencies["said"], records.Length)}",
                $"government={FormatShare(documentFrequencies["government"], records.Length)}",
                $"people={FormatShare(documentFrequencies["people"], records.Length)}",
                $"market={FormatShare(documentFrequencies["market"], records.Length)}",
                $"quasarneedle={FormatShare(documentFrequencies["quasarneedle"], records.Length)}",
                $"zzzznomatch={documentFrequencies["zzzznomatch"]}",
                $"categories={records.Select(static record => record.Category).Distinct(StringComparer.Ordinal).Count()}",
                $"regions={records.Select(static record => record.Region).Distinct(StringComparer.Ordinal).Count()}"));
        }

        foreach (var token in LeanCorpusSearchProfile.RequiredLexicalTokens)
            Assert.Contains(token, vocabulary);

        AssertDistribution(records, static record => record.Locale, new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["en_GB"] = 8_200, ["en"] = 800, ["fr"] = 300, ["de"] = 250, ["es"] = 200,
            ["nl"] = 100, ["pt_BR"] = 50, ["ru"] = 50, ["ja"] = 25, ["ar"] = 25
        }, 0.25, minimumExpected: 1);
        AssertDistribution(records, static record => record.Archetype, new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["article"] = 3_000, ["product"] = 2_000, ["technical"] = 1_500,
            ["forum"] = 1_500, ["contact"] = 1_000, ["business"] = 1_000
        }, 0.10, minimumExpected: 0);

        var start = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var record in records)
        {
            Assert.InRange(record.PriceMinor, 100, 999_900);
            Assert.InRange(record.LatitudeE6, -90_000_000, 90_000_000);
            Assert.InRange(record.LongitudeE6, -180_000_000, 180_000_000);
            var timestamp = DateTime.ParseExact(record.TimestampUtc, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            Assert.InRange(timestamp, start, end.AddTicks(-1));
            Assert.Equal($"search-{record.Ordinal:D8}", record.Id);
        }
    }

    private static void AssertDistribution(
        IReadOnlyList<SearchRecord> records,
        Func<SearchRecord, string> selector,
        IReadOnlyDictionary<string, int> weights,
        double relativeTolerance,
        int minimumExpected)
    {
        var counts = records.GroupBy(selector, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        foreach (var pair in weights)
        {
            var expected = records.Count * pair.Value / 10_000d;
            var minimum = Math.Max(minimumExpected, (int)Math.Floor(expected * (1 - relativeTolerance)));
            var maximum = (int)Math.Ceiling(expected * (1 + relativeTolerance));
            Assert.True(counts.TryGetValue(pair.Key, out var actual), $"Missing distribution value '{pair.Key}'.");
            Assert.InRange(actual, minimum, maximum);
        }
    }

    private static void AssertShare(int actual, int total, double minimum, double maximum, string label)
    {
        var share = (double)actual / total;
        Assert.True(share >= minimum && share <= maximum, $"{label} was {share:P3}; expected {minimum:P1} to {maximum:P1}.");
    }

    private static string FormatShare(int actual, int total)
        => string.Concat(actual.ToString(CultureInfo.InvariantCulture), "/", total.ToString(CultureInfo.InvariantCulture), " (",
            (actual * 100d / total).ToString("F3", CultureInfo.InvariantCulture), "%)");

    private static string[] Tokenise(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)
            .Select(static match => match.Value)
            .ToArray();
}
