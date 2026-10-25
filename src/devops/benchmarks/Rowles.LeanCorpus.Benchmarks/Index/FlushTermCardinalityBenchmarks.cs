using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStoredField = Rowles.LeanCorpus.Document.Fields.StoredField;
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
    private string[] _expectedIds = [];
    private readonly List<string> _createdPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        const int documentCount = 1_000;
        _expectedIds = Enumerable.Range(0, documentCount)
            .Select(static id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
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
        {
            try
            {
                VerifyCommittedIndex(path);
            }
            finally
            {
                BenchmarkHelpers.DeleteDirectory(path);
            }
        }
        _createdPaths.Clear();
    }

    private void VerifyCommittedIndex(string path)
    {
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        var result = searcher.Search(new MatchAllDocsQuery(), _documents.Length);
        if (result.TotalHits != _documents.Length || result.ScoreDocs.Length != _documents.Length)
        {
            throw new InvalidOperationException(
                $"Flush cardinality preflight expected {_documents.Length} committed documents, got " +
                $"{result.TotalHits} total hits and {result.ScoreDocs.Length} returned.");
        }

        string[] actualIds = result.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        if (!actualIds.SequenceEqual(_expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("Flush cardinality preflight did not preserve the exact committed ID set.");
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
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
            document.Add(new LeanStoredField("id", i));
            document.Add(new LeanTextField("body", _documents[i]));
            documents[i] = document;
        }
        writer.AddDocumentsConcurrent(documents);
        writer.Commit();
        return _documents.Length;
    }
}
