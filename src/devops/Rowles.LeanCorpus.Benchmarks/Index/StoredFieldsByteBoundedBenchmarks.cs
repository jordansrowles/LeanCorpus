using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures read allocation when small documents share a count-bounded block with large neighbours.</summary>
[MemoryDiagnoser]
public class StoredFieldsByteBoundedBenchmarks
{
    private const int SmallDocumentCount = 16;
    private const int BlockSize = 2;
    private const int LargePayloadBytes = StoredFieldsBlockPolicy.TargetRawBytes;
    private string _indexPath = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"stored-fields-byte-bounded-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);

        var documents = new Dictionary<string, List<string>>[SmallDocumentCount * 2];
        for (int smallDoc = 0; smallDoc < SmallDocumentCount; smallDoc++)
        {
            documents[smallDoc * 2] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["id"] = [$"small-{smallDoc}"]
            };
            documents[smallDoc * 2 + 1] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["body"] = [new string('x', LargePayloadBytes)]
            };
        }

        StoredFieldsWriter.Write(
            Path.Combine(_indexPath, "segment.fdt"),
            Path.Combine(_indexPath, "segment.fdx"),
            documents,
            blockSize: BlockSize,
            compression: FieldCompressionPolicy.Deflate);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_indexPath))
            Directory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(OperationsPerInvoke = SmallDocumentCount)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long ReadSmallDocumentsBesideLargeNeighbours()
    {
        using var reader = StoredFieldsReader.Open(
            Path.Combine(_indexPath, "segment.fdt"),
            Path.Combine(_indexPath, "segment.fdx"));

        long checksum = 0;
        for (int smallDoc = 0; smallDoc < SmallDocumentCount; smallDoc++)
        {
            var fields = reader.ReadDocumentValues(smallDoc * 2);
            checksum += fields["id"][0].StringValue!.Length;
        }

        return checksum;
    }
}
