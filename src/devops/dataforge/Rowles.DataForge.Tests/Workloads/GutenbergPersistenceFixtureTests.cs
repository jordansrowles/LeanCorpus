using System.Reflection;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Benchmarks;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class GutenbergPersistenceFixtureTests
{
    private const string FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-13-gutenberg-persistence-restart.json";

    [Fact]
    public void Fixture_declares_as_shipped_and_matched_persistence_profiles()
    {
        using var fixture = LoadFixture();
        var profiles = fixture.RootElement.GetProperty("input").GetProperty("profiles").EnumerateArray().ToArray();

        Assert.Equal(
            new[] { "as-shipped-persistence", "matched-persistence" },
            profiles.Select(profile => profile.GetProperty("id").GetString()).OrderBy(static id => id, StringComparer.Ordinal));

        JsonElement matched = profiles.Single(profile => profile.GetProperty("id").GetString() == "matched-persistence");
        Assert.True(matched.TryGetProperty("leancorpus", out _), "Matched persistence must define LeanCorpus writer settings.");
        Assert.True(matched.TryGetProperty("lucene", out _), "Matched persistence must define Lucene writer settings.");
    }

    [Fact]
    public void Benchmark_enumerates_the_same_persistence_profiles_as_the_fixture()
    {
        var profileSource = typeof(GutenbergIndexingBenchmarks).GetProperty(
            "PersistenceProfiles",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(profileSource);
        var profiles = Assert.IsAssignableFrom<IEnumerable<string>>(profileSource.GetValue(null));

        Assert.Equal(new[] { "as-shipped-persistence", "matched-persistence" }, profiles);
    }

    [Fact]
    public void Persistence_profiles_commit_close_reopen_and_preserve_exact_ids()
    {
        var results = GutenbergPersistenceFixtureVerifier.Verify(BenchmarkHelpers.RepositoryRoot);

        Assert.Equal(
            new[] { "as-shipped-persistence", "matched-persistence" },
            results.Select(static result => result.ProfileId).Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal));
        Assert.Equal(4, results.Count);
        foreach (var result in results)
        {
            Assert.Equal(new[] { "book-001", "book-002", "book-003" }, result.ReopenedDocumentIds);
            Assert.Equal(3, result.ReopenedDocumentCount);
            Assert.True(result.IndexByteCount > 0);
            Assert.Equal(result.IndexByteCount / result.ExplicitCommitCount, result.IndexBytesPerCommit);
            Assert.Equal(1, result.CommitCount);
            Assert.Equal(1, result.ExplicitCommitCount);
            Assert.False(result.DirectoryMetadataSyncObservable);
            Assert.Contains(result.DirectoryType, new[] { "MMapDirectory", "FSDirectory" });
            Assert.False(string.IsNullOrWhiteSpace(result.WriterSettings));
        }

        var leanAsShipped = results.Single(result =>
            result.ProfileId == "as-shipped-persistence" && result.Engine == "LeanCorpus");
        var luceneAsShipped = results.Single(result =>
            result.ProfileId == "as-shipped-persistence" && result.Engine == "Lucene.NET");
        var leanMatched = results.Single(result =>
            result.ProfileId == "matched-persistence" && result.Engine == "LeanCorpus");
        var luceneMatched = results.Single(result =>
            result.ProfileId == "matched-persistence" && result.Engine == "Lucene.NET");

        Assert.False(leanAsShipped.DurableCommits);
        Assert.True(luceneAsShipped.DurableCommits);
        Assert.Equal(0, leanAsShipped.FileSyncOperationCount);
        Assert.Equal("Directional", leanAsShipped.ClaimClass);
        Assert.Equal("Directional", luceneAsShipped.ClaimClass);
        Assert.True(leanMatched.DurableCommits);
        Assert.True(luceneMatched.DurableCommits);
        Assert.True(leanMatched.FileSyncOperationCount > 0);
        Assert.True(leanMatched.FileSyncBytes > 0);
        Assert.Equal(leanMatched.RamBufferSizeMB, luceneMatched.RamBufferSizeMB);
        Assert.Equal(leanMatched.RamPerThreadHardLimitMB, luceneMatched.RamPerThreadHardLimitMB);
        Assert.Equal(leanMatched.MaxBufferedDocs, luceneMatched.MaxBufferedDocs);
        Assert.Equal(leanMatched.UseCompoundFile, luceneMatched.UseCompoundFile);
    }

    [Fact]
    public void As_shipped_persistence_is_reported_as_a_directional_comparison()
    {
        var statistics = new BenchmarkStatisticsReport
        {
            SampleCount = 5,
            MeanNanoseconds = 10,
            StandardDeviationNanoseconds = 0,
            CoefficientOfVariationPercent = 0
        };

        List<string> directional = BenchmarkRunReportBuilder.BuildQualityFlags(
            nameof(GutenbergIndexingBenchmarks),
            "LeanCorpus_Standard_Index",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PersistenceProfile"] = "as-shipped-persistence"
            },
            statistics);
        List<string> matched = BenchmarkRunReportBuilder.BuildQualityFlags(
            nameof(GutenbergIndexingBenchmarks),
            "LeanCorpus_Standard_Index",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PersistenceProfile"] = "matched-persistence"
            },
            statistics);

        Assert.Contains("DirectionalPersistenceMismatch", directional);
        Assert.DoesNotContain("DirectionalPersistenceMismatch", matched);
    }

    [Fact]
    public void English_benchmark_analyser_creates_an_independent_instance_per_writer_thread()
    {
        var analyser = new EnglishAnalyser();
        var threadLocalAnalyser = Assert.IsAssignableFrom<IThreadLocalAnalyser>(analyser);

        IAnalyser workerAnalyser = threadLocalAnalyser.CreateThreadLocalAnalyser();

        Assert.IsType<EnglishAnalyser>(workerAnalyser);
        Assert.NotSame(analyser, workerAnalyser);
    }

    private static JsonDocument LoadFixture()
    {
        var path = Path.Combine(BenchmarkHelpers.RepositoryRoot, FixturePath);
        Assert.True(File.Exists(path), $"Required BVAL-13 fixture is missing: {path}");
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
}
