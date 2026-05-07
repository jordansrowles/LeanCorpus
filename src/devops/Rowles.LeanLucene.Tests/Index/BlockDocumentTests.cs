using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Contains unit tests for Block Document.
/// </summary>
public sealed class BlockDocumentTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public BlockDocumentTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    /// <summary>
    /// Verifies the Add Document Block: Writes Parent Bit Set scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Block: Writes Parent Bit Set")]
    public void AddDocumentBlock_WritesParentBitSet()
    {
        var dir = Path.Combine(_path, nameof(AddDocumentBlock_WritesParentBitSet));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            // Block: 2 children + 1 parent = 3 docs, parent is doc 2
            var block = new LeanDocument[]
            {
                MakeChild("comment", "great article"),
                MakeChild("comment", "thanks for sharing"),
                MakeParent("blog post about dotnet")
            };
            writer.AddDocumentBlock(block);
            writer.Commit();
        }

        // Verify .pbs file exists
        var pbsFiles = Directory.GetFiles(dir, "*.pbs");
        Assert.NotEmpty(pbsFiles);

        var pbs = ParentBitSet.ReadFrom(pbsFiles[0]);
        Assert.False(pbs.IsParent(0));
        Assert.False(pbs.IsParent(1));
        Assert.True(pbs.IsParent(2));
    }

    /// <summary>
    /// Verifies the Add Document Block: Children Searchable scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Block: Children Searchable")]
    public void AddDocumentBlock_ChildrenSearchable()
    {
        var dir = Path.Combine(_path, nameof(AddDocumentBlock_ChildrenSearchable));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            writer.AddDocumentBlock(
            [
                MakeChild("comment", "brilliant analysis"),
                MakeChild("comment", "well written"),
                MakeParent("original post")
            ]);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new TermQuery("body", "brilliant"), 10);
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Block Join Query: Returns Parent For Matching Child scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Returns Parent For Matching Child")]
    public void BlockJoinQuery_ReturnsParentForMatchingChild()
    {
        var dir = Path.Combine(_path, nameof(BlockJoinQuery_ReturnsParentForMatchingChild));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            // Block 1: children match "alpha"
            writer.AddDocumentBlock(
            [
                MakeChild("reply", "alpha bravo"),
                MakeChild("reply", "charlie delta"),
                MakeParent("post about letters")
            ]);

            // Block 2: children match "echo"
            writer.AddDocumentBlock(
            [
                MakeChild("reply", "echo foxtrot"),
                MakeParent("post about nato")
            ]);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);

        // Search for child docs with "alpha", expect the parent of block 1
        var results = searcher.Search(
            new BlockJoinQuery(new TermQuery("body", "alpha")), 10);

        Assert.Equal(1, results.TotalHits);

        // The parent doc should have stored field "title"
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.True(stored.ContainsKey("title"));
        Assert.Contains("letters", stored["title"][0]);
    }

    /// <summary>
    /// Verifies the Block Join Query: Multiple Blocks Correct Parent Mapping scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Multiple Blocks Correct Parent Mapping")]
    public void BlockJoinQuery_MultipleBlocks_CorrectParentMapping()
    {
        var dir = Path.Combine(_path, nameof(BlockJoinQuery_MultipleBlocks_CorrectParentMapping));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            // Block 1: parent at doc 2
            writer.AddDocumentBlock(
            [
                MakeChild("tag", "science"),
                MakeChild("tag", "nature"),
                MakeParent("article one")
            ]);

            // Block 2: parent at doc 5
            writer.AddDocumentBlock(
            [
                MakeChild("tag", "technology"),
                MakeChild("tag", "science"),
                MakeParent("article two")
            ]);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);

        // "science" appears in both blocks — should return both parents
        var results = searcher.Search(
            new BlockJoinQuery(new TermQuery("body", "science")), 10);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Block Join Query: Parent Match Is Not Treated As Own Child scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Parent Match Is Not Treated As Own Child")]
    public void BlockJoinQuery_ParentMatch_IsNotTreatedAsOwnChild()
    {
        var dir = Path.Combine(_path, nameof(BlockJoinQuery_ParentMatch_IsNotTreatedAsOwnChild));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            writer.AddDocumentBlock(
            [
                MakeChild("reply", "child text"),
                MakeParent("parent-only token")
            ]);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new BlockJoinQuery(new TermQuery("title", "parent")), 10);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Add Document Block: Requires At Least Two Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Block: Requires At Least Two Docs")]
    public void AddDocumentBlock_RequiresAtLeastTwoDocs()
    {
        var dir = Path.Combine(_path, nameof(AddDocumentBlock_RequiresAtLeastTwoDocs));
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        Assert.Throws<ArgumentException>(() => writer.AddDocumentBlock([MakeParent("solo")]));
    }

    /// <summary>
    /// Verifies the Parent Bit Set: Next Parent Finds Correct Parent scenario.
    /// </summary>
    [Fact(DisplayName = "Parent Bit Set: Next Parent Finds Correct Parent")]
    public void ParentBitSet_NextParent_FindsCorrectParent()
    {
        var pbs = new ParentBitSet(10);
        pbs.Set(3);
        pbs.Set(7);

        Assert.Equal(3, pbs.NextParent(0));
        Assert.Equal(3, pbs.NextParent(3));
        Assert.Equal(7, pbs.NextParent(4));
        Assert.Equal(-1, pbs.NextParent(8));
    }

    /// <summary>
    /// Verifies the Parent Bit Set: Prev Parent Finds Correct Parent scenario.
    /// </summary>
    [Fact(DisplayName = "Parent Bit Set: Prev Parent Finds Correct Parent")]
    public void ParentBitSet_PrevParent_FindsCorrectParent()
    {
        var pbs = new ParentBitSet(10);
        pbs.Set(3);
        pbs.Set(7);

        Assert.Equal(3, pbs.PrevParent(7));
        Assert.Equal(-1, pbs.PrevParent(3));
        Assert.Equal(7, pbs.PrevParent(8));
    }

    /// <summary>
    /// Verifies the Parent Bit Set: Round-trip scenario.
    /// </summary>
    [Fact(DisplayName = "Parent Bit Set: Round-trip")]
    public void ParentBitSet_RoundTrip()
    {
        var dir = Path.Combine(_path, nameof(ParentBitSet_RoundTrip));
        Directory.CreateDirectory(dir);
        var pbsPath = Path.Combine(dir, "test.pbs");

        var pbs = new ParentBitSet(100);
        pbs.Set(5);
        pbs.Set(15);
        pbs.Set(99);
        pbs.WriteTo(pbsPath);

        var loaded = ParentBitSet.ReadFrom(pbsPath);
        Assert.True(loaded.IsParent(5));
        Assert.True(loaded.IsParent(15));
        Assert.True(loaded.IsParent(99));
        Assert.False(loaded.IsParent(0));
        Assert.False(loaded.IsParent(50));
    }

    private static LeanDocument MakeChild(string fieldName, string value)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", value));
        doc.Add(new StringField("type", fieldName));
        return doc;
    }

    private static LeanDocument MakeParent(string title)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("title", title));
        doc.Add(new StringField("type", "parent"));
        return doc;
    }
}
