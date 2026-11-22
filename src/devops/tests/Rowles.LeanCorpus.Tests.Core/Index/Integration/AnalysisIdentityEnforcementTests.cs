using System.Text.Json;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Serialisation;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core;

/// <summary>
/// ADR051: an index records the analysis identity of every analysed field and refuses to write a
/// field under a different one, including content written before identities were recorded.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class AnalysisIdentityEnforcementTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly string German = AnalyserFactory.GetDefaultAnalysisId("de");
    private static readonly string French = AnalyserFactory.GetDefaultAnalysisId("fr");
    private static readonly string Standard = AnalysisIdentity.Of(new StandardAnalyser())!;

    private readonly TestDirectoryFixture _fixture;

    public AnalysisIdentityEnforcementTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Analysis identity: each analysed field records its analyser's identity")]
    public void Commit_RecordsEachAnalysedFieldsIdentity()
    {
        using var directory = Directory("records");
        Write(directory, GermanBody(), Doc("1", body: "Tomaten und Zwiebeln", title: "Suppe"));

        var identities = IndexAnalysisIdentities.Read(directory);
        Assert.NotNull(identities);
        Assert.Equal(German, identities["body"]);
        Assert.Equal(Standard, identities["title"]);
        Assert.False(identities.ContainsKey("id"), "A string field is not analysed and has no identity.");
    }

    [Fact(DisplayName = "Analysis identity: reopening with the same analysers keeps writing")]
    public void Reopen_WithTheSameAnalysers_Succeeds()
    {
        using var directory = Directory("same");
        Write(directory, GermanBody(), Doc("1", body: "Tomaten"));
        Write(directory, GermanBody(), Doc("2", body: "Tomate"));

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(2, searcher.Search(new TermQuery("body", "tomat"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Analysis identity: a changed analyser fails when the writer opens")]
    public void Reopen_WithADifferentAnalyser_FailsOnOpen()
    {
        using var directory = Directory("changed");
        Write(directory, GermanBody(), Doc("1", body: "Tomaten"));

        var exception = Assert.Throws<AnalysisIdentityMismatchException>(
            () => new IndexWriter(directory, Config(("body", AnalyserFactory.Create("fr")))));
        Assert.Equal("body", exception.FieldName);
        Assert.Equal(German, exception.RecordedIdentity);
        Assert.Equal(French, exception.CurrentIdentity);

        // The failed open left the index unchanged and writable with the recorded analyser.
        Write(directory, GermanBody(), Doc("2", body: "Tomate"));
        Assert.Equal(German, IndexAnalysisIdentities.Read(directory)!["body"]);
    }

    [Fact(DisplayName = "Analysis identity: a changed default analyser fails for every recorded field")]
    public void Reopen_WithADifferentDefaultAnalyser_Fails()
    {
        using var directory = Directory("default-changed");
        Write(directory, new IndexWriterConfig(), Doc("1", title: "Soup"));

        var exception = Assert.Throws<AnalysisIdentityMismatchException>(
            () => new IndexWriter(directory, new IndexWriterConfig { DefaultAnalyser = new StemmedAnalyser() }));
        Assert.Equal("title", exception.FieldName);
        Assert.Equal(Standard, exception.RecordedIdentity);
    }

    [Fact(DisplayName = "Analysis identity: accepting a change records the new identity")]
    public void AcceptAnalysisIdentityChanges_RecordsTheNewIdentity()
    {
        using var directory = Directory("accept");
        Write(directory, GermanBody(), Doc("1", body: "Tomaten"));

        var accepting = Config(("body", AnalyserFactory.Create("fr")));
        accepting.AcceptAnalysisIdentityChanges = true;
        Write(directory, accepting, Doc("2", body: "chats"));

        Assert.Equal(French, IndexAnalysisIdentities.Read(directory)!["body"]);
        // From now on the French analyser is the recorded one.
        Write(directory, Config(("body", AnalyserFactory.Create("fr"))), Doc("3", body: "chiens"));
        Assert.Throws<AnalysisIdentityMismatchException>(() => new IndexWriter(directory, GermanBody()));
    }

    [Fact(DisplayName = "Analysis identity: unidentified analysers are recorded and their replacement is detected")]
    public void UnidentifiedAnalyser_IsRecorded_AndAnIdentifiedReplacementIsDetected()
    {
        using var directory = Directory("unidentified");
        Write(directory, Config(("body", AnalyserFactory.Create("de", stemmer: null))), Doc("1", body: "Tomaten"));
        Assert.Equal(AnalysisIdentity.Unidentified, IndexAnalysisIdentities.Read(directory)!["body"]);

        var exception = Assert.Throws<AnalysisIdentityMismatchException>(() => new IndexWriter(directory, GermanBody()));
        Assert.Equal(AnalysisIdentity.Unidentified, exception.RecordedIdentity);
        Assert.Equal(German, exception.CurrentIdentity);
    }

    [Fact(DisplayName = "Analysis identity: an assigned identity is recorded and enforced")]
    public void AssignedIdentity_IsRecordedAndEnforced()
    {
        using var directory = Directory("assigned");
        var v1 = Config(("body", AnalysisIdentity.Assign(AnalyserFactory.Create("de", stemmer: null), "test/de-exact/1")));
        Write(directory, v1, Doc("1", body: "Tomaten"));
        Assert.Equal("test/de-exact/1", IndexAnalysisIdentities.Read(directory)!["body"]);

        var v2 = Config(("body", AnalysisIdentity.Assign(AnalyserFactory.Create("de", stemmer: null), "test/de-exact/2")));
        Assert.Throws<AnalysisIdentityMismatchException>(() => new IndexWriter(directory, v2));
    }

    [Fact(DisplayName = "Analysis identity: writer character filters make fields unidentified")]
    public void CharFilters_MakeFieldsUnidentified()
    {
        using var directory = Directory("char-filters");
        var config = GermanBody();
        config.CharFilters = [new HtmlStripCharFilter()];
        Write(directory, config, Doc("1", body: "<b>Tomaten</b>"));

        Assert.Equal(AnalysisIdentity.Unidentified, IndexAnalysisIdentities.Read(directory)!["body"]);
        Assert.Throws<AnalysisIdentityMismatchException>(() => new IndexWriter(directory, GermanBody()));
    }

    [Fact(DisplayName = "Analysis identity: content written before identities were recorded is refused")]
    public void LegacyContent_IsRefusedUntilAccepted()
    {
        using var directory = Directory("legacy");
        Write(directory, GermanBody(), Doc("1", body: "Tomaten"));
        RemoveRecordedIdentities(directory);
        Assert.Null(IndexAnalysisIdentities.Read(directory));

        // Opening succeeds: nothing is known to check until a field is written.
        using (var writer = new IndexWriter(directory, GermanBody()))
        {
            var exception = Assert.Throws<AnalysisIdentityMismatchException>(() => writer.AddDocument(Doc("2", body: "Tomate")));
            Assert.Equal("body", exception.FieldName);
            Assert.Null(exception.RecordedIdentity);
            Assert.Equal(German, exception.CurrentIdentity);
            Assert.Contains("before analysis identities were recorded", exception.Message, StringComparison.Ordinal);

            // A field without analysis is unaffected.
            var keywordOnly = new LeanDocument();
            keywordOnly.Add(new StringField("id", "3", stored: true));
            writer.AddDocument(keywordOnly);
            writer.Commit();
        }

        var accepting = GermanBody();
        accepting.AcceptAnalysisIdentityChanges = true;
        Write(directory, accepting, Doc("4", body: "Tomate"));
        Assert.Equal(German, IndexAnalysisIdentities.Read(directory)!["body"]);
        Write(directory, GermanBody(), Doc("5", body: "Zwiebel"));
    }

    [Fact(DisplayName = "Analysis identity: a pre-identity commit with no segments constrains nothing")]
    public void LegacyCommitWithoutSegments_IsNotRestricted()
    {
        using var directory = Directory("legacy-empty");
        using (var writer = new IndexWriter(directory, GermanBody()))
            writer.Commit();
        RemoveRecordedIdentities(directory);

        Write(directory, GermanBody(), Doc("1", body: "Tomaten"));
        Assert.Equal(German, IndexAnalysisIdentities.Read(directory)!["body"]);
    }

    [Fact(DisplayName = "Analysis identity: a query analyser is checked against the recorded identity")]
    public void EnsureCompatible_ChecksAQueryAnalyser()
    {
        using var directory = Directory("query");
        Write(directory, Config(("body", AnalyserFactory.Create("de")), ("exact", AnalyserFactory.Create("de", stemmer: null))),
            Doc("1", body: "Tomaten", exact: "Tomaten"));

        IndexAnalysisIdentities.EnsureCompatible(directory, "body", AnalyserFactory.Create("de-AT"));
        IndexAnalysisIdentities.EnsureCompatible(directory, "never-written", AnalyserFactory.Create("fr"));
        IndexAnalysisIdentities.EnsureCompatible(directory, "exact", AnalyserFactory.Create("fr"));

        var exception = Assert.Throws<AnalysisIdentityMismatchException>(
            () => IndexAnalysisIdentities.EnsureCompatible(directory, "body", AnalyserFactory.Create("fr")));
        Assert.Equal(German, exception.RecordedIdentity);

        RemoveRecordedIdentities(directory);
        Assert.Throws<AnalysisIdentityMismatchException>(
            () => IndexAnalysisIdentities.EnsureCompatible(directory, "body", AnalyserFactory.Create("de")));
    }

    [Fact(DisplayName = "Analysis identity: an index with no commit reads as empty")]
    public void Read_WithoutACommit_IsEmpty()
    {
        using var directory = Directory("no-commit");
        Assert.Empty(IndexAnalysisIdentities.Read(directory)!);
        IndexAnalysisIdentities.EnsureCompatible(directory, "body", AnalyserFactory.Create("de"));
    }

    [Fact(DisplayName = "Analysis identity: identities survive merges and later commits")]
    public void Identities_SurviveMergesAndLaterCommits()
    {
        using var directory = Directory("merge");
        using (var writer = new IndexWriter(directory, GermanBody()))
        {
            for (int i = 0; i < 5; i++)
            {
                writer.AddDocument(Doc(i.ToString(System.Globalization.CultureInfo.InvariantCulture), body: "Tomaten"));
                writer.Commit();
            }
            writer.ForceMerge(1);
            writer.Commit();
        }

        Assert.Equal(German, IndexAnalysisIdentities.Read(directory)!["body"]);
        Assert.Throws<AnalysisIdentityMismatchException>(() => new IndexWriter(directory, Config(("body", AnalyserFactory.Create("fr")))));
    }

    [Fact(DisplayName = "Analysis identity: concurrent indexing records each field once")]
    public void ConcurrentIndexing_RecordsEachFieldOnce()
    {
        using var directory = Directory("concurrent");
        var config = GermanBody();
        config.IndexingConcurrency = 4;
        using (var writer = new IndexWriter(directory, config))
        {
            Parallel.For(0, 200, i => writer.AddDocument(Doc(i.ToString(System.Globalization.CultureInfo.InvariantCulture), body: "Tomaten", title: "Suppe")));
            writer.Commit();
        }

        var identities = IndexAnalysisIdentities.Read(directory)!;
        Assert.Equal(German, identities["body"]);
        Assert.Equal(Standard, identities["title"]);
    }

    private static IndexWriterConfig GermanBody() => Config(("body", AnalyserFactory.Create("de")));

    private static IndexWriterConfig Config(params (string Field, IAnalyser Analyser)[] fields)
    {
        var config = new IndexWriterConfig();
        foreach (var (field, analyser) in fields)
            config.FieldAnalysers[field] = analyser;
        return config;
    }

    private static void Write(MMapDirectory directory, IndexWriterConfig config, LeanDocument document)
    {
        using var writer = new IndexWriter(directory, config);
        writer.AddDocument(document);
        writer.Commit();
    }

    private static LeanDocument Doc(string id, string? body = null, string? title = null, string? exact = null)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        if (body is not null)
            document.Add(new TextField("body", body));
        if (title is not null)
            document.Add(new TextField("title", title));
        if (exact is not null)
            document.Add(new TextField("exact", exact));
        return document;
    }

    /// <summary>Rewrites the latest commit as a pre-identity commit, as LeanCorpus 3.x wrote it.</summary>
    private static void RemoveRecordedIdentities(MMapDirectory directory)
    {
        string latest = System.IO.Directory.GetFiles(directory.DirectoryPath, "segments_*")
            .Where(static path => !path.EndsWith(".pending", StringComparison.Ordinal))
            .OrderByDescending(static path => int.Parse(Path.GetFileName(path)["segments_".Length..], System.Globalization.CultureInfo.InvariantCulture))
            .First();
        var commit = JsonSerializer.Deserialize(CommitFileFormat.ReadJson(latest), LeanCorpusJsonContext.Default.CommitData)!;
        commit.FieldAnalysisIdentities = null;
        File.WriteAllText(latest, CommitFileFormat.Wrap(JsonSerializer.Serialize(commit, LeanCorpusJsonContext.Default.CommitData)));
    }

    private MMapDirectory Directory(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return new MMapDirectory(path);
    }
}
