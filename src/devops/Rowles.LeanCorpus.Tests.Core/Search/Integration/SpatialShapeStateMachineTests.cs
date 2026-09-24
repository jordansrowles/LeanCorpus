using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.XY;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class SpatialShapeStateMachineTests : IDisposable
{
    private const int ReplaySeed = 0x7032_034;
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "leancorpus_shape_model_" + Guid.NewGuid().ToString("N"));

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact(DisplayName = "Seeded shape updates, deletes, commits and force merges match the rectangle relation model")]
    public void SeededShapeLifecycle_MatchesIndependentRelationModel()
    {
        var random = new Random(ReplaySeed);
        var model = new Dictionary<string, ShapeDocument>(StringComparer.Ordinal);
        int nextId = 0;

        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BKDMaxLeafSize = 3,
            MaxBufferedDocs = 2,
            MergePolicy = NoMergePolicy.Instance,
            UseCompoundFile = true,
        }))
        {
            for (int step = 0; step < 48; step++)
            {
                int operation = model.Count == 0 ? 0 : random.Next(100);
                if (operation < 48)
                {
                    string id = $"shape-{nextId++:D3}";
                    ShapeDocument added = CreateModelDocument(id, random);
                    writer.AddDocument(ToDocument(added));
                    model.Add(id, added);
                }
                else
                {
                    string id = model.Keys.ElementAt(random.Next(model.Count));
                    if (operation < 78)
                    {
                        ShapeDocument replacement = CreateModelDocument(id, random);
                        writer.UpdateDocument("id", id, ToDocument(replacement));
                        model[id] = replacement;
                    }
                    else
                    {
                        writer.DeleteDocuments(new TermQuery("id", id));
                        model.Remove(id);
                    }
                }

                if (step % 8 == 7 || step == 47)
                {
                    if (step == 23)
                        _ = writer.ForceMerge(1);
                    writer.Commit();
                    ValidateAgainstModel(model, random, step);
                }
            }
        }
    }

    private void ValidateAgainstModel(
        IReadOnlyDictionary<string, ShapeDocument> model,
        Random random,
        int step)
    {
        using var directory = new MMapDirectory(_path);
        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig { ParallelSearch = false });
        for (int queryIndex = 0; queryIndex < 4; queryIndex++)
        {
            Box query = RandomBox(random);
            var geometry = new XYRectangle(query.MinX, query.MinY, query.MaxX, query.MaxY);
            foreach (SpatialRelation relation in Enum.GetValues<SpatialRelation>())
            {
                string[] expected = model.Values
                    .Where(document => OracleMatches(document.Values, query, relation))
                    .Select(static document => document.Id)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                string[] actual = searcher.Search(
                        new XYShapeQuery("shape", relation, geometry),
                        Math.Max(1, model.Count + 1),
                        TestContext.Current.CancellationToken)
                    .ScoreDocs
                    .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                Assert.True(
                    expected.SequenceEqual(actual),
                    $"Seed 0x7032034, checkpoint {step}, query {queryIndex}, relation {relation}, query box {query}: expected [{string.Join(',', expected)}], actual [{string.Join(',', actual)}].");
            }
        }
    }

    private static bool OracleMatches(IReadOnlyList<Box> values, Box query, SpatialRelation relation)
    {
        if (values.Count == 0)
            return false;

        return relation switch
        {
            SpatialRelation.Intersects => values.Any(value => value.Intersects(query)),
            SpatialRelation.Within => values.All(value => query.Contains(value)),
            SpatialRelation.Contains => values.Any(value => value.Contains(query)),
            SpatialRelation.Disjoint => values.All(value => !value.Intersects(query)),
            _ => throw new ArgumentOutOfRangeException(nameof(relation)),
        };
    }

    private static ShapeDocument CreateModelDocument(string id, Random random)
    {
        int valueCount = random.Next(5) == 0 ? 0 : 1 + random.Next(2);
        Box[] values = Enumerable.Range(0, valueCount).Select(_ => RandomBox(random)).ToArray();
        return new ShapeDocument(id, values);
    }

    private static Box RandomBox(Random random)
    {
        int minX = random.Next(-40, 41);
        int minY = random.Next(-40, 41);
        return new Box(minX, minY, minX + random.Next(1, 16), minY + random.Next(1, 16));
    }

    private static LeanDocument ToDocument(ShapeDocument state)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", state.Id));
        foreach (Box value in state.Values)
            document.Add(new XYShapeField("shape", new XYRectangle(value.MinX, value.MinY, value.MaxX, value.MaxY)));
        return document;
    }

    private sealed record ShapeDocument(string Id, Box[] Values);

    private readonly record struct Box(float MinX, float MinY, float MaxX, float MaxY)
    {
        internal bool Intersects(Box other)
            => MaxX >= other.MinX && MinX <= other.MaxX && MaxY >= other.MinY && MinY <= other.MaxY;

        internal bool Contains(Box other)
            => MinX <= other.MinX && MinY <= other.MinY && MaxX >= other.MaxX && MaxY >= other.MaxY;
    }
}
