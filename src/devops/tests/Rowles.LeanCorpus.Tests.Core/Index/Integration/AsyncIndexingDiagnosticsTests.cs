using System.Globalization;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Opt-in async indexing counters. The counters are process-wide and other tests may
/// index concurrently while a scope is open, so enabled assertions are lower bounds.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class AsyncIndexingDiagnosticsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public AsyncIndexingDiagnosticsTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument[] MakeDocs(int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture)));
            doc.Add(new TextField("body", "counted async document"));
            return doc;
        }).ToArray();

    [Fact(DisplayName = "Async Indexing Diagnostics: Disabled By Default")]
    public void Diagnostics_AreDisabledWithoutScope()
    {
        Assert.False(AsyncIndexingDiagnostics.IsEnabled);
        Assert.Equal(0, AsyncIndexingDiagnostics.TimestampIfEnabled());
    }

    [Fact(DisplayName = "Async Indexing Diagnostics: Scope Counts Admissions, Snapshots And Commits")]
    public async Task Diagnostics_ScopeCountsAdmissionsSnapshotsAndCommits()
    {
        using var dir = new MMapDirectory(SubDir(nameof(Diagnostics_ScopeCountsAdmissionsSnapshotsAndCommits)));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 10, DurableCommits = false });
        var docs = MakeDocs(25);

        using (AsyncIndexingDiagnostics.BeginMeasurement())
        {
            Assert.True(AsyncIndexingDiagnostics.IsEnabled);
            AsyncIndexingDiagnostics.Reset();

            await writer.AddDocumentAsync(docs[0], TestContext.Current.CancellationToken);
            await writer.AddDocumentsAsync(docs[1..], TestContext.Current.CancellationToken);
            writer.Commit();

            var snapshot = AsyncIndexingDiagnostics.GetSnapshot();
            Assert.True(snapshot.QueueAdmissions >= 2, snapshot.ToString());
            Assert.True(snapshot.AdmittedDocuments >= 25, snapshot.ToString());
            Assert.True(snapshot.MaximumCommandDocuments >= 24, snapshot.ToString());
            Assert.True(snapshot.CompletedCommands >= 2, snapshot.ToString());
            Assert.True(snapshot.FlushSnapshots >= 3, snapshot.ToString());
            Assert.True(snapshot.FlushSnapshotDocuments >= 25, snapshot.ToString());
            Assert.True(snapshot.PublishedSegments >= 3, snapshot.ToString());
            Assert.True(snapshot.Commits >= 1, snapshot.ToString());
        }

        Assert.False(AsyncIndexingDiagnostics.IsEnabled);
    }
}
