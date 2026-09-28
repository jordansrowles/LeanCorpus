using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Composable analyser that runs a tokeniser followed by a chain of span filters.
/// </summary>
/// <remarks>
/// Each call uses an execution context with its own filter instances and routing
/// sinks. The built-in language filters use a thread-affine context after successful
/// calls; other filters receive a fresh context for every call. Filters must return
/// an independent stateful instance from <see cref="ISpanTokenFilter.Clone"/>;
/// stateless filters may return themselves.
/// Concurrent calls are supported when the configured tokeniser implements
/// <see cref="IShareableSpanTokeniser"/>. For a thread-local tokeniser, create one
/// analyser per worker with <see cref="IThreadLocalAnalyser.CreateThreadLocalAnalyser"/>.
/// Tokenisers without an ownership marker remain usable by a single caller but do
/// not provide a concurrent-use guarantee.
/// </remarks>
public sealed class Analyser : IThreadLocalAnalyser
{
    [ThreadStatic]
    private static ConditionalWeakTable<Analyser, AnalysisContext>? t_threadContexts;

    private readonly ISpanTokeniser _tokeniser;
    private readonly ISpanTokenFilter[] _filters;
    private readonly bool _canReuseThreadContext;

    /// <summary>
    /// Initialises a new <see cref="Analyser"/> with the specified span tokeniser and optional filter chain.
    /// </summary>
    /// <param name="tokeniser">The span tokeniser used to split input into raw tokens.</param>
    /// <param name="filters">Zero or more span filters to apply in order.</param>
    public Analyser(ISpanTokeniser tokeniser, params ISpanTokenFilter[] filters)
    {
        _tokeniser = tokeniser ?? throw new ArgumentNullException(nameof(tokeniser));
        ArgumentNullException.ThrowIfNull(filters);
        _filters = (ISpanTokenFilter[])filters.Clone();

        _canReuseThreadContext = true;
        foreach (var filter in _filters)
        {
            if (filter is not IReusableAnalysisFilter || filter is IAnalysisContextFilter)
            {
                _canReuseThreadContext = false;
                break;
            }
        }
    }

    /// <summary>Creates a new <see cref="Analyser"/> with independently owned mutable components.</summary>
    /// <remarks>
    /// Each filter's <see cref="ISpanTokenFilter.Clone"/> is called. A tokeniser must
    /// explicitly declare itself shareable or provide an independent instance.
    /// </remarks>
    internal Analyser Clone()
    {
        var filters = new ISpanTokenFilter[_filters.Length];
        for (int i = 0; i < _filters.Length; i++)
            filters[i] = _filters[i].Clone();
        ISpanTokeniser tokeniser = _tokeniser switch
        {
            IThreadLocalSpanTokeniser owned => owned.CreateThreadLocalTokeniser(),
            IShareableSpanTokeniser => _tokeniser,
            _ => throw new InvalidOperationException(
                $"Tokeniser '{_tokeniser.GetType().FullName}' has no explicit concurrent ownership contract.")
        };
        return new Analyser(tokeniser, filters);
    }

    /// <inheritdoc/>
    public IAnalyser CreateThreadLocalAnalyser() => Clone();

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_filters.Length == 0)
        {
            _tokeniser.Tokenise(input, sink);
            return;
        }

        AnalysisContext context;
        bool reusedContext;
        if (_canReuseThreadContext)
            reusedContext = TryBeginReusableContext(sink, out context!);
        else
        {
            reusedContext = false;
            context = null!;
        }

        if (!reusedContext)
        {
            context = new AnalysisContext(_filters);
            context.Begin(sink);
        }

        bool completed = false;
        try
        {
            _tokeniser.Tokenise(input, context);
            context.Finish();
            context.Complete();
            completed = true;
        }
        finally
        {
            if (reusedContext && completed)
            {
                // Reused filters are stateless or thread-affine and have completed
                // successfully. Keep their execution state, but release this sink.
                context.Release();
            }
            else
            {
                // Failed reusable executions are discarded so partially-mutated
                // filter state cannot affect a later analysis on this thread.
                context.Clear();
                if (reusedContext)
                    t_threadContexts?.Remove(this);
            }
        }
    }

    private bool TryBeginReusableContext(ISpanTokenSink sink, out AnalysisContext context)
    {
        var contexts = t_threadContexts ??= new ConditionalWeakTable<Analyser, AnalysisContext>();
        if (!contexts.TryGetValue(this, out context!))
        {
            context = new AnalysisContext(_filters);
            contexts.Add(this, context);
        }

        if (context.TryBegin(sink))
            return true;

        // Recursive analysis on the same thread gets a one-shot context rather
        // than overwriting the active call's sink or stage state.
        context = null!;
        return false;
    }

    private sealed class AnalysisContext : ISpanTokenSink
    {
        private ISpanTokenFilter[] _filters;
        private StageSink[] _stageSinks;
        private ISpanTokenSink? _finalSink;
        private bool _active;

        public AnalysisContext(ISpanTokenFilter[] filterConfiguration)
        {
            _filters = new ISpanTokenFilter[filterConfiguration.Length];
            for (int i = 0; i < filterConfiguration.Length; i++)
            {
                var configuredFilter = filterConfiguration[i];
                _filters[i] = (configuredFilter is IAnalysisContextFilter contextFilter
                    ? contextFilter.CreateExecutionFilter()
                    : configuredFilter.Clone())
                    ?? throw new InvalidOperationException(
                        $"Filter '{configuredFilter.GetType().FullName}' returned null from its execution factory.");
            }

            _stageSinks = new StageSink[_filters.Length];
            for (int i = 0; i < _stageSinks.Length; i++)
                _stageSinks[i] = new StageSink(this, i + 1);
        }

        public bool TryBegin(ISpanTokenSink finalSink)
        {
            if (_active)
                return false;

            _finalSink = finalSink;
            _active = true;
            return true;
        }

        public void Begin(ISpanTokenSink finalSink)
        {
            if (!TryBegin(finalSink))
                throw new InvalidOperationException("An analysis execution context is already active.");
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            ApplyAt(0, text, startOffset, endOffset, type, positionIncrement, 1, payload);
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            int positionLength,
            byte[]? payload)
        {
            ApplyAt(0, text, startOffset, endOffset, type, positionIncrement, positionLength, payload);
        }

        /// <summary>
        /// Signals end-of-stream to every filter in the chain so stateful filters can
        /// flush buffered tokens downstream.
        /// </summary>
        public void Finish()
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                ISpanTokenSink nextSink = i + 1 < _filters.Length ? _stageSinks[i] : _finalSink!;
                _filters[i].Finish(nextSink);
            }
        }

        public void Complete()
        {
            foreach (var filter in _filters)
            {
                if (filter is IAnalysisContextFilter contextFilter)
                    contextFilter.CompleteAnalysis();
            }
        }

        public void Release()
        {
            _finalSink = null;
            _active = false;
        }

        public void Clear()
        {
            Release();
            Array.Clear(_filters);
            Array.Clear(_stageSinks);
            _filters = [];
            _stageSinks = [];
        }

        private void ApplyAt(
            int filterIndex,
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            int positionLength,
            byte[]? payload)
        {
            Token.ValidatePositionLength(positionLength);

            if (filterIndex >= _filters.Length)
            {
                _finalSink!.Add(text, startOffset, endOffset, type, positionIncrement, positionLength, payload);
                return;
            }

            var stageSink = _stageSinks[filterIndex];
            int previousPositionLength = stageSink.PositionLength;
            stageSink.PositionLength = positionLength;
            try
            {
                _filters[filterIndex].Apply(text, startOffset, endOffset, type, positionIncrement,
                    positionLength, payload, stageSink);
            }
            finally
            {
                stageSink.PositionLength = previousPositionLength;
            }
        }

        private sealed class StageSink : ISpanTokenSink, IPositionLengthContextSink
        {
            private readonly AnalysisContext _owner;
            private readonly int _nextFilterIndex;
            private int _positionLength = 1;

            public StageSink(AnalysisContext owner, int nextFilterIndex)
            {
                _owner = owner;
                _nextFilterIndex = nextFilterIndex;
            }

            public int PositionLength
            {
                get => _positionLength;
                set => _positionLength = Token.ValidatePositionLength(value);
            }

            public void Add(
                ReadOnlySpan<char> text,
                int startOffset,
                int endOffset,
                string type = Token.DefaultType,
                int positionIncrement = 1,
                byte[]? payload = null)
            {
                _owner.ApplyAt(_nextFilterIndex, text, startOffset, endOffset, type, positionIncrement,
                    PositionLength, payload);
            }

            public void Add(
                ReadOnlySpan<char> text,
                int startOffset,
                int endOffset,
                string type,
                int positionIncrement,
                int positionLength,
                byte[]? payload)
            {
                _owner.ApplyAt(_nextFilterIndex, text, startOffset, endOffset, type, positionIncrement, positionLength, payload);
            }
        }
    }
}
