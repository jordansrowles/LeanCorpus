using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class StressProfileTests
{
    [Theory]
    [InlineData("search", 32)]
    [InlineData("vector", 32)]
    [InlineData("hybrid", 32)]
    public void Modes_generate_canonical_prefix_stable_records(string mode, int count)
    {
        var profile = new LeanCorpusStressProfile();
        var parameters = new Dictionary<string, string> { ["mode"] = mode };
        var shortOptions = new DataForgeGenerationOptions(42, count, parameters);
        var longOptions = new DataForgeGenerationOptions(42, count * 2, parameters);
        var shortRecords = profile.Generate(shortOptions).ToArray();
        var longPrefix = profile.Generate(longOptions).Take(count).ToArray();

        Assert.Equal(count, shortRecords.Length);
        Assert.Equal(mode, profile.Summarise(shortOptions).Single(static item => item.Name == "mode").Value);
        for (var index = 0; index < count; index++)
        {
            using var left = new MemoryStream();
            using var right = new MemoryStream();
            using (var writer = new CanonicalJsonWriter(left))
                profile.CanonicalRecordWriter.Write(writer, shortRecords[index]);
            using (var writer = new CanonicalJsonWriter(right))
                profile.CanonicalRecordWriter.Write(writer, longPrefix[index]);
            Assert.Equal(left.ToArray(), right.ToArray());
        }

        if (mode == "search")
        {
            Assert.All(shortRecords, static record =>
            {
                var search = Assert.IsType<SearchRecord>(record.Payload);
                Assert.StartsWith("category-", search.Category, StringComparison.Ordinal);
                Assert.StartsWith("region-", search.Region, StringComparison.Ordinal);
            });
        }
        else if (mode == "vector")
            Assert.All(shortRecords, static record => Assert.Equal(128, Assert.IsType<VectorRecord>(record.Payload).Vector.Length));
        else
            Assert.All(shortRecords, static record => Assert.Equal(64, Assert.IsType<HybridRecord>(record.Payload).Vector.Length));
    }

    [Fact]
    public void Search_stress_length_classes_and_rare_anchor_are_deterministic()
    {
        var profile = new LeanCorpusStressProfile();
        var options = new DataForgeGenerationOptions(42, 1_000,
            new Dictionary<string, string> { ["mode"] = "search" });
        var records = profile.Generate(options).Select(static item => (SearchRecord)item.Payload).ToArray();
        var lengths = records.Select(static record => record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToArray();
        Assert.InRange(lengths.Count(static length => length >= 1_500), 30, 70);
        Assert.InRange(lengths.Count(static length => length is >= 500 and < 1_500), 150, 250);
        Assert.All(records, static record =>
        {
            Assert.InRange(int.Parse(record.Category.AsSpan("category-".Length)), 0, 2_047);
            Assert.InRange(int.Parse(record.Region.AsSpan("region-".Length)), 0, 255);
        });
    }

    [Fact]
    public void Mode_specific_parameters_are_rejected()
    {
        var profile = new LeanCorpusStressProfile();
        var options = new DataForgeGenerationOptions(42, 8,
            new Dictionary<string, string> { ["mode"] = "hybrid", ["regionCardinality"] = "256" });
        Assert.Throws<ArgumentException>(() => profile.Generate(options).ToArray());
    }
}
