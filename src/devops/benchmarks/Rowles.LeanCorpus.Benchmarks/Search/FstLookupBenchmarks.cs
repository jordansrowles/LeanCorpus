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
[WarmupCount(2)]
[IterationCount(5)]
public class FstLookupBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int TermCount { get; set; }

    private byte[] _fstBlob = [];
    private FstReader _reader = null!;
    private byte[][] _keys = [];
    private byte[] _prefix = Encoding.UTF8.GetBytes("term");
    private int _expectedHits;
    private int _expectedPrefixMatches;

    [GlobalSetup]
    public void Setup()
    {
        // Generate synthetic terms: "term000000", "term000001", ...
        var terms = new string[TermCount];
        for (int i = 0; i < TermCount; i++)
            terms[i] = $"term{i:D8}";

        Array.Sort(terms, StringComparer.Ordinal);
        _expectedHits = TermCount / 2;
        _expectedPrefixMatches = terms.Count(static term => term.StartsWith("term", StringComparison.Ordinal));

        var builder = new FstBuilder();
        for (int i = 0; i < terms.Length; i++)
        {
            var keyBytes = Encoding.UTF8.GetBytes(terms[i]);
            builder.Add(keyBytes, i);
        }
        _fstBlob = builder.Finish();
        _reader = FstReader.Open(_fstBlob);

        if (_reader.Count != TermCount)
            throw new InvalidOperationException($"FST fixture expected {TermCount} keys, got {_reader.Count}.");

        VerifyKeySequence(_reader.EnumerateWithPrefix(_prefix), terms);
        VerifyKeySequence(
            _reader.IntersectAutomaton(new PrefixAutomaton(_prefix))
                .Select(static match => (match.Key, match.Output)),
            terms);

        int prefixMatches = _reader.EnumerateWithPrefix(_prefix).Count();
        if (prefixMatches != _expectedPrefixMatches)
            throw new InvalidOperationException($"FST prefix fixture expected {_expectedPrefixMatches} matches, got {prefixMatches}.");

        // Pick representative keys for lookup (50% hit rate).
        var rnd = BenchmarkDeterministicRandom.Create("benchmark/fst/miss-keys");
        _keys = new byte[TermCount][];
        for (int i = 0; i < TermCount; i++)
        {
            if (i < TermCount / 2)
                _keys[i] = Encoding.UTF8.GetBytes(terms[i]); // hit
            else
                _keys[i] = Encoding.UTF8.GetBytes($"miss{rnd.NextInt32(100000):D8}"); // miss
        }
    }

    private static void VerifyKeySequence(IEnumerable<(byte[] Key, long Output)> actual, IReadOnlyList<string> expected)
    {
        int index = 0;
        foreach (var (key, output) in actual)
        {
            if (index >= expected.Count
                || !key.AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(expected[index]))
                || output != index)
            {
                throw new InvalidOperationException($"FST fixture entry {index} did not match its expected UTF-8 key and ordinal output.");
            }

            index++;
        }

        if (index != expected.Count)
            throw new InvalidOperationException($"FST fixture expected {expected.Count} enumerated entries, got {index}.");
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
        if (hits != _expectedHits)
            throw new InvalidOperationException($"FST lookup fixture expected {_expectedHits} hits, got {hits}.");
        return hits;
    }

    [Benchmark(Description = "FST EnumeratePrefix")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Fst_EnumeratePrefix()
    {
        int count = 0;
        foreach (var _ in _reader.EnumerateWithPrefix(_prefix))
            count++;
        if (count != _expectedPrefixMatches)
            throw new InvalidOperationException($"FST prefix benchmark expected {_expectedPrefixMatches} matches, got {count}.");
        return count;
    }

    [Benchmark(Description = "FST IntersectAutomaton (wildcard)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Fst_IntersectAutomaton()
    {
        // Prefix: "term" matches every key in this deterministic fixture.
        var automaton = new PrefixAutomaton(Encoding.UTF8.GetBytes("term"));
        int count = 0;
        foreach (var _ in _reader.IntersectAutomaton(automaton))
            count++;
        if (count != _expectedPrefixMatches)
            throw new InvalidOperationException($"FST intersection fixture expected {_expectedPrefixMatches} matches, got {count}.");
        return count;
    }
}
