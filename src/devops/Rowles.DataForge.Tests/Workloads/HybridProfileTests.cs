using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class HybridProfileTests
{
    [Fact]
    public void Seed42_twenty_thousand_records_qualify_topics_correlations_and_filters()
    {
        var profile = new LeanCorpusHybridProfile();
        var options = new DataForgeGenerationOptions(42, 20_000);
        var records = profile.Generate(options).ToArray();
        Assert.Equal(20_000, records.Length);
        Assert.Equal(8, LeanCorpusHybridProfile.Topics.Count);

        foreach (var topic in LeanCorpusHybridProfile.Topics)
            Assert.InRange(records.Count(record => record.Topic == topic), 2_200, 2_800);

        var aligned = 0;
        var neutral = 0;
        var conflict = 0;
        foreach (var record in records)
        {
            var textTopics = LeanCorpusHybridProfile.Topics
                .Where(topic => record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(topic))
                .ToArray();
            var correlation = profile.Classify(42, record.Ordinal);
            switch (correlation)
            {
                case HybridCorrelation.Aligned:
                    aligned++;
                    Assert.Equal([record.Topic], textTopics);
                    break;
                case HybridCorrelation.Neutral:
                    neutral++;
                    Assert.Empty(textTopics);
                    break;
                case HybridCorrelation.Conflict:
                    conflict++;
                    Assert.Single(textTopics);
                    Assert.NotEqual(record.Topic, textTopics[0]);
                    break;
            }
            Assert.Equal($"hybrid-{record.Ordinal:D8}", record.Id);
            Assert.Equal(64, record.Vector.Length);
            Assert.Contains(record.Vector, static value => value != 0f);
            Assert.All(record.Vector, static value => Assert.True(float.IsFinite(value) && value >= -1f && value < 1f));
        }

        Assert.InRange(aligned, 13_500, 14_500);
        Assert.InRange(neutral, 3_500, 4_500);
        Assert.InRange(conflict, 1_500, 2_500);
        Assert.InRange(records.Count(static record => record.AccessGroup == "public"), 9_000, 11_000);
        Assert.InRange(records.Count(static record => record.Category == "hybrid-category-00"), 400, 1_000);
        Assert.InRange(records.Count(static record => record.AccessGroup == "tenant-00"), 6, 60);

        var query = profile.GenerateTopicQuery(42, 0, 64);
        Assert.Equal(64, query.Length);
        Assert.Equal(query, profile.GenerateTopicQuery(42, 0, 64));
    }

    [Fact]
    public void Hybrid_canonical_prefix_is_independent_of_requested_count()
    {
        var profile = new LeanCorpusHybridProfile();
        var first = profile.Generate(new DataForgeGenerationOptions(42, 32)).ToArray();
        var larger = profile.Generate(new DataForgeGenerationOptions(42, 64)).Take(32).ToArray();
        Assert.Equal(first.Select(static record => record.Id), larger.Select(static record => record.Id));
        Assert.Equal(first.SelectMany(static record => record.Vector), larger.SelectMany(static record => record.Vector));

        using var left = new MemoryStream();
        using var right = new MemoryStream();
        var leftHash = DataForgeMaterialiser.GenerateToStream(profile, new DataForgeGenerationOptions(42, 32), left).ContentSha256;
        var rightHash = DataForgeMaterialiser.GenerateToStream(profile, new DataForgeGenerationOptions(42, 32), right).ContentSha256;
        Assert.Equal(leftHash, rightHash);
        Assert.Equal(left.ToArray(), right.ToArray());
    }
}
