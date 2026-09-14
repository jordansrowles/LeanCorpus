using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures detached flushes with low, medium, and high unique-term cardinality.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(2)]
[IterationCount(5)]
public class FlushTermCardinalityBenchmarks
{
    [Params(10, 1_000, 10_000)]
    public int UniqueTermCount { get; set; }

    [Params(1, 2, 4)]
    public int MaxConcurrentFlushes { get; set; }

    private string[] _documents = [];
    private readonly List<string> _createdPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        const int documentCount = 1_000;
        int termsPerDocument = (UniqueTermCount + documentCount - 1) / documentCount;
        _documents = new string[documentCount];
        for (int document = 0; document < documentCount; document++)
        {
            var terms = new string[termsPerDocument];
            for (int term = 0; term < terms.Length; term++)
                terms[term] = $"term_{(document * termsPerDocument + term) % UniqueTermCount}";
            _documents[document] = string.Join(' ', terms);
        }
    }

    [IterationCleanup]
    public void Cleanup()
    {
        foreach (string path in _createdPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _createdPaths.Clear();
    }

    [Benchmark]
    public int FlushUniqueTerms()
    {
        string path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-flush-terms-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(path);
        _createdPaths.Add(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = _documents.Length,
            MaxConcurrentFlushes = MaxConcurrentFlushes,
            IndexingConcurrency = 4,
            RamBufferSizeMB = 256
        });
        var documents = new LeanDocument[_documents.Length];
        for (int i = 0; i < _documents.Length; i++)
        {
            var document = new LeanDocument();
            document.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            document.Add(new LeanTextField("body", _documents[i]));
            documents[i] = document;
        }
        writer.AddDocumentsConcurrent(documents);
        writer.Commit();
        return _documents.Length;
    }
}
