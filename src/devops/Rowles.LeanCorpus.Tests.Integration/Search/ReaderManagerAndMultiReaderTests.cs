using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

[Trait("Category", "Search")]
[Trait("Category", "ReaderLifecycle")]
public sealed class ReaderManagerAndMultiReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lc-composition-{Guid.NewGuid():N}");

    public ReaderManagerAndMultiReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public void ReaderManagerRetiresReadersAfterTheLastLease()
    {
        int generation = 0;
        using var manager = new ReaderManager<TestReader>(
            () => new TestReader(0),
            current => Volatile.Read(ref generation) > current.Generation
                ? new TestReader(Volatile.Read(ref generation))
                : null,
            TimeSpan.FromHours(1));

        using var first = manager.AcquireLease();
        var firstReader = first.Reader;
        generation = 1;
        Assert.True(manager.MaybeRefresh());
        Assert.False(firstReader.Disposed);
        Assert.Equal(1, manager.GetDiagnostics().ActiveReaders);
        first.Dispose();
        Assert.True(firstReader.Disposed);
        Assert.Equal(1, manager.GetDiagnostics().Refreshes);
    }

    [Fact]
    public void ReaderManagerRecordsRefreshFailuresAndKeepsCurrentReader()
    {
        var failure = new InvalidDataException("broken reader");
        using var manager = new ReaderManager<TestReader>(
            () => new TestReader(0),
            _ => throw failure,
            TimeSpan.FromHours(1));
        bool eventRaised = false;
        manager.RefreshFailed += (_, args) =>
        {
            eventRaised = true;
            Assert.Same(failure, args.Exception);
        };

        Assert.False(manager.MaybeRefresh());
        Assert.Same(failure, manager.LastRefreshError);
        Assert.True(eventRaised);
        using var lease = manager.AcquireLease();
        Assert.Equal(0, lease.Reader.Generation);
    }

    [Fact]
    public void ReaderManagerCanLeaseARetiredReaderWhileItIsStillRetained()
    {
        int generation = 0;
        using var manager = new ReaderManager<TestReader>(
            () => new TestReader(0),
            current => Volatile.Read(ref generation) > current.Generation
                ? new TestReader(Volatile.Read(ref generation))
                : null,
            TimeSpan.FromHours(1));

        using var original = manager.AcquireLease();
        generation = 1;
        Assert.True(manager.MaybeRefresh());

        Assert.True(manager.TryAcquire(
            reader => reader.Generation == 0,
            out var retained));
        using (retained)
            Assert.Same(original.Reader, retained.Reader);

        original.Dispose();
        Assert.False(manager.TryAcquire(reader => reader.Generation == 0, out _));
    }

    [Fact]
    public void MultiReaderUsesStableGlobalIdsAndUnionResults()
    {
        var firstPath = CreateIndex("first", ["common first"]);
        var secondPath = CreateIndex("second", ["common second", "other second"]);
        using var firstDirectory = new MMapDirectory(firstPath);
        using var secondDirectory = new MMapDirectory(secondPath);
        using var reader = new MultiReader([firstDirectory, secondDirectory]);

        var results = reader.Search(new TermQuery("body", "common"), 10, SortField.DocId);

        Assert.Equal(2, results.TotalHits);
        Assert.Equal([0, 1], results.ScoreDocs.Select(static hit => hit.DocId).ToArray());
        Assert.Equal(2, reader.CommitGenerations.Count);
    }

    [Fact]
    public void MultiReaderKeepsAnOldCompositionStableAcrossLaterCommits()
    {
        var firstPath = CreateIndex("stable", ["before"]);
        var secondPath = CreateIndex("stable-second", ["before second"]);
        using var firstDirectory = new MMapDirectory(firstPath);
        using var secondDirectory = new MMapDirectory(secondPath);
        using var oldReader = new MultiReader([firstDirectory, secondDirectory]);

        using (var writer = new IndexWriter(firstDirectory, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDocument("after"));
            writer.Commit();
        }

        Assert.Equal(0, oldReader.Search(new TermQuery("body", "after"), 10).TotalHits);
        using var newReader = new MultiReader([firstDirectory, secondDirectory]);
        Assert.Equal(1, newReader.Search(new TermQuery("body", "after"), 10).TotalHits);
    }

    [Fact]
    public void MultiReaderSupportsFieldSortAndContinuation()
    {
        var firstPath = CreateIndex("sort-first", ["common"], [30]);
        var secondPath = CreateIndex("sort-second", ["common", "common"], [10, 20]);
        using var firstDirectory = new MMapDirectory(firstPath);
        using var secondDirectory = new MMapDirectory(secondPath);
        using var reader = new MultiReader([firstDirectory, secondDirectory]);
        var sort = SortField.Numeric("number");

        var first = reader.Search(new TermQuery("body", "common"), 2, sort);
        var second = reader.SearchAfter(first.ScoreDocs[^1], new TermQuery("body", "common"), 2, sort);

        Assert.Equal([1, 2], first.ScoreDocs.Select(static hit => hit.DocId).ToArray());
        Assert.Equal([0], second.ScoreDocs.Select(static hit => hit.DocId).ToArray());
    }

    [Fact]
    public void MultiReaderSupportsDocumentIdContinuationAcrossComponents()
    {
        var firstPath = CreateIndex("docid-first", ["common"]);
        var secondPath = CreateIndex("docid-second", ["common", "common"]);
        using var firstDirectory = new MMapDirectory(firstPath);
        using var secondDirectory = new MMapDirectory(secondPath);
        using var reader = new MultiReader([firstDirectory, secondDirectory]);

        var first = reader.Search(new TermQuery("body", "common"), 2, SortField.DocId);
        var second = reader.SearchAfter(first.ScoreDocs[^1], new TermQuery("body", "common"), 2, SortField.DocId);

        Assert.Equal([0, 1], first.ScoreDocs.Select(static hit => hit.DocId).ToArray());
        Assert.Equal([2], second.ScoreDocs.Select(static hit => hit.DocId).ToArray());
    }

    [Fact]
    public void OrdinalMapUsesStableTermOrderAcrossComponentReaders()
    {
        var firstPath = CreateIndex("ordinal-first", ["common"], tags: ["alpha"]);
        var secondPath = CreateIndex("ordinal-second", ["common"], tags: ["beta"]);
        using var firstDirectory = new MMapDirectory(firstPath);
        using var secondDirectory = new MMapDirectory(secondPath);
        using var reader = new MultiReader([firstDirectory, secondDirectory]);

        var map = reader.GetOrdinalMap("tag");

        Assert.Equal(["alpha", "beta"], map.Terms);
        Assert.Equal(0, map.GetGlobalOrdinal(0, 0));
        Assert.Equal(1, map.GetGlobalOrdinal(1, 0));
        Assert.True(map.TryGetGlobalOrdinal(1, "beta", out int betaOrdinal));
        Assert.Equal(1, betaOrdinal);
    }

    private string CreateIndex(string name, string[] bodies, double[]? numbers = null, string[]? tags = null)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        for (int i = 0; i < bodies.Length; i++)
        {
            var document = CreateDocument(bodies[i]);
            if (numbers is not null)
                document.Add(new NumericField("number", numbers[i], stored: true));
            if (tags is not null)
                document.Add(new StringField("tag", tags[i]));
            writer.AddDocument(document);
        }
        writer.Commit();
        return path;
    }

    private static LeanDocument CreateDocument(string body)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        return document;
    }

    private sealed class TestReader(int generation) : IDisposable
    {
        public int Generation { get; } = generation;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
