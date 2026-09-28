using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures concurrent random reads from the same and different stored-fields blocks.</summary>
[MemoryDiagnoser]
public class StoredFieldsReadBenchmarks
{
    private const int ReadsPerBatch = 4_096;
    private const int BlockSize = 64;
    private const int FieldValueLength = 96;

    public static IEnumerable<int> DocumentCounts => BenchmarkData.GetDocCounts(16_384, 131_072);

    [ParamsSource(nameof(DocumentCounts))]
    public int DocumentCount { get; set; }

    [Params(1, 2, 4, 8, 16)]
    public int WorkerCount { get; set; }

    private string _indexPath = string.Empty;
    private StoredFieldsReader? _reader;
    private int[][] _sameBlockReadOrder = [];
    private int[][] _differentBlockReadOrder = [];

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"stored-fields-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);

        var documents = new Dictionary<string, List<string>>[DocumentCount];
        string body = new('x', FieldValueLength);
        for (int docId = 0; docId < documents.Length; docId++)
        {
            documents[docId] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["id"] = [docId.ToString(CultureInfo.InvariantCulture)],
                ["body"] = [body]
            };
        }

        string fdtPath = Path.Combine(_indexPath, "segment.fdt");
        string fdxPath = Path.Combine(_indexPath, "segment.fdx");
        StoredFieldsWriter.Write(
            fdtPath,
            fdxPath,
            documents,
            blockSize: BlockSize,
            compression: FieldCompressionPolicy.Deflate);
        _reader = StoredFieldsReader.Open(fdtPath, fdxPath);
        _reader.ReadDocumentValues(0); // Warm the shared immutable cache for the same-block workload.

        var random = BenchmarkDeterministicRandom.Create("benchmark/stored-fields/random-read-order");
        _sameBlockReadOrder = new int[WorkerCount][];
        _differentBlockReadOrder = new int[WorkerCount][];
        int readsPerWorker = ReadsPerBatch / WorkerCount;
        int blockCount = (DocumentCount + BlockSize - 1) / BlockSize;
        for (int worker = 0; worker < WorkerCount; worker++)
        {
            _sameBlockReadOrder[worker] = new int[readsPerWorker];
            _differentBlockReadOrder[worker] = new int[readsPerWorker];
            for (int i = 0; i < readsPerWorker; i++)
            {
                _sameBlockReadOrder[worker][i] = random.NextInt32(BlockSize);

                int block = (worker + (i % Math.Max(1, blockCount / WorkerCount)) * WorkerCount) % blockCount;
                int blockStart = block * BlockSize;
                int blockLength = Math.Min(BlockSize, DocumentCount - blockStart);
                _differentBlockReadOrder[worker][i] = blockStart + random.NextInt32(blockLength);
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _reader?.Dispose();
        if (Directory.Exists(_indexPath))
            Directory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(OperationsPerInvoke = ReadsPerBatch)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long ConcurrentSameBlockRandomReads()
        => ReadConcurrently(_sameBlockReadOrder);

    [Benchmark(OperationsPerInvoke = ReadsPerBatch)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public long ConcurrentDifferentBlockRandomReads()
        => ReadConcurrently(_differentBlockReadOrder);

    private long ReadConcurrently(int[][] readOrder)
    {
        long checksum = 0;
        Parallel.For(0, WorkerCount, worker =>
        {
            long workerChecksum = 0;
            foreach (int docId in readOrder[worker])
            {
                var fields = _reader!.ReadDocumentValues(docId);
                workerChecksum += fields["body"][0].StringValue!.Length;
            }

            Interlocked.Add(ref checksum, workerChecksum);
        });

        return checksum;
    }
}
