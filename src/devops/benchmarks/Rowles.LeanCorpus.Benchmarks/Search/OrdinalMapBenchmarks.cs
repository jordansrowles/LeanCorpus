using BenchmarkDotNet.Attributes;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures global ordinal construction and lookup across overlapping local dictionaries.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class OrdinalMapBenchmarks
{
    [Params(2, 8, 32)]
    public int SourceCount { get; set; }

    [Params(100, 1_000)]
    public int TermsPerSource { get; set; }

    [Params(0, 50, 90)]
    public int OverlapPercent { get; set; }

    private IReadOnlyList<IReadOnlyList<string>> _sources = [];
    private OrdinalMap? _map;
    private string[] _probes = [];

    [GlobalSetup]
    public void Setup()
    {
        int shared = TermsPerSource * OverlapPercent / 100;
        var sources = new IReadOnlyList<string>[SourceCount];
        for (int source = 0; source < SourceCount; source++)
        {
            var terms = new string[TermsPerSource];
            for (int ordinal = 0; ordinal < TermsPerSource; ordinal++)
            {
                int identity = ordinal < shared
                    ? ordinal
                    : shared + source * (TermsPerSource - shared) + ordinal - shared;
                terms[ordinal] = $"term-{identity:D8}";
            }
            Array.Sort(terms, StringComparer.Ordinal);
            sources[source] = terms;
        }

        _sources = sources;
        _map = OrdinalMap.Build(_sources);
        _probes = _sources.Select(static source => source[source.Count / 2]).ToArray();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int BuildMap() => OrdinalMap.Build(_sources).ValueCount;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LocalOrdinalLookups()
    {
        int checksum = 0;
        for (int source = 0; source < SourceCount; source++)
            checksum += _map!.GetGlobalOrdinal(source, TermsPerSource / 2);
        return checksum;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int TermLookups()
    {
        int checksum = 0;
        for (int source = 0; source < SourceCount; source++)
            if (_map!.TryGetGlobalOrdinal(source, _probes[source], out int ordinal))
                checksum += ordinal;
        return checksum;
    }
}
