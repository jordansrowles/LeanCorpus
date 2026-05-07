using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Xunit;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Language Analyser Concurrency.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class LanguageAnalyserConcurrencyTests
{
    /// <summary>
    /// Verifies the Analyse: Concurrent Calls Match Single Threaded Baseline scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Concurrent Calls Match Single Threaded Baseline")]
    public void Analyse_ConcurrentCalls_MatchSingleThreadedBaseline()
    {
        var analyser = AnalyserFactory.Create("en");

        string[] inputs =
        [
            "The quick brown fox jumps over the lazy dog",
            "Running foxes jumped over the lazy dogs and slept soundly",
            "Programming is the art of telling another human what one wants the computer to do",
            "She sells seashells by the seashore on a sunny afternoon",
            "Rapid hashing yields searchable inverted lists for relevance scoring",
            "Vector embeddings approximate semantic distance between two short passages"
        ];

        // Single-threaded baseline.
        var baseline = inputs
            .Select(t => analyser.Analyse(t).Select(tok => tok.Text).ToArray())
            .ToArray();

        const int iterations = 200;
        Parallel.For(0, iterations * inputs.Length, i =>
        {
            var idx = i % inputs.Length;
            var actual = analyser.Analyse(inputs[idx]).Select(t => t.Text).ToArray();
            Assert.Equal(baseline[idx], actual);
        });
    }
}
