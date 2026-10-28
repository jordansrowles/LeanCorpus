using BenchmarkDotNet.Attributes;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Fixed workloads for comparing scalar correctness against the issue-start implementation.</summary>
[MemoryDiagnoser]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class UnicodeScalarBenchmarks
{
    private const string Ascii = "the quick brown fox jumps over the lazy dog 0123456789";
    private const string Mixed = "A\U00010400B \U00010428\U000104A3C \U0001F600";
    private const string Identifier = "PowerShot100WiFiSchools_test";
    private readonly LetterTokeniser _letter = new();
    private readonly NGramTokeniser _ngram = new(1, 3);
    private readonly EdgeNGramTokeniser _edge = new(1, 3);
    private readonly LengthFilter _length = new(1, 100);
    private readonly TruncateTokenFilter _noTruncate = new(100);
    private readonly TruncateTokenFilter _truncate = new(8);
    private readonly ReverseStringFilter _reverse = new();
    private readonly DecimalDigitFilter _digits = new();
    private readonly WordDelimiterFilter _words = new();
    private readonly AccentFoldingFilter _accents = new();
    private readonly MeasuringSink _sink = new();
    private readonly string _longAccents = string.Concat(Enumerable.Repeat("café résumé naïve ", 32));

    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int LetterAscii() { _sink.Reset(); _letter.Tokenise(Ascii, _sink); return _sink.Total; }
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int NGramAscii() => NGrams(Ascii);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int NGramSupplementary() => NGrams(Mixed);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int EdgeAscii() => Edges(Ascii);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int EdgeSupplementary() => Edges(Mixed);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int NGramSinkAscii() { _sink.Reset(); _ngram.Tokenise(Ascii, _sink); return _sink.Total; }
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int EdgeSinkAscii() { _sink.Reset(); _edge.Tokenise(Ascii, _sink); return _sink.Total; }
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int LengthAscii() => Filter(_length, "token");
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int TruncateNoOpAscii() => Filter(_noTruncate, "token");
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int TruncateAscii() => Filter(_truncate, Ascii);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int ReverseAscii() => Filter(_reverse, Ascii);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int ReverseSupplementary() => Filter(_reverse, Mixed);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int DecimalNoOpAscii() => Filter(_digits, Ascii);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int DecimalSupplementary() => Filter(_digits, Mixed);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int WordDelimiterAscii() => Filter(_words, Identifier);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int WordDelimiterSupplementary() => Filter(_words, Mixed);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int AccentNoOpAscii() => Filter(_accents, Ascii);
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int AccentBmp() => Filter(_accents, "café résumé naïve");
    [Benchmark, MethodImpl(MethodImplOptions.NoInlining)] public int AccentPooled() => Filter(_accents, _longAccents);

    private int NGrams(string input)
    {
        int result = 0;
        foreach (var token in _ngram.EnumerateTokens(input)) result += token.Text.Length;
        return result;
    }
    private int Edges(string input)
    {
        int result = 0;
        foreach (var token in _edge.EnumerateTokens(input)) result += token.Text.Length;
        return result;
    }
    private int Filter(ISpanTokenFilter filter, string input)
    {
        _sink.Reset();
        filter.Apply(input, 0, input.Length, Token.DefaultType, 1, null, _sink);
        return _sink.Total;
    }
    private sealed class MeasuringSink : ISpanTokenSink
    {
        public int Total { get; private set; }
        public void Reset() => Total = 0;
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
            => Total += text.Length;
    }
}
