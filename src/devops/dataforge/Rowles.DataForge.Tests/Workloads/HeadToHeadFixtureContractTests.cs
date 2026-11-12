using System.Text.Json;
using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Workloads;

/// <summary>
/// Re-creates each head-to-head fixture defect that ADR045 removes and proves that the setup
/// gate now rejects it.
/// </summary>
[Collection(BenchmarkArtifactDirectoryCollection.Name)]
public sealed class HeadToHeadFixtureContractTests
{
    private const int CorpusSize = 2_000;

    [Fact]
    public void Preflight_rejects_a_case_that_does_no_work()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkFixtureEvidence.RequireWork("range", "RangeWidth=0.01", BenchmarkFixtureEvidence.LuceneNetEngine, 0));
        Assert.Contains("did no work", exception.Message, StringComparison.Ordinal);

        Assert.Equal(12, BenchmarkFixtureEvidence.RequireWork("range", "RangeWidth=0.01", BenchmarkFixtureEvidence.LeanCorpusEngine, 12));
    }

    [Fact]
    public void Preflight_accepts_a_declared_no_match_case_only_when_it_matches_nothing()
    {
        Assert.Equal(0, BenchmarkFixtureEvidence.RequireWork("fuzzy", "no-match", BenchmarkFixtureEvidence.LeanCorpusEngine, 0, requiresNoMatch: true));
        Assert.Throws<InvalidOperationException>(() =>
            BenchmarkFixtureEvidence.RequireWork("fuzzy", "no-match", BenchmarkFixtureEvidence.LeanCorpusEngine, 3, requiresNoMatch: true));
    }

    [Fact]
    public void Term_vector_highlighter_rejects_unstored_Lucene_bodies()
    {
        string[] leanTexts = ["government said", "the government market"];

        // Re-create the defect: Lucene.NET indexed body unstored, so every sample text was empty.
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TermVectorHighlighterBenchmarks.ValidateSamples(leanTexts, [string.Empty, string.Empty]));
        Assert.Contains("must store body", exception.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() =>
            TermVectorHighlighterBenchmarks.ValidateSamples(leanTexts, []));
        TermVectorHighlighterBenchmarks.ValidateSamples(leanTexts, leanTexts);
    }

    [Fact]
    public void Multi_phrase_rejects_slots_that_the_corpus_never_emits()
    {
        string[] documents = WithArtifactDirectory(static () => BenchmarkData.BuildDocuments(CorpusSize));

        // Re-create the defect: the original slots matched nothing on either engine.
        string[][] original = [["united", "federal"], ["states", "government"]];
        int originalHits = MultiPhraseQueryBenchmarks.CountExpectedHits(documents, original);
        Assert.Equal(0, originalHits);
        Assert.Throws<InvalidOperationException>(() => MultiPhraseQueryBenchmarks.ValidateHits(originalHits, 0, 0));

        int hits = MultiPhraseQueryBenchmarks.CountExpectedHits(documents, MultiPhraseQueryBenchmarks.Slots);
        Assert.True(hits > 0);
        MultiPhraseQueryBenchmarks.ValidateHits(hits, hits, hits);
        Assert.Throws<InvalidOperationException>(() => MultiPhraseQueryBenchmarks.ValidateHits(hits, hits, hits - 1));
    }

    [Fact]
    public void Multi_phrase_expected_count_requires_consecutive_slot_terms()
    {
        string[] documents = ["said public government", "new york", "new big york", "York New", "public said"];
        Assert.Equal(2, MultiPhraseQueryBenchmarks.CountExpectedHits(documents, MultiPhraseQueryBenchmarks.Slots));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(100)]
    public void Term_in_set_rejects_absent_terms_and_selects_only_live_terms(int setSize)
    {
        string[] documents = WithArtifactDirectory(static () => BenchmarkData.BuildDocuments(CorpusSize));
        var corpusTerms = documents
            .SelectMany(static document => document.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(static token => token.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        int Hits(string term) => corpusTerms.Contains(term) ? 1 : 0;

        string[] selected = TermInSetQueryBenchmarks.SelectLiveTerms(documents, setSize);
        Assert.Equal(setSize, selected.Length);
        Assert.Equal(setSize, selected.Distinct(StringComparer.Ordinal).Count());
        TermInSetQueryBenchmarks.ValidateLiveTerms(selected, setSize, Hits, Hits);

        // Re-create the defect: taking the vocabulary prefix included terms the corpus lacks.
        string[] original = TermInSetQueryBenchmarks.Vocabulary.Take(setSize).ToArray();
        if (original.All(term => corpusTerms.Contains(term)))
            return;
        Assert.Throws<InvalidOperationException>(() => TermInSetQueryBenchmarks.ValidateLiveTerms(original, setSize, Hits, Hits));
    }

    [Fact]
    public void Benchmark_temporary_directory_is_scoped_to_one_run()
    {
        Assert.Equal("run-abc", BenchmarkHelpers.ResolveRunName("run-abc", "other"));
        Assert.Equal("run-1234-5678", BenchmarkHelpers.ResolveRunName(null, "1234-5678"));
        Assert.StartsWith("local-", BenchmarkHelpers.ResolveRunName(null, null), StringComparison.Ordinal);
        Assert.NotEqual(BenchmarkHelpers.ResolveRunName(null, null), BenchmarkHelpers.ResolveRunName(null, null));
        Assert.Throws<InvalidOperationException>(() => BenchmarkHelpers.ResolveRunName("../escape", null));

        string areaRoot = RepositoryTemporaryStorage.GetAreaRoot("benchmarks");
        Assert.Equal(areaRoot, Path.GetDirectoryName(BenchmarkHelpers.TempRoot));
        Assert.NotEqual(areaRoot, BenchmarkHelpers.TempRoot);
    }

    [Fact]
    public void Topology_record_is_written_as_a_run_sidecar()
    {
        string artifactDirectory = RepositoryTemporaryStorage.NewPath("dataforge", "leancorpus-topology-");
        Directory.CreateDirectory(artifactDirectory);
        string? previous = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", artifactDirectory);
            BenchmarkFixtureEvidence.Record(new BenchmarkTopologyRecord("range-as-shipped-100", "Lucene.NET", "MMapDirectory", 3, 100));

            string path = Path.Combine(artifactDirectory, "topology", "range-as-shipped-100.Lucene.NET.json");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal("MMapDirectory", document.RootElement.GetProperty("directoryType").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("segmentCount").GetInt32());
            Assert.Equal(100, document.RootElement.GetProperty("documentCount").GetInt32());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", previous);
            Directory.Delete(artifactDirectory, recursive: true);
        }
    }

    private static T WithArtifactDirectory<T>(Func<T> action)
    {
        string? previous = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (!string.IsNullOrWhiteSpace(previous))
            return action();

        string artifactDirectory = RepositoryTemporaryStorage.NewPath("dataforge", "leancorpus-fixture-contract-");
        Directory.CreateDirectory(artifactDirectory);
        try
        {
            Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", artifactDirectory);
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", previous);
            Directory.Delete(artifactDirectory, recursive: true);
        }
    }
}
