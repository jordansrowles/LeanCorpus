using System.Globalization;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>One analysed token, as compared across engines (ADR045).</summary>
internal readonly record struct AnalysedToken(string Term, int PositionIncrement, int PositionLength, int StartOffset, int EndOffset)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Term}+{PositionIncrement}/{PositionLength}@{StartOffset}-{EndOffset}");
}

/// <summary>The only differences a preflight may accept.</summary>
internal sealed record PreflightAllowList(bool CjkOnNonAsciiDocuments = false, string? NamedDifference = null)
{
    public static PreflightAllowList None { get; } = new();

    public static PreflightAllowList Cjk { get; } = new(CjkOnNonAsciiDocuments: true);

    public static PreflightAllowList Named(string name) => new(NamedDifference: name);
}

/// <summary>Counts from one preflight comparison.</summary>
internal sealed record PreflightResult(string Case, int Documents, int Identical, int AllowedDifferences, int Tokens);

/// <summary>
/// Compares <c>(term, position increment, position length, offsets)</c> across engines on the first
/// 500 corpus documents, outside timing, and fails setup on any difference that the case's
/// allow-list does not name (ADR045). The allow-list permits only CJK differences on documents that
/// contain non-ASCII text, or a named, declared difference such as KStem.
/// </summary>
internal static class TextOutputPreflight
{
    public const int DocumentCount = 500;

    public static PreflightResult Compare(
        string caseName,
        IReadOnlyList<string> documents,
        Func<string, IReadOnlyList<AnalysedToken>> lean,
        Func<string, IReadOnlyList<AnalysedToken>> lucene,
        PreflightAllowList allow)
    {
        ArgumentNullException.ThrowIfNull(documents);
        int count = Math.Min(DocumentCount, documents.Count);
        int identical = 0, allowed = 0, tokens = 0;
        var failures = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var left = lean(documents[i]);
            var right = lucene(documents[i]);
            tokens += left.Count;
            if (left.SequenceEqual(right))
            {
                identical++;
                continue;
            }

            if (IsAllowed(documents[i], left, right, allow))
            {
                allowed++;
                continue;
            }

            if (failures.Count < 3)
                failures.Add(Describe(i, left, right));
        }

        var result = new PreflightResult(caseName, count, identical, allowed, tokens);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark preflight text {caseName}: documents={count}, identical={identical}, allowed={allowed}, tokens={tokens}, allow={Describe(allow)}"));
        if (identical + allowed != count)
            throw new InvalidOperationException(
                $"Text output preflight '{caseName}' found {count - identical - allowed} documents with differences not on the allow-list ({Describe(allow)}):"
                + Environment.NewLine + string.Join(Environment.NewLine, failures));
        return result;
    }

    /// <summary>
    /// A difference is allowed when the case names one, or when the document has non-ASCII text and
    /// every differing token on either side contains a CJK character.
    /// </summary>
    internal static bool IsAllowed(string document, IReadOnlyList<AnalysedToken> lean, IReadOnlyList<AnalysedToken> lucene, PreflightAllowList allow)
    {
        if (allow.NamedDifference is not null)
            return true;
        if (!allow.CjkOnNonAsciiDocuments || document.All(char.IsAscii))
            return false;

        var leftOnly = lean.Except(lucene).ToArray();
        var rightOnly = lucene.Except(lean).ToArray();
        return leftOnly.Concat(rightOnly).All(static token => token.Term.Any(IsCjk))
            && lean.Where(static token => !token.Term.Any(IsCjk)).SequenceEqual(lucene.Where(static token => !token.Term.Any(IsCjk)));
    }

    internal static bool IsCjk(char character)
        => character is >= '぀' and <= 'ヿ'   // Hiragana, Katakana
            or >= '㐀' and <= '䶿'            // CJK extension A
            or >= '一' and <= '鿿'            // CJK unified ideographs
            or >= '가' and <= '힯'            // Hangul syllables
            or >= 'ᄀ' and <= 'ᇿ'            // Hangul jamo
            or >= '豈' and <= '﫿'            // CJK compatibility ideographs
            or >= 'ｦ' and <= 'ﾟ';           // half-width Katakana

    /// <summary>Captures a LeanCorpus run into tokens.</summary>
    public static IReadOnlyList<AnalysedToken> CaptureLean(Action<ISpanTokenSink> run)
    {
        var sink = new CapturingSink();
        run(sink);
        return sink.Tokens;
    }

    /// <summary>Captures a Lucene.NET token stream into tokens, consuming and ending it.</summary>
    public static IReadOnlyList<AnalysedToken> CaptureLucene(TokenStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var term = stream.AddAttribute<ICharTermAttribute>();
        var increment = stream.AddAttribute<IPositionIncrementAttribute>();
        var length = stream.AddAttribute<IPositionLengthAttribute>();
        var offset = stream.AddAttribute<IOffsetAttribute>();
        var tokens = new List<AnalysedToken>();
        stream.Reset();
        while (stream.IncrementToken())
            tokens.Add(new AnalysedToken(term.ToString(), increment.PositionIncrement, length.PositionLength, offset.StartOffset, offset.EndOffset));
        stream.End();
        return tokens;
    }

    private static string Describe(PreflightAllowList allow)
        => allow.NamedDifference is { } name ? $"named:{name}" : allow.CjkOnNonAsciiDocuments ? "cjk-on-non-ascii" : "none";

    private static string Describe(int index, IReadOnlyList<AnalysedToken> lean, IReadOnlyList<AnalysedToken> lucene)
    {
        int first = 0;
        while (first < lean.Count && first < lucene.Count && lean[first] == lucene[first])
            first++;
        string Window(IReadOnlyList<AnalysedToken> tokens) => string.Join(" ", tokens.Skip(Math.Max(0, first - 1)).Take(4));
        return string.Create(CultureInfo.InvariantCulture,
            $"  document {index}, token {first}: LeanCorpus [{Window(lean)}] (of {lean.Count}), Lucene.NET [{Window(lucene)}] (of {lucene.Count})");
    }

    private sealed class CapturingSink : ISpanTokenSink
    {
        public List<AnalysedToken> Tokens { get; } = [];

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Rowles.LeanCorpus.Analysis.Token.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
            => Tokens.Add(new AnalysedToken(text.ToString(), positionIncrement, 1, startOffset, endOffset));

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type, int positionIncrement,
            int positionLength, byte[]? payload)
            => Tokens.Add(new AnalysedToken(text.ToString(), positionIncrement, positionLength, startOffset, endOffset));
    }
}

/// <summary>
/// A sink that reads every term by copying it into a reused buffer, so reading rows pay for term
/// consumption as an index writer would. This is the comparison default; counting rows are named
/// <c>_CountOnly</c>.
/// </summary>
internal sealed class ReadingTokenSink : ISpanTokenSink
{
    private char[] _buffer = new char[64];

    public int Count { get; private set; }

    /// <summary>Sum of copied characters, so the copy cannot be elided.</summary>
    public long Characters { get; private set; }

    public void Reset()
    {
        Count = 0;
        Characters = 0;
    }

    public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Rowles.LeanCorpus.Analysis.Token.DefaultType,
        int positionIncrement = 1, byte[]? payload = null)
    {
        if (text.Length > _buffer.Length)
            _buffer = new char[Math.Max(text.Length, _buffer.Length * 2)];
        text.CopyTo(_buffer);
        Count++;
        Characters += text.Length;
    }
}

/// <summary>Reads Lucene.NET terms the same way <see cref="ReadingTokenSink"/> does.</summary>
internal static class LuceneTermReader
{
    [ThreadStatic]
    private static char[]? s_buffer;

    /// <summary>Consumes a stream, copying each term into a reused buffer; returns the token count.</summary>
    public static int ReadAll(TokenStream stream)
    {
        var term = stream.AddAttribute<ICharTermAttribute>();
        var buffer = s_buffer ??= new char[64];
        int count = 0;
        stream.Reset();
        while (stream.IncrementToken())
        {
            if (term.Length > buffer.Length)
                s_buffer = buffer = new char[Math.Max(term.Length, buffer.Length * 2)];
            Array.Copy(term.Buffer, buffer, term.Length);
            count++;
        }

        stream.End();
        return count;
    }
}
