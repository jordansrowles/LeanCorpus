using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures concurrent reads from one warm stored-fields block.</summary>
[MemoryDiagnoser]
public class StoredFieldsReadBenchmarks
{
    private const int DocumentCount = 8_192;
    private const int ReadsPerBatch = 4_096;
    private const int BlockSize = DocumentCount;
    private readonly string _body = new('x', 96);
    private string _indexPath = string.Empty;
    private StoredFieldsReader? _reader;

    [Params(1, 2, 4, 8)]
    public int WorkerCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"stored-fields-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);

        var documents = new Dictionary<string, List<string>>[DocumentCount];
        for (int docId = 0; docId < documents.Length; docId++)
        {
            documents[docId] = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["id"] = [docId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
                ["body"] = [_body]
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
        _reader.ReadDocumentValues(0); // Warm the single shared immutable block cache.
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
    public long ConcurrentSameSegmentReads()
    {
        int readsPerWorker = ReadsPerBatch / WorkerCount;
        long checksum = 0;
        Parallel.For(0, WorkerCount, worker =>
        {
            long workerChecksum = 0;
            int firstDoc = worker * readsPerWorker;
            for (int i = 0; i < readsPerWorker; i++)
            {
                int docId = (firstDoc + i) % DocumentCount;
                var fields = _reader!.ReadDocumentValues(docId);
                workerChecksum += fields["body"][0].StringValue!.Length;
            }

            Interlocked.Add(ref checksum, workerChecksum);
        });

        return checksum;
    }
}
