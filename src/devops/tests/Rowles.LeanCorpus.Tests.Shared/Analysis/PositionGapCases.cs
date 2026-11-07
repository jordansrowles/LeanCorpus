using FsCheck;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Shared.Analysis;

/// <summary>
/// Verifies that removing a stop word keeps its position, so the next retained token carries the gap.
/// </summary>

internal static class PositionGapCases
{
    public static IEnumerable<object[]> RemovalCases =>
    [
        ["said government", "said:1 government:1"],
        ["said the government", "said:1 government:2"],
        ["said the of government", "said:1 government:3"],
        ["said the of in government", "said:1 government:4"],
        ["the cat", "cat:2"],
        ["the a cat", "cat:3"],
        ["the a an cat", "cat:4"],
        ["cat the", "cat:1"],
        ["cat the a", "cat:1"],
        ["cat the a an", "cat:1"],
        ["the a an", ""],
        ["", ""],
        ["cat the dog the bird", "cat:1 dog:2 bird:2"],
        ["THE ÉCHO", "écho:2"],
        ["café AND THE ÉCHO", "café:1 écho:3"],
    ];

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

    internal static void StandardAnalyser_RemovedStopWordsKeepTheirPosition(string input, string expected)
    {
        Assert.Equal(expected, Format(Run(new StandardAnalyser(), input)));
        Assert.Equal(expected, Format(Run(Pipeline(), input)));
    }

    internal static void StandardAnalyser_OffsetsAreUnchanged()
    {
        const string input = "said the government";

        var tokens = Run(new StandardAnalyser(), input);

        Assert.Equal([(0, 4), (9, 19)], tokens.Select(static token => (token.StartOffset, token.EndOffset)));
        Assert.Equal(
            tokens.Select(static token => (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement)),
            Run(Pipeline(), input).Select(static token => (token.Text, token.StartOffset, token.EndOffset, token.PositionIncrement)));
    }

    internal static void DerivedAnalysers_KeepRemovedPositions()
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

    private static readonly string[] ParityInputs =
    [
        "said the government",
        "THE ÉCHO and the café",
        "the a an cat sat on the mat of",
        "Running foxes jumped over the lazy dogs in the warm sun",
    ];

    /// <summary>
    /// The terms, position increments and offsets that every analyser affected by the position contract must
    /// produce. This table is compiled into both the standalone Rowles.Text tests and the Core tests, so the
    /// two builds of the shared analysis source are held to the same output.
    /// </summary>
    private static readonly string ParityGolden = """
        StandardAnalyser | said the government => said:1:0-4 government:2:9-19
        StandardAnalyser | THE ÉCHO and the café => écho:2:4-8 café:3:17-21
        StandardAnalyser | the a an cat sat on the mat of => cat:4:9-12 sat:1:13-16 mat:3:24-27
        StandardAnalyser | Running foxes jumped over the lazy dogs in the warm sun => running:1:0-7 foxes:1:8-13 jumped:1:14-20 over:1:21-25 lazy:2:30-34 dogs:1:35-39 warm:3:47-51 sun:1:52-55
        StemmerAnalyser | said the government => said:1:0-4 govern:2:9-19
        StemmerAnalyser | THE ÉCHO and the café => écho:2:4-8 café:3:17-21
        StemmerAnalyser | the a an cat sat on the mat of => cat:4:9-12 sat:1:13-16 mat:3:24-27
        StemmerAnalyser | Running foxes jumped over the lazy dogs in the warm sun => run:1:0-7 fox:1:8-13 jump:1:14-20 over:1:21-25 lazi:2:30-34 dog:1:35-39 warm:3:47-51 sun:1:52-55
        StemmedAnalyser | said the government => said:1:0-4 govern:2:9-19
        StemmedAnalyser | THE ÉCHO and the café => écho:2:4-8 café:3:17-21
        StemmedAnalyser | the a an cat sat on the mat of => cat:4:9-12 sat:1:13-16 mat:3:24-27
        StemmedAnalyser | Running foxes jumped over the lazy dogs in the warm sun => run:1:0-7 fox:1:8-13 jump:1:14-20 over:1:21-25 lazi:2:30-34 dog:1:35-39 warm:3:47-51 sun:1:52-55
        LanguageAnalyser(en) | said the government => said:1:0-4 govern:2:9-19
        LanguageAnalyser(en) | THE ÉCHO and the café => écho:2:4-8 café:3:17-21
        LanguageAnalyser(en) | the a an cat sat on the mat of => cat:4:9-12 sat:1:13-16 mat:3:24-27
        LanguageAnalyser(en) | Running foxes jumped over the lazy dogs in the warm sun => run:1:0-7 fox:1:8-13 jump:1:14-20 over:1:21-25 lazi:2:30-34 dog:1:35-39 warm:3:47-51 sun:1:52-55
        IcuAnalyser | said the government => said:1:0-4 government:2:9-19
        IcuAnalyser | THE ÉCHO and the café => écho:2:4-8 café:3:17-21
        IcuAnalyser | the a an cat sat on the mat of => cat:4:9-12 sat:1:13-16 mat:3:24-27
        IcuAnalyser | Running foxes jumped over the lazy dogs in the warm sun => running:1:0-7 foxes:1:8-13 jumped:1:14-20 over:1:21-25 lazy:2:30-34 dogs:1:35-39 warm:3:47-51 sun:1:52-55
        """.ReplaceLineEndings("\n");

    internal static string DumpParityOutput()
    {
        var analysers = new (string Name, Func<IAnalyser> Create)[]
        {
            ("StandardAnalyser", static () => new StandardAnalyser()),
            ("StemmerAnalyser", static () => StemmerAnalyser.Porter()),
            ("StemmedAnalyser", static () => new StemmedAnalyser()),
            ("LanguageAnalyser(en)", static () => AnalyserFactory.Create("en")),
            ("IcuAnalyser", static () => new IcuAnalyser()),
        };
        var lines = new List<string>();
        foreach (var (name, create) in analysers)
        {
            foreach (string input in ParityInputs)
            {
                var tokens = Run(create(), input)
                    .Select(static token => $"{token.Text}:{token.PositionIncrement}:{token.StartOffset}-{token.EndOffset}");
                lines.Add($"{name} | {input} => {string.Join(' ', tokens)}");
            }
        }

        return string.Join('\n', lines);
    }

    internal static void AllAffectedAnalysersMatchTheSharedOutput()
        => Assert.Equal(ParityGolden, DumpParityOutput());

    internal static void IncrementsSumToTheOriginalPosition(NonEmptyArray<byte> input)
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

    internal static void StopWordFilter_PassesTheRemovedIncrementOn()
    {
        var filter = new StopWordFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("the", 0, 3, Token.DefaultType, 3, null, sink);
        filter.Apply("cat", 4, 7, Token.DefaultType, 2, null, sink);
        filter.Apply("dog", 8, 11, Token.DefaultType, 1, null, sink);

        Assert.Equal([5, 1], sink.Tokens.Select(static token => token.PositionIncrement));
    }

    internal static void StopWordFilter_FinishDiscardsTrailingRemovals()
    {
        var filter = new StopWordFilter();
        var sink = new MaterialisingTokenSink();

        filter.Apply("cat", 0, 3, Token.DefaultType, 1, null, sink);
        filter.Apply("the", 4, 7, Token.DefaultType, 1, null, sink);
        filter.Finish(sink);
        filter.Apply("dog", 0, 3, Token.DefaultType, 1, null, sink);

        Assert.Equal([1, 1], sink.Tokens.Select(static token => token.PositionIncrement));
    }

    internal static void StopWordFilter_CloneIsIndependent()
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

    internal static void Analyse_DoesNotLeakSkippedPositionsBetweenDocumentsOrThreads()
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

    internal static void Analyse_FailureLeavesNoSkippedPositionsBehind()
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
