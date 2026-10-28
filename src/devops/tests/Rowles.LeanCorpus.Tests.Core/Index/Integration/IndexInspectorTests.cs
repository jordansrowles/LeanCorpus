using System.Security.Cryptography;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Inspection;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexInspectorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexInspectorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "IndexInspector: exposes committed overview and typed field inspection")]
    public void Inspect_ReportsOverviewFieldsTermsPostingsAndDocumentValues()
    {
        string path = Path.Combine(_fixture.Path, "index-inspector-contract");
        Directory.CreateDirectory(path);
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { StoreTermVectors = true }))
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", "doc-1"));
            document.Add(new TextField("body", "hello hello world", stored: true));
            document.Add(new NumericField("price", 12.5));
            document.Add(new BinaryField("payload", new byte[] { 1, 2, 3 }));
            document.Add(new VectorField("embedding", new float[] { 0.25f, 0.75f }));
            writer.AddDocument(document);
            writer.Commit();
        }

        using var inspector = new IndexInspector(path);
        Assert.Equal(1, inspector.Overview.LiveDocumentCount);
        Assert.Equal(1, inspector.Overview.DocumentCount);
        Assert.Single(inspector.GetSegments());
        Assert.True(inspector.GetSizeReport().TotalSizeBytes > 0);

        IndexInspectionField body = Assert.Single(inspector.GetFields(), static field => field.Name == "body");
        Assert.True(body.Indexed);
        Assert.True(body.Stored);
        Assert.True(body.HasTermVectors);
        Assert.Equal(2, body.UniqueTermCount);
        Assert.Equal(3, body.TotalTermFrequency);
        IndexInspectionField id = Assert.Single(inspector.GetFields(), static field => field.Name == "id");
        Assert.False(id.HasTermVectors);
        IndexInspectionField embedding = Assert.Single(inspector.GetFields(), static field => field.Name == "embedding");
        Assert.Equal(2, embedding.VectorDimension);

        IndexInspectionTerm hello = Assert.Single(inspector.GetTerms("body", prefix: "hel"));
        Assert.Equal("hello", hello.Term);
        Assert.Equal(1, hello.DocumentFrequency);
        Assert.Equal(2, hello.TotalTermFrequency);
        IndexInspectionPosting posting = Assert.Single(inspector.GetPostings("body", "hello"));
        Assert.Equal(2, posting.Frequency);
        Assert.Equal(new[] { 0, 1 }, posting.Positions);

        IndexInspectionDocument inspected = inspector.GetDocument(0);
        Assert.True(inspected.IsLive);
        Assert.Equal("hello hello world", Assert.Single(inspected.StoredFields["body"]));
        Assert.Equal(3, Assert.Single(inspected.BinaryFieldLengths["payload"]));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 })),
            Assert.Single(inspected.BinaryFieldHashes["payload"]));
        Assert.Contains(inspected.DocValues, static value => value.FieldName == "price" && value.Kind == "numeric");
        Assert.Equal(2, inspected.Vectors["embedding"].Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => inspector.GetDocument(1));
    }
}
