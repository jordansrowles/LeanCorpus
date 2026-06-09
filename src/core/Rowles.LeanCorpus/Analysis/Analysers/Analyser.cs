using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Composable analyser that runs a tokeniser followed by a chain of span filters.
/// </summary>
public sealed class Analyser : IAnalyser
{
    private readonly ISpanTokeniser _tokeniser;
    private readonly ISpanTokenFilter[] _filters;
    private readonly FilteringSpanTokenSink _filteringSink = new();

    /// <summary>
    /// Initialises a new <see cref="Analyser"/> with the specified span tokeniser and optional filter chain.
    /// </summary>
    /// <param name="tokeniser">The span tokeniser used to split input into raw tokens.</param>
    /// <param name="filters">Zero or more span filters to apply in order.</param>
    public Analyser(ISpanTokeniser tokeniser, params ISpanTokenFilter[] filters)
    {
        _tokeniser = tokeniser;
        _filters = filters;
    }

    /// <summary>Creates a new <see cref="Analyser"/> sharing the same tokeniser and filters.</summary>
    /// <remarks>
    /// After the tokeniser and all filters have been made stateless per-call, the shared references
    /// are safe. Only <see cref="FilteringSpanTokenSink"/> needs to be per-instance, which this
    /// method ensures by constructing a fresh <see cref="Analyser"/>.
    /// </remarks>
    internal Analyser Clone() => new(_tokeniser, _filters);

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_filters.Length == 0)
        {
            _tokeniser.Tokenise(input, sink);
        }
        else
        {
            _filteringSink.Reset(_filters, sink);
            _tokeniser.Tokenise(input, _filteringSink);
        }
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
}
