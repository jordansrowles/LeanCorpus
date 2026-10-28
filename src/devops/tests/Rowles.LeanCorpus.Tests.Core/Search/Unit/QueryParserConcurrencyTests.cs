using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Verifies concurrent parser ownership and invocation isolation.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class QueryParserConcurrencyTests
{
    [Fact(DisplayName = "Query parser rejects overlapping calls with a fixed analyser")]
    public async Task Parse_FixedAnalyser_RejectsConcurrentCallAndCanBeReused()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var parser = new QueryParser("body", new BlockingAnalyser(entered, release));

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task<Query> firstParse = Task.Run(() => parser.Parse("first"), cancellationToken);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "The first parse did not reach analysis.");

        Exception? concurrentError;
        try
        {
            concurrentError = Record.Exception(() => parser.Parse("second"));
        }
        finally
        {
            release.Set();
        }

        Assert.IsType<TermQuery>(await firstParse);
        Assert.IsType<InvalidOperationException>(concurrentError);
        Assert.IsType<TermQuery>(parser.Parse("after"));
    }

    [Fact(DisplayName = "Query parser isolates phrase budgets for concurrent analyser factory calls")]
    public async Task Parse_AnalyserFactory_UsesIndependentPhraseBudgetsConcurrently()
    {
        const int concurrentParses = 4;
        const int phraseTokenCount = 5_000;
        string phraseText = string.Join(' ', Enumerable.Repeat("term", phraseTokenCount));
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        for (int repetition = 0; repetition < 3; repetition++)
        {
            using var barrier = new Barrier(concurrentParses);
            int factoryCalls = 0;
            var parser = new QueryParser("body", () =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new BarrierPhraseAnalyser(barrier);
            });

            Task<Query>[] parses = Enumerable.Range(0, concurrentParses)
                .Select(_ => Task.Factory.StartNew(
                    () => parser.Parse($"\"{phraseText}\""),
                    cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();

            Query[] results = await Task.WhenAll(parses);

            Assert.Equal(concurrentParses, factoryCalls);
            foreach (Query result in results)
            {
                var phrase = Assert.IsType<PhraseQuery>(result);
                Assert.Equal(Enumerable.Repeat("term", phraseTokenCount), phrase.Terms);
            }
        }
    }

    [Fact(DisplayName = "Derived query parsers preserve their behaviour with analyser factories")]
    public void Parse_DerivedParsersWithFactories_PreserveTheirBehaviour()
    {
        var analysingParser = new AnalysingQueryParser("body", static () => new StandardAnalyser());
        var prefix = Assert.IsType<PrefixQuery>(analysingParser.Parse("QUICK*"));
        Assert.Equal("quick", prefix.Prefix);

        var complexPhraseParser = new ComplexPhraseQueryParser("body", static () => new StandardAnalyser());
        var phrase = Assert.IsType<PhraseQuery>(complexPhraseParser.Parse("\"quick brown\""));
        Assert.Equal(["quick", "brown"], phrase.Terms);
    }

    private sealed class BlockingAnalyser(
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : IAnalyser
    {
        private int _callCount;

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The test did not release the blocked analysis call.");
            }

            sink.Add(input, 0, input.Length);
        }
    }

    private sealed class BarrierPhraseAnalyser(Barrier barrier) : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Concurrent phrase analyses did not reach the barrier.");

            int position = 0;
            while (position < input.Length)
            {
                while (position < input.Length && char.IsWhiteSpace(input[position]))
                    position++;
                if (position >= input.Length)
                    break;

                int start = position;
                while (position < input.Length && !char.IsWhiteSpace(input[position]))
                    position++;
                sink.Add(input[start..position], start, position);
            }
        }
    }
}
