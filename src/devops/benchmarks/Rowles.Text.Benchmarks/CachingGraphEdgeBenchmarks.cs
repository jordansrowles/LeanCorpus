using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.Text.Benchmarks;

/// <summary>
/// Compares the pre-RT-06 legacy cache path with graph-aware capture
/// for a repeated small token graph.
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class CachingGraphEdgeBenchmarks
{
    private const int GraphRepetitions = 32_768;

    private static readonly Token[] GraphEdges =
    [
        new("new", 0, 3),
        new("nyc", 0, 8, positionIncrement: 0, positionLength: 2),
        new("york", 4, 8),
        new("park", 9, 13)
    ];

    private ISpanTokenFilter _legacyFilter = null!;
    private ISpanTokenFilter _graphAwareFilter = null!;
    private CountingTokenSink _legacySink = null!;
    private CountingTokenSink _graphAwareSink = null!;

    [IterationSetup]
    public void SetUpIteration()
    {
        _legacyFilter = new LegacyCachingTokenFilter();
        _graphAwareFilter = new CachingTokenFilter();
        _legacySink = new CountingTokenSink();
        _graphAwareSink = new CountingTokenSink();
    }

    [Benchmark(
        Baseline = true,
        Description = "Pre-fix legacy cache",
        OperationsPerInvoke = GraphRepetitions)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CaptureAndForwardGraphEdges_Legacy()
        => CaptureAndForwardGraphEdges(_legacyFilter, _legacySink);

    [Benchmark(Description = "Graph-aware cache", OperationsPerInvoke = GraphRepetitions)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CaptureAndForwardGraphEdges_GraphAware()
        => CaptureAndForwardGraphEdges(_graphAwareFilter, _graphAwareSink);

    private static int CaptureAndForwardGraphEdges(ISpanTokenFilter filter, CountingTokenSink sink)
    {
        for (int repetition = 0; repetition < GraphRepetitions; repetition++)
        {
            foreach (var token in GraphEdges)
            {
                filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset,
                    token.Type, token.PositionIncrement, token.PositionLength, token.Payload, sink);
            }
        }

        return sink.Count;
    }

    /// <summary>
    /// Reproduces the former cache implementation: it only accepts the legacy edge
    /// and therefore reaches the downstream sink through the default graph adapter.
    /// </summary>
    private sealed class LegacyCachingTokenFilter : ISpanTokenFilter
    {
        private readonly List<Token> _tokens = [];

        public void Apply(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            byte[]? payload,
            ISpanTokenSink sink)
        {
            _tokens.Add(new Token(text.ToString(), startOffset, endOffset,
                type, positionIncrement, payload));
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
        }
    }
}
