using System.Collections.Concurrent;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// <see cref="StemmedAnalyser"/> reuses its analysis context now that
/// <c>PorterStemmerFilter</c> is a reusable filter (Sprint 4 PR 1).
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class StemmedAnalyserReuseTests
{
    private static readonly string[] Documents =
    [
        "The running foxes jumped over the lazy dogs while children watched",
        "Generalisations of connected relational databases were operationally hopeful",
        "Stemming reduces conditional happiness and controllable organisations"
    ];

    [Fact(DisplayName = "Stemmed analyser: no per-document allocation beyond the sink")]
    public void Stemmed_analyser_allocates_nothing_per_document_beyond_the_sink()
    {
        var analyser = new StemmedAnalyser();
        var sink = new CountingTokenSink();
        for (int i = 0; i < 200; i++)
            analyser.Analyse(Documents[i % Documents.Length], sink);

        const int documents = 1_000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < documents; i++)
            analyser.Analyse(Documents[i % Documents.Length], sink);
        long perDocument = (GC.GetAllocatedBytesForCurrentThread() - before) / documents;

        Assert.True(perDocument < 32, $"StemmedAnalyser allocated {perDocument} B per document; the analysis context should be reused.");
    }

    [Fact(DisplayName = "Stemmed analyser: thread-local instances match the single-threaded output")]
    public void Thread_local_stemmed_analysers_match_the_single_threaded_output()
    {
        var factory = new StemmedAnalyser();
        string[] expected = Documents.Select(document => Render(factory, document)).ToArray();
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, Environment.ProcessorCount * 2, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            worker =>
            {
                var local = ((IThreadLocalAnalyser)factory).CreateThreadLocalAnalyser();
                for (int round = 0; round < 200; round++)
                {
                    int index = (worker + round) % Documents.Length;
                    string actual = Render(local, Documents[index]);
                    if (actual != expected[index])
                        failures.Add($"worker {worker} round {round}: {actual}");
                }
            });

        Assert.Empty(failures);
    }

    private static string Render(IAnalyser analyser, string text)
    {
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(text, sink);
        return string.Join(" ", sink.Tokens.Select(static token => $"{token.Text}@{token.StartOffset}+{token.PositionIncrement}"));
    }
}
