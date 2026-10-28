using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class VectorProfileTests
{
    [Theory]
    [InlineData(VectorDistribution.Uniform)]
    [InlineData(VectorDistribution.Clustered)]
    [InlineData(VectorDistribution.DenseNeighbourhood)]
    [InlineData(VectorDistribution.NearDuplicate)]
    [InlineData(VectorDistribution.QuantisationFriendly)]
    [InlineData(VectorDistribution.QuantisationHostile)]
    public void All_distributions_have_stable_prefixes_and_finite_non_zero_vectors(VectorDistribution distribution)
    {
        var profile = new LeanCorpusVectorProfile();
        var small = Options(32, distribution);
        var large = Options(64, distribution);
        var first = profile.Generate(small).ToArray();
        var longer = profile.Generate(large).Take(32).ToArray();

        Assert.Equal(first.Select(static item => item.Id), longer.Select(static item => item.Id));
        Assert.Equal(first.SelectMany(static item => item.Vector), longer.SelectMany(static item => item.Vector));
        Assert.Equal(first.SelectMany(static item => item.Vector),
            profile.Generate(small).SelectMany(static item => item.Vector));

        foreach (var record in first)
        {
            Assert.Equal($"vector-{record.Ordinal:D8}", record.Id);
            Assert.Equal(64, record.Vector.Length);
            Assert.Contains(record.Vector, static component => component != 0);
            Assert.All(record.Vector, static component => Assert.True(float.IsFinite(component) && component >= -1f && component <= 1f));
            Assert.StartsWith("vector-category-", record.Category);
            Assert.NotEmpty(record.AccessGroup);
        }
    }

    [Fact]
    public void Query_stream_is_independent_and_ground_truth_filters_before_scoring()
    {
        var profile = new LeanCorpusVectorProfile();
        var options = Options(128, VectorDistribution.Uniform);
        var records = profile.Generate(options).ToArray();
        var query = profile.GenerateQuery(options, 0);
        Assert.DoesNotContain(records, record => record.Vector.SequenceEqual(query.Vector));

        var full = VectorGroundTruth.Compute(records, query, 10);
        var filtered = VectorGroundTruth.Compute(records, query, 10, static ordinal => ordinal % 2 == 0);
        Assert.Equal("cosine", full.Metric);
        Assert.Equal(10, full.NeighbourOrdinals.Length);
        Assert.Equal(10, filtered.NeighbourOrdinals.Length);
        Assert.All(filtered.NeighbourOrdinals, static ordinal => Assert.Equal(0, ordinal % 2));
        Assert.Equal(full.NeighbourOrdinals, VectorGroundTruth.Compute(records, query, 10).NeighbourOrdinals);
    }

    [Fact]
    public void Near_duplicate_group_changes_exactly_one_component()
    {
        var profile = new LeanCorpusVectorProfile();
        var records = profile.Generate(Options(32, VectorDistribution.NearDuplicate)).ToArray();
        for (var member = 1; member < 16; member++)
        {
            var changed = Enumerable.Range(0, 64)
                .Where(dimension => records[0].Vector[dimension] != records[member].Vector[dimension])
                .ToArray();
            Assert.Equal([member], changed);
        }
    }

    [Fact]
    public void Canonical_vector_bits_repeat_and_parameters_are_validated()
    {
        var profile = new LeanCorpusVectorProfile();
        var options = Options(16, VectorDistribution.Uniform);
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        var firstSummary = DataForgeMaterialiser.GenerateToStream(profile, options, first);
        var secondSummary = DataForgeMaterialiser.GenerateToStream(profile, options, second);
        Assert.Equal(firstSummary.ContentSha256, secondSummary.ContentSha256);
        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Generate(
            new DataForgeGenerationOptions(42, 2, new Dictionary<string, string> { ["dimension"] = "1" })).ToArray());
    }

    private static DataForgeGenerationOptions Options(int count, VectorDistribution distribution) =>
        new(42, count, new Dictionary<string, string>
        {
            ["dimension"] = "64",
            ["vectorDistribution"] = distribution.ToString(),
            ["clusterCount"] = "8",
            ["queryCount"] = "1"
        });
}
