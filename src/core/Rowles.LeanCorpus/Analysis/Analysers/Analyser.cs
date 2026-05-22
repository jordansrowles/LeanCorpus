using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Composable analyser that runs a tokeniser followed by a chain of filters.
/// </summary>
public sealed class Analyser : IAnalyser, ISpanAnalyser
{
    private readonly ISpanTokeniser _tokeniser;
    private readonly ITokenFilter[] _filters;
    private readonly ISpanTokenFilter[]? _spanFilters;
    private readonly FilteringSpanTokenSink _filteringSink = new();
    private readonly MaterialisingTokenSink _materialisingSink = new();

    /// <summary>
    /// Initialises a new <see cref="Analyser"/> with the specified span tokeniser and optional filter chain.
    /// </summary>
    /// <param name="tokeniser">The span tokeniser used to split input into raw tokens.</param>
    /// <param name="filters">Zero or more filters to apply to the token list in order.</param>
    public Analyser(ISpanTokeniser tokeniser, params ITokenFilter[] filters)
    {
        _tokeniser = tokeniser;
        _filters = filters;
        _spanFilters = TryGetSpanFilters(filters);
    }

    /// <summary>
    /// Creates a new <see cref="Analyser"/> from a legacy <see cref="ITokeniser"/> by wrapping it
    /// in a span adapter. Prefer passing an <see cref="ISpanTokeniser"/> directly.
    /// </summary>
    /// <param name="tokeniser">The legacy tokeniser to adapt.</param>
    /// <param name="filters">Zero or more filters to apply to the token list in order.</param>
    public static Analyser FromTokeniser(ITokeniser tokeniser, params ITokenFilter[] filters)
        => new(new TokeniserToSpanAdapter(tokeniser), filters);

    /// <summary>Creates a new <see cref="Analyser"/> sharing the same tokeniser and filters.</summary>
    /// <remarks>
    /// After the tokeniser and all filters have been made stateless per-call, the shared references
    /// are safe. Only <see cref="FilteringSpanTokenSink"/> needs to be per-instance, which this
    /// method ensures by constructing a fresh <see cref="Analyser"/>.
    /// </remarks>
    internal Analyser Clone() => new(_tokeniser, _filters);

    /// <inheritdoc/>
    public List<Token> Analyse(ReadOnlySpan<char> input)
    {
        _materialisingSink.Reset();
        _tokeniser.Tokenise(input, _materialisingSink);
        var tokens = _materialisingSink.Tokens;
        foreach (var filter in _filters)
            filter.Apply(tokens);
        return tokens;
    }

    /// <inheritdoc/>
    public bool TryAnalyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_spanFilters is null)
            return false;

        if (_spanFilters.Length == 0)
        {
            _tokeniser.Tokenise(input, sink);
        }
        else
        {
            _filteringSink.Reset(_spanFilters, sink);
            _tokeniser.Tokenise(input, _filteringSink);
        }

        return true;
    }

    private static ISpanTokenFilter[]? TryGetSpanFilters(ITokenFilter[] filters)
    {
        if (filters.Length == 0)
            return [];

        var spanFilters = new ISpanTokenFilter[filters.Length];
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] is not ISpanTokenFilter spanFilter)
                return null;

            spanFilters[i] = spanFilter;
        }

        return spanFilters;
    }

    private sealed class FilteringSpanTokenSink : ISpanTokenSink
    {
        private ISpanTokenFilter[] _filters = [];
        private StageSink[] _stageSinks = [];
        private ISpanTokenSink _finalSink = null!;

        public void Reset(ISpanTokenFilter[] filters, ISpanTokenSink finalSink)
        {
            _filters = filters;
            _finalSink = finalSink;

            if (_stageSinks.Length < filters.Length)
            {
                var stageSinks = new StageSink[filters.Length];
                for (int i = 0; i < stageSinks.Length; i++)
                    stageSinks[i] = new StageSink(this, i + 1);
                _stageSinks = stageSinks;
            }
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            ApplyAt(0, text, startOffset, endOffset, type, positionIncrement, payload);
        }

        private void ApplyAt(
            int filterIndex,
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            byte[]? payload)
        {
            if (filterIndex >= _filters.Length)
            {
                _finalSink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
                return;
            }

            _filters[filterIndex].Apply(text, startOffset, endOffset, type, positionIncrement, payload, _stageSinks[filterIndex]);
        }

        private sealed class StageSink : ISpanTokenSink
        {
            private readonly FilteringSpanTokenSink _owner;
            private readonly int _nextFilterIndex;

            public StageSink(FilteringSpanTokenSink owner, int nextFilterIndex)
            {
                _owner = owner;
                _nextFilterIndex = nextFilterIndex;
            }

            public void Add(
                ReadOnlySpan<char> text,
                int startOffset,
                int endOffset,
                string type = Token.DefaultType,
                int positionIncrement = 1,
                byte[]? payload = null)
            {
                _owner.ApplyAt(_nextFilterIndex, text, startOffset, endOffset, type, positionIncrement, payload);
            }
        }
    }

    private sealed class MaterialisingTokenSink : ISpanTokenSink
    {
        public List<Token> Tokens { get; } = [];

        public void Reset() => Tokens.Clear();

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
        }
    }

    /// <summary>
    /// Adapts a legacy <see cref="ITokeniser"/> to the <see cref="ISpanTokeniser"/> interface.
    /// Used by <see cref="Analyser.FromTokeniser"/> to bridge tokenisers that have not yet been
    /// migrated to the span path.
    /// </summary>
    internal sealed class TokeniserToSpanAdapter : ISpanTokeniser
    {
        private readonly ITokeniser _inner;

        public TokeniserToSpanAdapter(ITokeniser inner) => _inner = inner;

        public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            var tokens = _inner.Tokenise(input);
            foreach (var t in tokens)
                sink.Add(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload);
        }
    }
}
