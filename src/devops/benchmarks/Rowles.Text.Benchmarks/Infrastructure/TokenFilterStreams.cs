using Lucene.Net.Analysis.TokenAttributes;
using Rowles.LeanCorpus.Analysis;
using LeanToken = Rowles.LeanCorpus.Analysis.Token;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Pregenerated token streams for the token-filter suites. Each invocation filters
/// <see cref="TokensPerInvoke"/> tokens, so rows report per-token cost through
/// <c>OperationsPerInvoke</c> with no single-invocation floor and no tokeniser in timing.
/// </summary>
internal static class TokenFilterStreams
{
    /// <summary>Source tokens filtered per benchmark invocation.</summary>
    public const int TokensPerInvoke = 4096;

    /// <summary>
    /// Repeats <paramref name="pattern"/> into <see cref="TokensPerInvoke"/> tokens with
    /// whitespace-separated offsets. Each pattern entry gives its term and position increment.
    /// </summary>
    public static LeanToken[] Build(params (string Term, int PositionIncrement)[] pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (TokensPerInvoke % pattern.Length != 0)
            throw new InvalidOperationException($"A token pattern of {pattern.Length} entries does not divide {TokensPerInvoke}.");

        var tokens = new LeanToken[TokensPerInvoke];
        int offset = 0;
        for (int i = 0; i < tokens.Length; i++)
        {
            var (term, increment) = pattern[i % pattern.Length];
            tokens[i] = new LeanToken(term, offset, offset + term.Length, positionIncrement: increment);
            offset += term.Length + 1;
        }

        return tokens;
    }

    /// <summary>Applies a LeanCorpus filter to the stream and finishes it, which is the filter's reset path.</summary>
    public static void ApplyLean(LeanToken[] source, ISpanTokenFilter filter, ISpanTokenSink sink)
    {
        foreach (var token in source)
            filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.Payload, sink);
        filter.Finish(sink);
    }

    /// <summary>Captures the terms a LeanCorpus filter emits over the stream.</summary>
    public static string[] CaptureLean(LeanToken[] source, ISpanTokenFilter filter)
    {
        var sink = new TermCaptureSink();
        ApplyLean(source, filter, sink);
        return [.. sink.Terms];
    }

    /// <summary>Consumes a Lucene.NET stream, returning the token count.</summary>
    public static int ApplyLucene(Lucene.Net.Analysis.TokenStream stream)
    {
        int total = 0;
        stream.Reset();
        while (stream.IncrementToken())
            total++;
        stream.End();
        return total;
    }

    /// <summary>Captures the terms of a Lucene.NET stream.</summary>
    public static string[] CaptureLucene(Lucene.Net.Analysis.TokenStream stream)
    {
        var term = stream.AddAttribute<ICharTermAttribute>();
        var terms = new List<string>();
        stream.Reset();
        while (stream.IncrementToken())
            terms.Add(term.ToString());
        stream.End();
        return [.. terms];
    }

    /// <summary>Fails a row whose output changed from the setup-verified count.</summary>
    public static int Validate(int actual, int expected, string row)
    {
        if (actual != expected)
            throw new InvalidOperationException($"{row} token-filter output changed: expected {expected}, got {actual}.");
        return actual;
    }

    private sealed class TermCaptureSink : ISpanTokenSink
    {
        public List<string> Terms { get; } = [];

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = LeanToken.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
            => Terms.Add(text.ToString());
    }
}

/// <summary>
/// Replays pregenerated tokens into Lucene.NET filters, so no tokeniser runs inside timing.
/// <see cref="ResetCount"/> proves that a reused filter chain reached its reset path.
/// </summary>
internal sealed class ReplayTokenStream : Lucene.Net.Analysis.TokenStream
{
    private readonly LeanToken[] _tokens;
    private readonly ICharTermAttribute _term;
    private readonly IOffsetAttribute _offset;
    private readonly IPositionIncrementAttribute _increment;
    private readonly ITypeAttribute _type;
    private int _index;

    public ReplayTokenStream(LeanToken[] tokens)
    {
        _tokens = tokens;
        _term = AddAttribute<ICharTermAttribute>();
        _offset = AddAttribute<IOffsetAttribute>();
        _increment = AddAttribute<IPositionIncrementAttribute>();
        _type = AddAttribute<ITypeAttribute>();
    }

    /// <summary>Number of times the stream has been reset.</summary>
    public int ResetCount { get; private set; }

    public override bool IncrementToken()
    {
        if (_index >= _tokens.Length)
            return false;
        ClearAttributes();
        var token = _tokens[_index++];
        _term.SetEmpty().Append(token.Text);
        _offset.SetOffset(token.StartOffset, token.EndOffset);
        _increment.PositionIncrement = token.PositionIncrement;
        _type.Type = token.Type;
        return true;
    }

    public override void Reset()
    {
        base.Reset();
        _index = 0;
        ResetCount++;
    }

    public override void End()
    {
        base.End();
        int final = _tokens.Length == 0 ? 0 : _tokens[^1].EndOffset;
        _offset.SetOffset(final, final);
    }
}
