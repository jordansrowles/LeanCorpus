using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Verifies that positions left by removed stop words survive indexing, merging, compound files,
/// deletes and quoted-phrase query construction.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class PositionGapIndexTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public PositionGapIndexTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    private static readonly (string Id, string Text)[] Documents =
    [
        ("collapsed", "said government"),
        ("gap-one", "said the government"),
        ("gap-two", "said the of government"),
        ("probe", "said aspernatur at fugit government"),
        ("deleted", "said the government"),
    ];

    [Theory(DisplayName = "Parse: a quoted phrase keeps the position of a removed stop word")]
    [InlineData("said government", new[] { 0, 1 })]
    [InlineData("said the government", new[] { 0, 2 })]
    [InlineData("said the of government", new[] { 0, 3 })]
    public void Parse_QuotedPhrase_UsesGappedPositions(string phrase, int[] expectedPositions)
    {
        var parser = new QueryParser("body", new StandardAnalyser());

        var query = Assert.IsType<PhraseQuery>(parser.Parse("\"" + phrase + "\""));

        Assert.Equal(["said", "government"], query.Terms);
        Assert.Equal(expectedPositions, query.Positions);
    }

    [Fact(DisplayName = "Parse: a leading removed stop word does not move the phrase origin")]
    public void Parse_LeadingStopWord_DoesNotMoveThePhraseOrigin()
    {
        var parser = new QueryParser("body", new StandardAnalyser());

        var query = Assert.IsType<PhraseQuery>(parser.Parse("\"the cat sat\""));

        Assert.Equal(["cat", "sat"], query.Terms);
        Assert.Equal([0, 1], query.Positions);
    }

    [Fact(DisplayName = "Index: gapped positions give the same exact and sloppy results at every stage")]
    public void Index_GappedPositions_SurviveFlushMergeCompoundFilesAndDeletes()
    {
        foreach (bool compound in new[] { false, true })
        {
            var path = SubDir("gaps_" + (compound ? "compound" : "loose"));
            var directory = new MMapDirectory(path);
            var config = new IndexWriterConfig
            {
                MaxBufferedDocs = 1,
                MergePolicy = NoMergePolicy.Instance,
                UseCompoundFile = compound
            };
            using (var writer = new IndexWriter(directory, config))
            {
                foreach (var (id, text) in Documents)
                {
                    var doc = new LeanDocument();
                    doc.Add(new StringField("id", id));
                    doc.Add(new TextField("body", text));
                    writer.AddDocument(doc);
                    writer.Commit();
                }

                AssertResults(directory, "five flushed segments, compound=" + compound, withDeleted: true);

                writer.DeleteDocuments(new TermQuery("id", "deleted"));
                writer.Commit();
                AssertResults(directory, "after the delete, compound=" + compound, withDeleted: false);

                writer.ForceMerge(1);
                AssertResults(directory, "after the merge, compound=" + compound, withDeleted: false);
            }

            AssertResults(directory, "after the writer closed, compound=" + compound, withDeleted: false);
        }
    }

    private static void AssertResults(MMapDirectory directory, string stage, bool withDeleted)
    {
        using var searcher = new IndexSearcher(directory);
        string[] Ids(Query query) => searcher.Search(query, 20, TestContext.Current.CancellationToken)
            .ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).Order(StringComparer.Ordinal).ToArray();
        string[] gapOne = withDeleted ? ["deleted", "gap-one"] : ["gap-one"];

        Assert.True(new[] { "collapsed" }.SequenceEqual(Ids(new PhraseQuery("body", "said", "government"))), stage + ": exact said government");
        Assert.True(gapOne.SequenceEqual(Ids(new PhraseQuery("body", ["said", "government"], [0, 2]))), stage + ": exact positions 0 and 2");
        Assert.True(new[] { "gap-two" }.SequenceEqual(Ids(new PhraseQuery("body", ["said", "government"], [0, 3]))), stage + ": exact positions 0 and 3");

        // The probe document puts three positions between the terms, so a slop of two rejects it.
        Assert.DoesNotContain("probe", Ids(new PhraseQuery("body", 2, "said", "government")));
        Assert.Contains("probe", Ids(new PhraseQuery("body", 3, "said", "government")));
    }

    [Fact(DisplayName = "Metamorphic: inserting a stop word between terms never reduces the slop a phrase needs")]
    public void Metamorphic_InsertingAStopWord_NeverReducesTheRequiredSlop()
    {
        var path = SubDir("gaps_metamorphic");
        var directory = new MMapDirectory(path);
        string[] inserts = ["", " the", " the of", " the of in"];
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            for (int i = 0; i < inserts.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new TextField("body", "alpha" + inserts[i] + " beta"));
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        int RequiredSlop(int document)
        {
            for (int slop = 0; slop <= 6; slop++)
            {
                var hits = searcher.Search(new PhraseQuery("body", slop, "alpha", "beta"), 10, TestContext.Current.CancellationToken);
                if (hits.ScoreDocs.Any(hit => searcher.GetStoredFields(hit.DocId)["id"][0] == document.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    return slop;
            }

            return int.MaxValue;
        }

        var required = Enumerable.Range(0, inserts.Length).Select(RequiredSlop).ToArray();
        Assert.Equal(0, required[0]);
        for (int i = 1; i < required.Length; i++)
            Assert.True(required[i] >= required[i - 1], $"Inserting more stop words reduced the required slop: [{string.Join(", ", required)}].");
        Assert.Equal([0, 1, 2, 3], required);
    }
}
