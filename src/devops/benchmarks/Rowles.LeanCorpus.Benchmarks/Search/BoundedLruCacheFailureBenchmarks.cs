using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures acquisition while an unrelated evicted value throws during disposal.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class BoundedLruCacheFailureBenchmarks
{
    [Benchmark(Description = "Acquire after unrelated eviction disposal failure")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool AcquireAfterThrowingEvictionDisposal()
    {
        using var cache = new BoundedLruCache<string, BenchmarkState>(
            1,
            StringComparer.Ordinal,
            static _ => { });

        var evicted = new BenchmarkState(throwOnDispose: true);
        using (cache.Acquire("evicted", () => evicted)) { }

        var loaded = new BenchmarkState();
        try
        {
            using var lease = cache.Acquire("loaded", () => loaded);
            return ReferenceEquals(loaded, lease.Value);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class BenchmarkState(bool throwOnDispose = false) : IDisposable
    {
        public void Dispose()
        {
            if (throwOnDispose)
                throw new InvalidOperationException("Injected eviction disposal failure.");
        }
    }
}
