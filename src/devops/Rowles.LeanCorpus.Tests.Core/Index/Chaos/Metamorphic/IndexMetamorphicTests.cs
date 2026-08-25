using FsCheck.Xunit;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;
using Rowles.LeanCorpus.Tests.Shared.Metamorphic;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.Metamorphic;

[Category(TestCategory.Chaos)]
[Area(TestArea.Index)]
[Technique(TestTechnique.PropertyBased)]
[Technique(TestTechnique.Metamorphic)]
public sealed class IndexMetamorphicTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public IndexMetamorphicTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(DisplayName = "Sequential and concurrent writers are set-equivalent for generated corpora", MaxTest = 30)]
    public void Sequential_and_concurrent_writers_are_set_equivalent(int[] values)
    {
        var documents = CreateDocuments(values);
        var baseline = Observe(documents, static (writer, docs) =>
        {
            foreach (var document in docs)
                writer.AddDocument(document);
        });
        var transformed = Observe(documents, static (writer, docs) => writer.AddDocumentsConcurrent(docs));

        Assert.True(
            MetamorphicRelations.Holds(MetamorphicRelation.SetEquivalent, baseline, transformed),
            MetamorphicRelations.Describe(MetamorphicRelation.SetEquivalent, baseline, transformed));
    }

    [Property(DisplayName = "Force merge preserves generated logical search results", MaxTest = 20)]
    public void Force_merge_preserves_logical_results(int[] values)
    {
        var documents = CreateDocuments(values);
        var baseline = Observe(documents, static (writer, docs) =>
        {
            foreach (var document in docs)
                writer.AddDocument(document);
        });
        var transformed = Observe(documents, static (writer, docs) =>
        {
            foreach (var document in docs)
                writer.AddDocument(document);
            writer.Commit();
            writer.ForceMerge(1);
        });

        Assert.True(
            MetamorphicRelations.Holds(MetamorphicRelation.SetEquivalent, baseline, transformed),
            MetamorphicRelations.Describe(MetamorphicRelation.SetEquivalent, baseline, transformed));
    }

    private MetamorphicObservation Observe(
        IReadOnlyList<LeanDocument> documents,
        Action<IndexWriter, IReadOnlyList<LeanDocument>> index)
    {
        string path = Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig
        {
            MaxBufferedDocs = 2,
            MergePolicy = NoMergePolicy.Instance
        }))
        {
            index(writer, documents);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var results = searcher.Search(new MatchAllDocsQuery(), Math.Max(1, documents.Count + 1));
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var scoreDocument in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(scoreDocument.DocId);
            string id = stored["id"][0];
            ids.Add(id);
            fields.Add(id, [stored["category"][0], stored["body"][0]]);
        }

        ids.Sort(StringComparer.Ordinal);
        return new MetamorphicObservation(ids, fields);
    }

    private static IReadOnlyList<LeanDocument> CreateDocuments(int[] values)
    {
        values ??= [];
        var documents = new List<LeanDocument>(Math.Clamp(values.Length, 1, 24));
        foreach (var (value, index) in values.Take(24).Select(static (value, index) => (value, index)))
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", $"doc-{index}"));
            document.Add(new StringField("category", value % 2 == 0 ? "even" : "odd"));
            document.Add(new TextField("body", $"value {Math.Abs((long)value)}"));
            documents.Add(document);
        }

        if (documents.Count == 0)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", "doc-0"));
            document.Add(new StringField("category", "even"));
            document.Add(new TextField("body", "value 0"));
            documents.Add(document);
        }

        return documents;
    }
}
