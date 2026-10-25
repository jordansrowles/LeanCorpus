using BenchmarkDotNet.Attributes;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures generic reader acquisition, refresh, retained-reader lookup and diagnostics.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class ReaderManagerLifecycleBenchmarks
{
    private ReaderManager<BenchmarkReader>? _stableManager;
    private ReaderManager<BenchmarkReader>? _refreshingManager;
    private ReaderLease<BenchmarkReader> _retainedLease;
    private int _nextVersion;

    [GlobalSetup]
    public void Setup()
    {
        _stableManager = new ReaderManager<BenchmarkReader>(
            static () => new BenchmarkReader(0),
            static _ => null,
            TimeSpan.FromDays(1));
        _refreshingManager = new ReaderManager<BenchmarkReader>(
            () => new BenchmarkReader(Interlocked.Increment(ref _nextVersion)),
            _ => new BenchmarkReader(Interlocked.Increment(ref _nextVersion)),
            TimeSpan.FromDays(1));
        _retainedLease = _refreshingManager.AcquireLease();
        _refreshingManager.MaybeRefresh();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _retainedLease.Dispose();
        _stableManager?.Dispose();
        _refreshingManager?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int AcquireAndRelease()
    {
        var reader = _stableManager!.Acquire();
        try { return reader.Version; }
        finally { _stableManager.Release(reader); }
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int AcquireLease()
    {
        using var lease = _stableManager!.AcquireLease();
        return lease.Reader.Version;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool NoOpRefresh() => _stableManager!.MaybeRefresh();

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool PublishReplacement() => _refreshingManager!.MaybeRefresh();

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int AcquireRetainedReader()
    {
        if (!_refreshingManager!.TryAcquire(static reader => reader.Version == 1, out var lease))
            return -1;
        using (lease)
            return lease.Reader.Version;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int GetDiagnostics()
    {
        var diagnostics = _refreshingManager!.GetDiagnostics();
        return diagnostics.ActiveReaders + diagnostics.ActiveLeases;
    }

    private sealed class BenchmarkReader(int version) : IDisposable
    {
        public int Version { get; } = version;
        public void Dispose() { }
    }
}
