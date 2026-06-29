using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Fst;
using System.Text;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures FST term-dictionary lookup throughput via
/// <see cref="FstBuilder"/> and <see cref="FstReader"/>.
/// Builds an FST from synthetic terms, then benchmarks lookup,
/// prefix enumeration, and automaton intersection.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class FstLookupBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int TermCount { get; set; }

    private byte[] _fstBlob = [];
    private FstReader _reader = null!;
    private byte[][] _keys = [];
    private byte[] _prefix = Encoding.UTF8.GetBytes("govern");

    [GlobalSetup]
    public void Setup()
    {
        // Generate synthetic terms: "term000000", "term000001", ...
        var terms = new string[TermCount];
        for (int i = 0; i < TermCount; i++)
            terms[i] = $"term{i:D8}";

        Array.Sort(terms, StringComparer.Ordinal);

        var builder = new FstBuilder();
        for (int i = 0; i < terms.Length; i++)
        {
            var keyBytes = Encoding.UTF8.GetBytes(terms[i]);
            builder.Add(keyBytes, i);
        }
        _fstBlob = builder.Finish();
        _reader = FstReader.Open(_fstBlob);

        // Pick representative keys for lookup (50% hit rate).
        var rnd = new Random(7);
        _keys = new byte[TermCount][];
        for (int i = 0; i < TermCount; i++)
        {
            if (i < TermCount / 2)
                _keys[i] = Encoding.UTF8.GetBytes(terms[i]); // hit
            else
                _keys[i] = Encoding.UTF8.GetBytes($"miss{rnd.Next(100000):D8}"); // miss
        }
    }

    [Benchmark(Baseline = true, Description = "FST TryGetOutput")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Fst_Lookup()
    {
        int hits = 0;
        for (int i = 0; i < _keys.Length; i++)
        {
            if (_reader.TryGetOutput(_keys[i], out _))
                hits++;
        }
        return hits;
    }

    [Benchmark(Description = "FST EnumeratePrefix")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Fst_EnumeratePrefix()
    {
        int count = 0;
        foreach (var _ in _reader.EnumerateWithPrefix(_prefix))
            count++;
        return count;
    }

    [Benchmark(Description = "FST IntersectAutomaton (wildcard)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Fst_IntersectAutomaton()
    {
        // Wildcard: "term000*" — matches all keys.
        var automaton = new PrefixAutomaton(Encoding.UTF8.GetBytes("term000"));
        int count = 0;
        foreach (var _ in _reader.IntersectAutomaton(automaton))
            count++;
        return count;
    }
}