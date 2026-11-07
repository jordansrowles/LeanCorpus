using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Tests;

/// <summary>
/// Verifies that removing a stop word keeps its position, so the next retained token carries the gap.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class PositionGapTests
{
    private static readonly string[] StopWordPool = ["the", "and", "of", "a"];
    private static readonly string[] WordPool = ["the", "cat", "and", "dog", "of", "bird", "a", "fox"];

    private static IReadOnlyList<Token> Run(IAnalyser analyser, string input)
    {
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(input, sink);
        return sink.Tokens;
    }

    private static string Format(IEnumerable<Token> tokens)
        => string.Join(' ', tokens.Select(static token => $"{token.Text}:{token.PositionIncrement}"));

    private static Analyser Pipeline()
        => new(new Tokeniser(), new LowercaseFilter(), new StopWordFilter());

    [Theory(DisplayName = "StandardAnalyser: removed stop words keep their position")]
    [InlineData("said government", "said:1 government:1")]
    [InlineData("said the government", "said:1 government:2")]
    [InlineData("said the of government", "said:1 government:3")]
    [InlineData("said the of in government", "said:1 government:4")]
    [InlineData("the cat", "cat:2")]
    [InlineData("the a cat", "cat:3")]
    [InlineData("the a an cat", "cat:4")]
    [InlineData("cat the", "cat:1")]
    [InlineData("cat the a", "cat:1")]
    [InlineData("cat the a an", "cat:1")]
    [InlineData("the a an", "")]
    [InlineData("", "")]
    [InlineData("cat the dog the bird", "cat:1 dog:2 bird:2")]
    [InlineData("THE ÉCHO", "écho:2")]
    [InlineData("café AND THE ÉCHO", "café:1 écho:3")]
    public void StandardAnalyser_RemovedStopWordsKeepTheirPosition(string input, string expected)
    {
        Assert.Equal(expected, Format(Run(new StandardAnalyser(), input)));
        Assert.Equal(expected, Format(Run(Pipeline(), input)));
    }

    [Fact(DisplayName = "StandardAnalyser: offsets of retained tokens are unchanged by gaps")]
    public void StandardAnalyser_OffsetsAreUnchanged()
    {
        const string input = "said the government";

        var tokens = Run(new StandardAnalyser(), input);

        Assert.Equal([(0, 4), (9, 19)], tokens.Select(static token => (token.StartOffset, token.EndOffset)));
        Assert.Equal(
            tokens.Select(static token => (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement)),
            Run(Pipeline(), input).Select(static token => (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement)));
    }

    [Fact(DisplayName = "Analysers built on StopWordFilter keep removed positions")]
    public void DerivedAnalysers_KeepRemovedPositions()
    {
        const string input = "said the government";
        IReadOnlyDictionary<string, IAnalyser> analysers = new Dictionary<string, IAnalyser>
        {
            ["StemmerAnalyser"] = StemmerAnalyser.Porter(),
            ["StemmedAnalyser"] = new StemmedAnalyser(),
            ["LanguageAnalyser"] = AnalyserFactory.Create("en"),
            ["IcuAnalyser"] = new IcuAnalyser()
        };

        foreach (var (name, analyser) in analysers)
        {
            var increments = Run(analyser, input).Select(static token => token.PositionIncrement).ToArray();
            Assert.True(increments.SequenceEqual([1, 2]), $"{name} produced increments [{string.Join(", ", increments)}].");
        }
    }

    [Property(DisplayName = "Increments sum to the original position and the filter agrees with the analyser",
        MaxTest = 300, StartSize = 1, EndSize = 40)]
    public void IncrementsSumToTheOriginalPosition(NonEmptyArray<byte> input)
    {
        string[] words = input.Get.Select(static value => WordPool[value % WordPool.Length]).ToArray();
        string text = string.Join(' ', words);

        var standard = Run(new StandardAnalyser(), text);
        var pipeline = Run(Pipeline(), text);

        Assert.Equal(
            standard.Select(static token => (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement)),
            pipeline.Select(static token => (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement)));

        int position = 0;
        var retained = Enumerable.Range(0, words.Length)
            .Where(index => !StopWordPool.Contains(words[index]))
            .ToArray();
        Assert.Equal(retained.Length, standard.Count);
        for (int i = 0; i < standard.Count; i++)
        {
            position += standard[i].PositionIncrement;
            Assert.Equal(retained[i] + 1, position);
            Assert.Equal(words[retained[i]], standard[i].Text);
        }
    }

    [Fact(DisplayName = "StopWordFilter: a removed token passes its own increment on")]
    public void StopWordFilter_PassesTheRemovedIncrementOn()
    {
        var filter = new StopWordFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, Token.DefaultType, 3, null, sink);
        filter.Apply("cat", 4, 7, Token.DefaultType, 2, null, sink);
        filter.Apply("dog", 8, 11, Token.DefaultType, 1, null, sink);

        Assert.Equal([5, 1], sink.Tokens.Select(static token => token.PositionIncrement));
    }

    [Fact(DisplayName = "StopWordFilter: Finish discards trailing removals")]
    public void StopWordFilter_FinishDiscardsTrailingRemovals()
    {
        var filter = new StopWordFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("cat", 0, 3, Token.DefaultType, 1, null, sink);
        filter.Apply("the", 4, 7, Token.DefaultType, 1, null, sink);
        filter.Finish(sink);
        filter.Apply("dog", 0, 3, Token.DefaultType, 1, null, sink);

        Assert.Equal([1, 1], sink.Tokens.Select(static token => token.PositionIncrement));
    }

    [Fact(DisplayName = "StopWordFilter: Clone returns an independent instance with the same stop words")]
    public void StopWordFilter_CloneIsIndependent()
    {
        var original = new StopWordFilter(["foo"]);
        var sink = new MaterialisingTokenSink();
        original.Apply("foo", 0, 3, Token.DefaultType, 1, null, sink);

        var clone = Assert.IsType<StopWordFilter>(((ISpanTokenFilter)original).Clone());
        clone.Apply("bar", 0, 3, Token.DefaultType, 1, null, sink);

        Assert.NotSame(original, clone);
        Assert.Equal(1, Assert.Single(sink.Tokens).PositionIncrement);
        clone.Apply("foo", 4, 7, Token.DefaultType, 1, null, sink);
        clone.Apply("baz", 8, 11, Token.DefaultType, 1, null, sink);
        Assert.Equal(2, sink.Tokens[^1].PositionIncrement);
    }

    [Fact(DisplayName = "Analyse: no skipped positions leak between documents or threads")]
    public void Analyse_DoesNotLeakSkippedPositionsBetweenDocumentsOrThreads()
    {
        var analyser = AnalyserFactory.Create("en");
        string[] inputs = ["cat the a", "dog", "the the bird", "fox and", "wolf"];
        var expected = inputs.Select(input => Format(Run(analyser, input))).ToArray();
        Assert.Equal(["cat:1", "dog:1", "bird:3", "fox:1", "wolf:1"], expected);

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            for (int round = 0; round < 400; round++)
            {
                for (int i = 0; i < inputs.Length; i++)
                {
                    string actual = Format(Run(analyser, inputs[i]));
                    if (actual != expected[i])
                        failures.Add($"'{inputs[i]}' gave '{actual}', expected '{expected[i]}'.");
                }
            }
        });

        Assert.Empty(failures);
    }

    [Fact(DisplayName = "Analyse: a failed analysis leaves no skipped positions behind")]
    public void Analyse_FailureLeavesNoSkippedPositionsBehind()
    {
        var analyser = new Analyser(new FailingAfterStopWordTokeniser(), new LowercaseFilter(), new StopWordFilter());

        Assert.Throws<InvalidOperationException>(() => analyser.Analyse("boom", new MaterialisingTokenSink()));

        var sink = new MaterialisingTokenSink();
        analyser.Analyse("cat", sink);
        Assert.Equal(1, Assert.Single(sink.Tokens).PositionIncrement);
    }

    private sealed class FailingAfterStopWordTokeniser : ISpanTokeniser
    {
        public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (input.SequenceEqual("boom"))
            {
                sink.Add("the", 0, 3);
                throw new InvalidOperationException("Forced failure after a removed stop word.");
            }

            sink.Add(input, 0, input.Length);
        }
    }
}
