using System.Globalization;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexHarness : IDisposable
{
    private readonly StateMachineTestDirectory _testDirectory = new();
    private readonly string _indexPath;
    private MMapDirectory? _writerDirectory;
    private IndexWriter? _writer;
    private bool _disposed;

    public IndexHarness()
    {
        _indexPath = _testDirectory.CreateChildPath("index");
        OpenWriter();
        _writer!.Commit();
    }

    public void Add(ModelDocument document)
    {
        Writer.AddDocument(document.ToLeanDocument());
    }

    public void AddBatch(IReadOnlyList<ModelDocument> documents)
    {
        Writer.AddDocuments(documents.Select(static document => document.ToLeanDocument()).ToArray());
    }

    public void AddAsync(ModelDocument document)
    {
        Writer.AddDocumentAsync(document.ToLeanDocument()).AsTask().GetAwaiter().GetResult();
    }

    public void AddBatchAsync(IReadOnlyList<ModelDocument> documents)
    {
        Writer.AddDocumentsAsync(documents.Select(static document => document.ToLeanDocument()).ToArray())
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public void Delete(string id)
    {
        Writer.DeleteDocuments(new TermQuery("id", id));
    }

    public void Update(ModelDocument replacement)
    {
        Writer.UpdateDocument("id", replacement.Id, replacement.ToLeanDocument());
    }

    public void UpdateByQuery(ModelDocument replacement)
    {
        Writer.UpdateDocuments(
            new TermQuery("id", replacement.Id),
            replacement.ToLeanDocument());
    }

    public void Commit()
    {
        Writer.Commit();
    }

    public void Reopen()
    {
        CloseWriter();
        OpenWriter();
    }

    public void AssertCommitted(IReadOnlyDictionary<string, ModelDocument> expected)
    {
        var actual = ReadDocuments(new MatchAllDocsQuery(), expected.Count);

        Assert.Equal(expected.Count, actual.Count);
        foreach (var expectedDocument in expected.Values)
        {
            Assert.True(actual.TryGetValue(expectedDocument.Id, out var actualDocument),
                $"Committed document '{expectedDocument.Id}' was not found.");
            Assert.Equal(expectedDocument, actualDocument);
        }
    }

    public void AssertSearch(SearchSpec search, IReadOnlyDictionary<string, ModelDocument> committed)
    {
        string[] expectedIds = committed.Values
            .Where(search.Matches)
            .Select(static document => document.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        var actualIds = ReadSearchIds(search.ToQuery(), expectedIds.Length);
        Assert.Equal(expectedIds, actualIds);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseWriter();
        _testDirectory.Dispose();
    }

    private IndexWriter Writer => _writer ?? throw new ObjectDisposedException(nameof(IndexHarness));

    private void OpenWriter()
    {
        _writerDirectory = new MMapDirectory(_indexPath);
        _writer = new IndexWriter(_writerDirectory, CreateWriterConfig());
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
        _writerDirectory?.Dispose();
        _writerDirectory = null;
    }

    private Dictionary<string, ModelDocument> ReadDocuments(Query query, int expectedCount)
    {
        using var directory = new MMapDirectory(_indexPath);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(query, Math.Max(1, expectedCount + 1));
        var documents = new Dictionary<string, ModelDocument>(StringComparer.Ordinal);

        foreach (var scoreDocument in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(scoreDocument.DocId);
            string id = ReadStoredValue(stored, "id");
            string category = ReadStoredValue(stored, "category");
            int price = int.Parse(ReadStoredValue(stored, "price"), CultureInfo.InvariantCulture);
            string body = ReadStoredValue(stored, "body");
            documents.Add(id, new ModelDocument(id, category, price, body));
        }

        return documents;
    }

    private string[] ReadSearchIds(Query query, int expectedCount)
    {
        using var directory = new MMapDirectory(_indexPath);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(query, Math.Max(1, expectedCount + 1));

        Assert.Equal(expectedCount, results.TotalHits);

        return results.ScoreDocs
            .Select(scoreDocument => ReadStoredValue(searcher.GetStoredFields(scoreDocument.DocId), "id"))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ReadStoredValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> stored,
        string field)
    {
        Assert.True(stored.TryGetValue(field, out var values), $"Stored field '{field}' was not found.");
        Assert.NotNull(values);
        Assert.NotEmpty(values!);
        return values![0];
    }

    private static IndexWriterConfig CreateWriterConfig() => new()
    {
        MaxBufferedDocs = 3,
        MergePolicy = NoMergePolicy.Instance
    };
}
