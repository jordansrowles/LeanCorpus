using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.Text.Benchmarks;

/// <summary>
/// Measures capture and forwarding allocations for a small token graph.
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class CachingGraphEdgeBenchmarks
{
    private static readonly Token[] GraphEdges =
    [
        new("new", 0, 3),
        new("nyc", 0, 8, positionIncrement: 0, positionLength: 2),
        new("york", 4, 8),
        new("park", 9, 13)
    ];

    private ISpanTokenFilter _filter = null!;
    private CountingTokenSink _sink = null!;

    [IterationSetup]
    public void SetUpIteration()
    {
        _filter = new CachingTokenFilter();
        _sink = new CountingTokenSink();
    }

    [Benchmark]
    public int CaptureAndForwardGraphEdges()
    {
        foreach (var token in GraphEdges)
        {
            _filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset,
                token.Type, token.PositionIncrement, token.PositionLength, token.Payload, _sink);
        }

        return _sink.Count;
    }
}
