using System.Globalization;
using System.Security.Cryptography;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexHarness : IDisposable
{
    private readonly StateMachineTestDirectory _testDirectory = new();
    private readonly string _indexPath;
    private readonly bool _useBbqV3Vectors;
    private MMapDirectory? _writerDirectory;
    private IndexWriter? _writer;
    private bool _disposed;

    public IndexHarness(bool useBbqV3Vectors = false)
    {
        _useBbqV3Vectors = useBbqV3Vectors;
        _indexPath = _testDirectory.CreateChildPath("index");
        OpenWriter();
        _writer!.Commit();
    }

    public void Add(ModelDocument document)
    {
        Writer.AddDocument(document.ToLeanDocument(_useBbqV3Vectors));
    }

    public void AddBatch(IReadOnlyList<ModelDocument> documents)
    {
        Writer.AddDocuments(documents.Select(document => document.ToLeanDocument(_useBbqV3Vectors)).ToArray());
    }

    public void AddAsync(ModelDocument document)
    {
        Writer.AddDocumentAsync(document.ToLeanDocument(_useBbqV3Vectors)).AsTask().GetAwaiter().GetResult();
    }

    public void AddBatchAsync(IReadOnlyList<ModelDocument> documents)
    {
        Writer.AddDocumentsAsync(documents.Select(document => document.ToLeanDocument(_useBbqV3Vectors)).ToArray())
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
        Writer.UpdateDocument("id", replacement.Id, replacement.ToLeanDocument(_useBbqV3Vectors));
    }

    public void UpdateByQuery(ModelDocument replacement)
    {
        Writer.UpdateDocuments(
            new TermQuery("id", replacement.Id),
            replacement.ToLeanDocument(_useBbqV3Vectors));
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
        AssertCommittedDocuments(expected);

        if (_useBbqV3Vectors)
        {
            var expectedHashes = expected.Values
                .Where(static document => document.HasCanonicalVector)
                .ToDictionary(static document => document.Id, static document => document.CanonicalVectorHash(), StringComparer.Ordinal);
            Assert.Equal(expectedHashes, ReadCanonicalVectorHashes());
        }
    }

    public void AssertCommitted(IndexModel model)
    {
        Assert.Equal(_useBbqV3Vectors, model.UseBbqV3Vectors);
        AssertCommittedDocuments(model.Committed);
        if (_useBbqV3Vectors)
            Assert.Equal(model.CanonicalVectorHashes, ReadCanonicalVectorHashes());
    }

    public void AssertCanonicalVectorHashes(IndexModel model)
    {
        Assert.Equal(_useBbqV3Vectors, model.UseBbqV3Vectors);
        if (_useBbqV3Vectors)
            Assert.Equal(model.CanonicalVectorHashes, ReadCanonicalVectorHashes());
    }

    public void ForceMergeAndCommit()
    {
        Writer.Commit();
        Writer.ForceMerge(1);
        Writer.Commit();
    }

    private void AssertCommittedDocuments(IReadOnlyDictionary<string, ModelDocument> expected)
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
        _writer = new IndexWriter(_writerDirectory, CreateWriterConfig(_useBbqV3Vectors));
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

    private Dictionary<string, string> ReadCanonicalVectorHashes()
    {
        using var directory = new MMapDirectory(_indexPath);
        using var searcher = new IndexSearcher(directory);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in searcher.GetSegmentReaders())
        {
            if (!segment.Info.VectorFields.Any(static field => field.FieldName == "embedding"))
                continue;

            using var vectors = QuantisedVectorReader.Open(
                segment.OpenInput(VectorFilePaths.QuantisedVectorFile(string.Empty, "embedding")));
            Assert.Equal(QuantisedVectorStorageKind.BbqOsqCanonicalInt8, vectors.StorageKind);
            byte[] record = new byte[CanonicalInt8VectorMetadata.Size + vectors.Dimension];
            for (int docId = 0; docId < segment.MaxDoc; docId++)
            {
                if (!segment.IsLive(docId) || !vectors.HasVector(docId))
                    continue;

                string id = Assert.Single(segment.GetStoredFields(docId)["id"]);
                vectors.CopyCanonicalRecordTo(docId, record);
                hashes.Add(id, Convert.ToHexString(SHA256.HashData(record)));
            }
        }
        return hashes;
    }

    private static IndexWriterConfig CreateWriterConfig(bool useBbqV3Vectors) => new()
    {
        MaxBufferedDocs = 3,
        MergePolicy = NoMergePolicy.Instance,
        VectorQuantisation = useBbqV3Vectors ? VectorQuantisation.BBQ : VectorQuantisation.None,
        NormaliseVectors = useBbqV3Vectors,
        BuildHnswOnFlush = useBbqV3Vectors,
        HnswSeed = 42,
    };
}
