using System.Buffers;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.DocValues;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures binary DocValues block encoding and cumulative payload-offset accounting.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(2)]
[IterationCount(5)]
public class BinaryDocValuesWriteBenchmarks
{
    [Params(65_536)]
    public int ValueCount { get; set; }

    private readonly DiscardingBufferWriter _output = new();
    private IReadOnlyList<byte[]>?[] _documents = [];

    [GlobalSetup]
    public void Setup()
    {
        var value = new byte[64];
        var values = new byte[ValueCount][];
        Array.Fill(values, value);
        _documents = [values];
    }

    [Benchmark(Description = "Binary DocValues block write and offset accounting")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WriteBinaryDocValuesBlock()
    {
        _output.Reset();
        BinaryDocValuesWriter.WriteFieldBlock(_output, "payload", _documents, docCount: 1);
        return _output.BytesWritten;
    }

    private sealed class DiscardingBufferWriter : IBufferWriter<byte>
    {
        private readonly byte[] _buffer = new byte[4096];

        public int BytesWritten { get; private set; }

        public void Reset() => BytesWritten = 0;

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            BytesWritten = checked(BytesWritten + count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            GetSpan(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (sizeHint > _buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));
            return _buffer;
        }
    }
}
