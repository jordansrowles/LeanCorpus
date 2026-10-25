using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Fst;
using System.Text;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures building and serialising an FST from sorted UTF-8 terms.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(2)]
[IterationCount(5)]
public class FstBuildBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int TermCount { get; set; }

    private byte[][] _keys = [];
    private int _expectedBlobLength;

    [GlobalSetup]
    public void Setup()
    {
        _keys = new byte[TermCount][];
        for (int i = 0; i < TermCount; i++)
            _keys[i] = Encoding.UTF8.GetBytes($"term{i:D8}");

        for (int i = 1; i < _keys.Length; i++)
        {
            if (_keys[i - 1].AsSpan().SequenceCompareTo(_keys[i]) >= 0)
                throw new InvalidOperationException($"FST fixture keys {i - 1} and {i} are not strictly UTF-8 byte sorted.");
        }

        byte[] blob = BuildFst();
        var reader = FstReader.Open(blob);
        if (reader.Count != TermCount)
            throw new InvalidOperationException($"FST fixture expected {TermCount} keys, got {reader.Count}.");

        for (int i = 0; i < _keys.Length; i++)
        {
            if (!reader.TryGetOutput(_keys[i], out long output) || output != i)
                throw new InvalidOperationException($"FST fixture key {i} expected output {i}, got {output}.");
        }

        _expectedBlobLength = blob.Length;
    }

    [Benchmark(Description = "FST build and serialisation from sorted UTF-8 terms")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Fst_BuildSortedUtf8Terms()
    {
        byte[] blob = BuildFst();
        if (blob.Length != _expectedBlobLength)
            throw new InvalidOperationException($"FST build expected {_expectedBlobLength} bytes, got {blob.Length}.");
        return blob.Length;
    }

    private byte[] BuildFst()
    {
        var builder = new FstBuilder();
        for (int i = 0; i < _keys.Length; i++)
            builder.Add(_keys[i], i);
        return builder.Finish();
    }
}
