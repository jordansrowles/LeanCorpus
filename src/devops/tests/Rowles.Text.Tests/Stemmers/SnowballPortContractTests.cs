using System.Text;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Rowles.LeanCorpus.Tests.Stemming;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Issue #122: the production Snowball runtime against the unchanged reference runtime on inputs
/// the word lists never reach, plus the span buffer contract, per-thread ownership and allocation.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SnowballPortContractTests
{
    private const int FuzzWords = 200_000;
    private const int ArabicFuzzWords = 500_000;

    public static TheoryData<string> Algorithms() => new(SnowballFixtures.Algorithms);

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void DifferentialFuzz_ProductionMatchesReference(string algorithm)
    {
        var reference = SnowballReference.Create(algorithm);
        var production = ProductionStemmers.Create(algorithm);
        var buffer = new char[4096];
        int differences = 0;
        var examples = new List<string>();
        foreach (var word in FuzzCorpus(algorithm))
        {
            string expected = reference.Stem(word);
            int length = production.Stem(word, buffer);
            string actual = length < 0 ? "<-1>" : new string(buffer, 0, length);
            if (string.Equals(actual, expected, StringComparison.Ordinal))
                continue;
            differences++;
            if (examples.Count < 10)
                examples.Add($"'{Escape(word)}' -> '{Escape(actual)}' (reference '{Escape(expected)}')");
        }

        Assert.True(differences == 0, $"{algorithm}: {differences} fuzz words differ from the reference: " + string.Join("; ", examples));
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void ResultsDoNotDependOnCallOrder(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        var words = SnowballFixtures.Vectors(algorithm).Select(static pair => pair.Input).Take(5_000).ToArray();
        var forward = words.Select(word => Stem(stemmer, word)).ToArray();

        var reversed = words.Reverse().Select(word => Stem(stemmer, word)).Reverse().ToArray();
        var order = Enumerable.Range(0, words.Length).ToArray();
        new Random(122).Shuffle(order); // NOSONAR: deterministic test order, not security.
        var shuffled = new string[words.Length];
        foreach (int index in order)
            shuffled[index] = Stem(stemmer, words[index]);

        Assert.Equal(forward, reversed);
        Assert.Equal(forward, shuffled);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void SharedStemmer_IsSafeAcrossThreads(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        Assert.IsAssignableFrom<IShareableSpanStemmer>(stemmer);
        var pairs = SnowballFixtures.Vectors(algorithm).Take(4_000).ToArray();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, thread =>
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < pairs.Length; i++)
                {
                    var (input, expected) = pairs[(i + thread * 257) % pairs.Length];
                    string actual = Stem(stemmer, input);
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                        failures.Add($"{input} -> {actual} (expected {expected})");
                }
            }
        });

        Assert.Empty(failures);
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void ShortBuffer_ReturnsMinusOneWithoutWriting(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        foreach (var (input, expected) in SnowballFixtures.Vectors(algorithm).Where(static pair => pair.Expected.Length > 0).Take(300))
        {
            var guarded = new char[expected.Length + 8];
            Array.Fill(guarded, '#');
            int tooSmall = stemmer.Stem(input, guarded.AsSpan(4, expected.Length - 1));
            Assert.Equal(-1, tooSmall);
            Assert.All(guarded, static ch => Assert.Equal('#', ch));

            int exact = stemmer.Stem(input, guarded.AsSpan(4, expected.Length));
            Assert.Equal(expected.Length, exact);
            Assert.Equal(expected, new string(guarded, 4, exact));
            Assert.Equal("####", new string(guarded, 0, 4));
            Assert.Equal("####", new string(guarded, 4 + expected.Length, 4));
        }
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void EmptyAndLongInputs_AreHandled(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        var reference = SnowballReference.Create(algorithm);

        Assert.Equal(0, stemmer.Stem(ReadOnlySpan<char>.Empty, Span<char>.Empty));
        Assert.Equal(string.Empty, ProductionStemmers.StemString(stemmer, string.Empty));

        string longWord = string.Concat(Enumerable.Repeat(SnowballFixtures.Vectors(algorithm)[^1].Input, 200));
        var buffer = new char[longWord.Length * 3];
        int length = stemmer.Stem(longWord, buffer);
        Assert.Equal(reference.Stem(longWord), new string(buffer, 0, length));
        // After a very long word the runtime releases its large buffer. A following word that still
        // needs more than the initial capacity must not copy the previous word's length (found by fuzz).
        string last = SnowballFixtures.Vectors(algorithm)[^1].Input;
        string medium = string.Concat(Enumerable.Repeat(last, (100 / last.Length) + 1));
        Assert.True(medium.Length > 64, medium);
        Assert.Equal(reference.Stem(medium), ProductionStemmers.StemString(stemmer, medium));
        // A short word after a very long one must not see the released buffer's contents.
        Assert.Equal(reference.Stem(longWord), ProductionStemmers.StemString(stemmer, longWord));
        var (input, expected) = SnowballFixtures.Vectors(algorithm)[0];
        Assert.Equal(expected, Stem(stemmer, input));
    }

    [Fact]
    public void GermanSharpS_ExpandsBeyondTheWordLength()
    {
        var german = new GermanStemmer();
        string expected = SnowballReference.Create("german").Stem("ßßßß");
        Assert.True(expected.Length > 4, expected);

        Assert.Equal(-1, german.Stem("ßßßß", new char[4]));
        var buffer = new char[expected.Length];
        Assert.Equal(expected.Length, german.Stem("ßßßß", buffer));
        Assert.Equal(expected, new string(buffer));
        Assert.Equal(expected, german.Stem("ßßßß"));
        Assert.Equal("uberlasst", german.Stem("überläßt"));
    }

    [Fact]
    public void GermanAnalyser_MultipleSharpS_DoesNotThrow()
    {
        // Before #122 the German pre-processing wrote past an input-sized buffer and threw.
        var analyser = AnalyserFactory.Create("de");
        var sink = new MaterialisingTokenSink();
        analyser.Analyse("ßßßß Straße Füße", sink);

        var reference = SnowballReference.Create("german");
        Assert.Equal(
            new[] { "ßßßß", "straße", "füße" }.Select(reference.Stem),
            sink.Tokens.Select(static token => token.Text));
        Assert.Equal([0, 5, 12], sink.Tokens.Select(static token => token.StartOffset));
        Assert.Equal([4, 11, 16], sink.Tokens.Select(static token => token.EndOffset));
    }

    [Theory]
    [MemberData(nameof(Algorithms))]
    public void SpanPath_DoesNotAllocateAfterWarmUp(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        var words = SnowballFixtures.Vectors(algorithm).Select(static pair => pair.Input).Take(10_000).ToArray();
        var buffer = new char[256];
        foreach (var word in words)
            stemmer.Stem(word, buffer);

        // One-off runtime work, such as tiered JIT promotion, can land in a measured pass, so the
        // steady state is a pass over every word that allocates nothing at all.
        var passes = new List<long>();
        for (int pass = 0; pass < 5; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            foreach (var word in words)
                stemmer.Stem(word, buffer);
            passes.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            if (passes[^1] == 0)
                return;
        }

        Assert.Fail($"{algorithm}: every pass over {words.Length} words allocated ({string.Join(", ", passes)} bytes).");
    }

    [Fact]
    public void EveryAlgorithm_HasAProductionRuntime()
    {
        foreach (var algorithm in Enum.GetValues<SnowballAlgorithm>())
            Assert.NotNull(SnowballPort.Create(algorithm));
    }

    private static string Stem(ISpanStemmer stemmer, string word)
    {
        Span<char> buffer = stackalloc char[512];
        int length = stemmer.Stem(word, buffer);
        Assert.True(length >= 0);
        return new string(buffer[..length]);
    }

    /// <summary>
    /// Words built from fragments of the language's pinned words, its alphabet and the characters the
    /// algorithms treat specially, so cursor, region and replacement edges are exercised.
    /// </summary>
    private static IEnumerable<string> FuzzCorpus(string algorithm)
    {
        var words = SnowballFixtures.Vectors(algorithm).Select(static pair => pair.Input).ToArray();
        var alphabet = words.SelectMany(static word => word).Distinct().ToList();
        alphabet.AddRange("ßäöüÄÖÜUYIaeouy'’-0123456789ёЁـًّءأإآ٠٩");
        var symbols = alphabet.Distinct().ToArray();
        var random = new Random(StableSeed(algorithm)); // NOSONAR: deterministic fuzz, not security.
        int count = algorithm == "arabic" ? ArabicFuzzWords : FuzzWords;
        var builder = new StringBuilder();
        for (int n = 0; n < count; n++)
        {
            builder.Clear();
            switch (n % 4)
            {
                case 0: // Random characters from the language and special sets.
                    for (int i = random.Next(0, 20); i > 0; i--)
                        builder.Append(symbols[random.Next(symbols.Length)]);
                    break;
                case 1: // A word with a fragment of another appended, to reach suffix rules.
                    builder.Append(words[random.Next(words.Length)]);
                    string tail = words[random.Next(words.Length)];
                    builder.Append(tail.AsSpan(random.Next(tail.Length + 1)));
                    break;
                case 2: // A prefix of one word with another word appended, to reach prefix rules.
                    string head = words[random.Next(words.Length)];
                    builder.Append(head.AsSpan(0, random.Next(head.Length + 1)));
                    builder.Append(words[random.Next(words.Length)]);
                    break;
                default: // A word with a random character inserted or replaced.
                    builder.Append(words[random.Next(words.Length)]);
                    int at = random.Next(builder.Length + 1);
                    builder.Insert(at, symbols[random.Next(symbols.Length)]);
                    if (builder.Length > 1 && random.Next(2) == 0)
                        builder[random.Next(builder.Length)] = symbols[random.Next(symbols.Length)];
                    break;
            }
            yield return builder.ToString();
        }

        for (int n = 0; n < 50; n++)
            yield return string.Concat(Enumerable.Range(0, 100).Select(_ => words[random.Next(words.Length)]));
    }

    // string.GetHashCode is randomised per process; the fuzz corpus must be reproducible.
    private static int StableSeed(string algorithm)
        => algorithm.Aggregate(122, static (seed, ch) => unchecked((seed * 31) + ch));

    private static string Escape(string text)
        => text.Length > 60 ? text[..60] + "…" : text;
}
