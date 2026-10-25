using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Contains unit tests for Language Analyser Concurrency.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class LanguageAnalyserConcurrencyTests
{
    [Fact(DisplayName = "Analyse: thread-local language analysis reuses its filter execution state")]
    public void Analyse_ReusesThreadLocalFilterExecutionState_AndPreservesTokenObservables()
    {
        var stemmer = new CountingThreadLocalStemmer();
        var analyser = new LanguageAnalyser(new Tokeniser(), stopWords: [], stemmer);
        var firstSink = new MaterialisingTokenSink();
        var secondSink = new MaterialisingTokenSink();

        analyser.Analyse("CAFÉ runner", firstSink);
        analyser.Analyse("Straße jumps", secondSink);

        Assert.Equal(1, stemmer.CloneCount);
        Assert.Equal(
            [("café", 0, 4, 1, 1), ("runner", 5, 11, 1, 1)],
            firstSink.Tokens.Select(static token =>
                (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement, token.PositionLength)));
        Assert.Equal(
            [("straße", 0, 6, 1, 1), ("jumps", 7, 12, 1, 1)],
            secondSink.Tokens.Select(static token =>
                (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement, token.PositionLength)));
    }

    [Fact(DisplayName = "Analyse: failed reusable language context is discarded before the next call")]
    public void Analyse_FailedReusableContext_IsDiscardedBeforeNextCall()
    {
        var stemmer = new FirstCloneFailingThreadLocalStemmer();
        var analyser = new LanguageAnalyser(new Tokeniser(), stopWords: [], stemmer: stemmer);

        Assert.Throws<InvalidOperationException>(() =>
            analyser.Analyse("failure", new MaterialisingTokenSink()));

        var nextSink = new MaterialisingTokenSink();
        analyser.Analyse("clean", nextSink);

        Assert.Equal(2, stemmer.CloneCount);
        var token = Assert.Single(nextSink.Tokens);
        Assert.Equal(("clean", 0, 5, 1, 1),
            (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement, token.PositionLength));
    }

    /// <summary>
    /// Verifies concurrent thread-local analysers match the single-threaded baseline.
    /// </summary>
    [Fact(DisplayName = "Analyse: Concurrent Thread-Local Instances Match Single-Threaded Baseline")]
    public void Analyse_ConcurrentThreadLocalInstances_MatchSingleThreadedBaseline()
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
            .Select(t =>
            {
                var matSink = new MaterialisingTokenSink();
                analyser.Analyse(t, matSink);
                return matSink.Tokens.Select(tok => tok.Text).ToArray();
            })
            .ToArray();

        const int iterations = 200;
        var threadLocalFactory = Assert.IsAssignableFrom<IThreadLocalAnalyser>(analyser);
        Parallel.For(
            0,
            iterations * inputs.Length,
            threadLocalFactory.CreateThreadLocalAnalyser,
            (i, _, workerAnalyser) =>
            {
                var idx = i % inputs.Length;
                var matSink = new MaterialisingTokenSink();
                workerAnalyser.Analyse(inputs[idx], matSink);
                var actual = matSink.Tokens.Select(t => t.Text).ToArray();
                Assert.Equal(baseline[idx], actual);
                return workerAnalyser;
            },
            _ => { });
    }

    private sealed class CountingThreadLocalStemmer : IThreadLocalSpanStemmer
    {
        private int _cloneCount;

        public int CloneCount => Volatile.Read(ref _cloneCount);

        public ISpanStemmer CreateThreadLocalStemmer()
        {
            Interlocked.Increment(ref _cloneCount);
            return new CopyStemmer();
        }

        public int Stem(ReadOnlySpan<char> word, Span<char> output)
        {
            word.CopyTo(output);
            return word.Length;
        }

        private sealed class CopyStemmer : ISpanStemmer
        {
            public int Stem(ReadOnlySpan<char> word, Span<char> output)
            {
                word.CopyTo(output);
                return word.Length;
            }
        }
    }

    private sealed class FirstCloneFailingThreadLocalStemmer : IThreadLocalSpanStemmer
    {
        private int _cloneCount;

        public int CloneCount => Volatile.Read(ref _cloneCount);

        public ISpanStemmer CreateThreadLocalStemmer()
        {
            int cloneNumber = Interlocked.Increment(ref _cloneCount);
            return new ExecutionStemmer(throwOnStem: cloneNumber == 1);
        }

        public int Stem(ReadOnlySpan<char> word, Span<char> output)
        {
            word.CopyTo(output);
            return word.Length;
        }

        private sealed class ExecutionStemmer(bool throwOnStem) : ISpanStemmer
        {
            public int Stem(ReadOnlySpan<char> word, Span<char> output)
            {
                if (throwOnStem)
                    throw new InvalidOperationException("Injected stemmer failure.");

                word.CopyTo(output);
                return word.Length;
            }
        }
    }
}
