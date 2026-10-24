using Rowles.LeanCorpus.Benchmarks;
using System.Text.Json;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class SuggesterQualityFixtureTests
{
    [Fact]
    public void Fixture_covers_the_existing_DataForge_typo_workload_and_declares_quality_probes()
    {
        SuggesterQualityFixture fixture = SuggesterQualityFixture.Load(BenchmarkHelpers.RepositoryRoot);
        var expected = BenchmarkData.BuildMisspelledTerms()
            .Select(static pair => (Query: pair.Misspelled, Correction: pair.Original))
            .ToArray();

        Assert.Equal(expected, fixture.CorrectionCases
            .Select(static item => (Query: item.Query, Correction: Assert.Single(item.AcceptableCorrections)))
            .ToArray());
        Assert.Equal("intentional-no-suggestion", fixture.NoSuggestionCase.Id);
        Assert.True(fixture.NoSuggestionCase.ExpectNoSuggestion);
        Assert.Contains(fixture.Probes, static probe => probe.Id == "case-variant");
        Assert.Contains(fixture.Probes, static probe => probe.Id == "unicode-canonical-variant");
        Assert.Contains(fixture.Probes, static probe => probe.Id == "compound-variant");

        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            ".config", "devops", "benchmark-correctness-fixtures", "bval-15-suggester-quality-ground-truth.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        Assert.False(document.RootElement.GetProperty("input").GetProperty("correctionCases")[0]
            .TryGetProperty("suggestions", out _), "The fixture must not substitute synthetic suggestion output for engine results.");
    }

    [Fact]
    public void Scorer_reports_ranked_quality_and_intentional_no_suggestion_separately()
    {
        SuggesterQualityMetrics metrics = SuggesterQualityScorer.ComputeMetrics(
        [
            new SuggesterQualityQueryResult
            {
                AcceptableCorrections = ["alpha"],
                FirstRelevantRank = 1,
                Suggestions =
                [
                    new SuggesterQualityCandidate { Rank = 1, IsAcceptableCorrection = true },
                    new SuggesterQualityCandidate { Rank = 2 }
                ]
            },
            new SuggesterQualityQueryResult
            {
                AcceptableCorrections = ["bravo"],
                FirstRelevantRank = 2,
                Suggestions =
                [
                    new SuggesterQualityCandidate { Rank = 1 },
                    new SuggesterQualityCandidate { Rank = 2, IsAcceptableCorrection = true }
                ]
            },
            new SuggesterQualityQueryResult
            {
                AcceptableCorrections = ["charlie"],
                Suggestions = [new SuggesterQualityCandidate { Rank = 1 }]
            },
            new SuggesterQualityQueryResult
            {
                IsNoSuggestionCase = true,
                ExpectNoSuggestion = true,
                NoSuggestionSatisfied = true
            }
        ]);

        Assert.Equal(3, metrics.CorrectionQueryCount);
        Assert.Equal(4, metrics.TimedQueryCount);
        Assert.Equal(1d / 3, metrics.AccuracyAt1, precision: 10);
        Assert.Equal(2d / 3, metrics.RecallAt5, precision: 10);
        Assert.Equal(0.5, metrics.MeanReciprocalRank, precision: 10);
        Assert.Equal(0.75, metrics.Coverage, precision: 10);
        Assert.Equal(5, metrics.CandidateCount);
        Assert.True(metrics.IntentionalNoSuggestionCaseCorrect);
    }

    [Fact]
    public void Quality_evidence_scores_actual_ranked_suggestions_for_each_engine()
    {
        string? previousArtifactDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        string artifactDirectory = Path.Combine(Path.GetTempPath(), "leancorpus-bval-15", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactDirectory);
        Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", artifactDirectory);
        var benchmarks = new SuggesterBenchmarks { DocumentCount = BenchmarkData.DefaultDocCount };
        try
        {
            benchmarks.Setup();
            SuggesterQualityEvidence evidence = benchmarks.BuildQualityEvidence();

            Assert.Equal(BenchmarkData.DefaultDocCount, evidence.DocumentCount);
            Assert.Equal(3, evidence.Engines.Length);
            Assert.Equal(
                new[] { "DidYouMean", "SpellIndex", "LuceneSpellChecker" },
                evidence.Engines.Select(static engine => engine.EngineId).ToArray());

            foreach (SuggesterEngineQualityEvidence engine in evidence.Engines)
            {
                Assert.Equal(BenchmarkData.BuildMisspelledTerms().Length, engine.CorrectionQueryCount);
                Assert.Equal(engine.QueryResults.Sum(static result => result.Suggestions.Length), engine.CandidateCount);
                Assert.Contains(engine.QueryResults, static result => result.Suggestions.Any(static suggestion => suggestion.IsAcceptableCorrection));
                Assert.All(engine.QueryResults, static result =>
                {
                    Assert.Equal(Enumerable.Range(1, result.Suggestions.Length),
                        result.Suggestions.Select(static suggestion => suggestion.Rank));
                    Assert.Equal(result.Suggestions.Length,
                        result.Suggestions.Select(static suggestion => suggestion.Term).Distinct(StringComparer.Ordinal).Count());
                    Assert.Equal(result.Suggestions
                        .Where(static suggestion => suggestion.IsAcceptableCorrection)
                        .Select(static suggestion => (int?)suggestion.Rank)
                        .FirstOrDefault(), result.FirstRelevantRank);
                });

                SuggesterQualityMetrics recomputed = SuggesterQualityScorer.ComputeMetrics(engine.QueryResults);
                Assert.Equal(recomputed.CorrectionQueryCount, engine.Metrics.CorrectionQueryCount);
                Assert.Equal(recomputed.TimedQueryCount, engine.Metrics.TimedQueryCount);
                Assert.Equal(recomputed.AccuracyAt1, engine.Metrics.AccuracyAt1);
                Assert.Equal(recomputed.RecallAt5, engine.Metrics.RecallAt5);
                Assert.Equal(recomputed.MeanReciprocalRank, engine.Metrics.MeanReciprocalRank, precision: 10);
                Assert.Equal(recomputed.Coverage, engine.Metrics.Coverage, precision: 10);
                Assert.Equal(recomputed.CandidateCount, engine.Metrics.CandidateCount);
                Assert.Equal(recomputed.IntentionalNoSuggestionCaseCorrect, engine.Metrics.IntentionalNoSuggestionCaseCorrect);
            }

            SuggesterQualityFixture fixture = SuggesterQualityFixture.Load(BenchmarkHelpers.RepositoryRoot);
            Assert.All(evidence.Engines, engine =>
            {
                Assert.Equal(3, engine.ProbeResults.Length);
                Assert.All(engine.ProbeResults, static probe => Assert.NotNull(probe.Suggestions));
                foreach (SuggesterProbeCase expectedProbe in fixture.Probes)
                {
                    SuggesterQualityQueryResult actualProbe = Assert.Single(engine.ProbeResults,
                        probe => probe.Id == expectedProbe.Id);
                    Assert.Equal(SuggesterQualityNormalisation.Normalise(expectedProbe.Query), actualProbe.Query);
                    Assert.Equal(expectedProbe.AcceptableCorrections.Select(SuggesterQualityNormalisation.Normalise),
                        actualProbe.AcceptableCorrections);
                }
                SuggesterQualityQueryResult noSuggestion = Assert.Single(engine.QueryResults,
                    static query => query.IsNoSuggestionCase);
                Assert.True(noSuggestion.NoSuggestionSatisfied);
                Assert.Empty(noSuggestion.Suggestions);
                Assert.True(engine.Metrics.IntentionalNoSuggestionCaseCorrect);
            });
        }
        finally
        {
            try
            {
                benchmarks.Cleanup();
            }
            finally
            {
                Environment.SetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR", previousArtifactDirectory);
                if (Directory.Exists(artifactDirectory))
                    Directory.Delete(artifactDirectory, recursive: true);
            }
        }
    }
}
