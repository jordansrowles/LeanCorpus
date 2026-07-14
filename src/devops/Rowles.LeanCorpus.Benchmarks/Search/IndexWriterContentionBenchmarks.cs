using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="IndexWriter"/> throughput under increasing concurrency
/// to identify where <c>_writeLock</c> contention overtakes I/O.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class IndexWriterContentionBenchmarks
{
    [Params(1, 2, 4, 8)]
    public int WriterCount { get; set; }

    private const int TotalDocs = 20_000;
    private string _indexPath = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-contention-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_indexPath);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkHelpers.DeleteDirectory(_indexPath);
    }

    [Benchmark(Description = "Concurrent indexing")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Index()
    {
        using var directory = new MMapDirectory(_indexPath);
        var config = new IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 128 };
        using var writer = new IndexWriter(directory, config);

        int docsPerWriter = TotalDocs / WriterCount;
        var tasks = new System.Threading.Tasks.Task[WriterCount];
        for (int w = 0; w < WriterCount; w++)
        {
            int writerId = w;
            tasks[w] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < docsPerWriter; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new StringField("id", $"{writerId}-{i}"));
                    doc.Add(new TextField("body", $"document number {writerId}-{i} with some content"));
                    doc.Add(new NumericField("price", (writerId * 100.0) + i));
                    writer.AddDocument(doc);
                }
            });
        }

        System.Threading.Tasks.Task.WaitAll(tasks);
        writer.Commit();
        return TotalDocs;
    }
}
