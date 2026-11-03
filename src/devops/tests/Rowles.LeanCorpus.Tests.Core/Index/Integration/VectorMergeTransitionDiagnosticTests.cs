using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class VectorMergeTransitionDiagnosticTests : IDisposable
{
    private const int Dimension = 4;
    private const int TopK = 10;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"vector-merge-transition-{Guid.NewGuid():N}");

    public VectorMergeTransitionDiagnosticTests() => Directory.CreateDirectory(_path);

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Fact]
    public void SeparatesCorpusGrowthQualityFromMergeTransitionQuality()
    {
        string sourcePath = Path.Combine(_path, "source");
        string targetPath = Path.Combine(_path, "target");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        using var sourceDirectory = new MMapDirectory(sourcePath);
        using var targetDirectory = new MMapDirectory(targetPath);
        var originalVectors = new Dictionary<string, float[]?>(StringComparer.Ordinal);
        var rowAffineVectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var deletedIds = new HashSet<string>(StringComparer.Ordinal);

        using (var sourceWriter = new IndexWriter(sourceDirectory, CreateConfig()))
        {
            AddVectorDocuments(sourceWriter, originalVectors, rowAffineVectors, 0, 24);
            sourceWriter.Commit();
        }

        using var writer = new IndexWriter(targetDirectory, CreateConfig());
        writer.AddIndexes(sourceDirectory);
        AddVectorDocuments(writer, originalVectors, rowAffineVectors, 24, 24);
        writer.Commit();
        deletedIds.Add(FormatId(7));
        writer.DeleteDocuments(new TermQuery("id", FormatId(7)));
        writer.Commit();

        var generations = new List<object>(4);
        for (int generation = 1; generation <= 4; generation++)
        {
            int lastId = 47;
            if (generation > 1)
            {
                int firstId = (generation * 16) + 16;
                AddVectorDocuments(writer, originalVectors, rowAffineVectors, firstId, 16);
                writer.Commit();
                int deletedId = firstId + 7;
                deletedIds.Add(FormatId(deletedId));
                writer.DeleteDocuments(new TermQuery("id", FormatId(deletedId)));
                writer.Commit();
                lastId = firstId + 15;
            }

            var before = Measure(targetDirectory, originalVectors, deletedIds, lastId);
            var rowAffineBefore = MeasureRowAffine(originalVectors, rowAffineVectors, deletedIds, lastId);
            int segmentsBefore = IndexRecovery.RecoverLatestCommit(targetPath, cleanupOrphans: false)!
                .SegmentInfos.Count;

            _ = writer.ForceMerge(1);
            writer.Commit();
            RequantiseRowAffine(rowAffineVectors, deletedIds, lastId);

            var after = Measure(targetDirectory, originalVectors, deletedIds, lastId);
            var rowAffineAfter = MeasureRowAffine(originalVectors, rowAffineVectors, deletedIds, lastId);
            int segmentsAfter = IndexRecovery.RecoverLatestCommit(targetPath, cleanupOrphans: false)!
                .SegmentInfos.Count;
            generations.Add(new
            {
                generation, lastId, segmentsBefore, before, rowAffineBefore,
                segmentsAfter, after, rowAffineAfter
            });
        }

        string? diagnosticDirectory = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(diagnosticDirectory))
            diagnosticDirectory = Path.GetTempPath();
        Directory.CreateDirectory(diagnosticDirectory);
        File.WriteAllText(
            Path.Combine(diagnosticDirectory, "leancorpus-vector-merge-transition-diagnostic.json"),
            JsonSerializer.Serialize(generations, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(4, generations.Count);
    }

    private static IndexWriterConfig CreateConfig() => new()
    {
        MaxBufferedDocs = 24,
        MergePolicy = NoMergePolicy.Instance,
        MergeThreshold = 100,
        BuildHnswOnFlush = false,
        NormaliseVectors = false,
        VectorQuantisation = VectorQuantisation.Int8,
        IndexSort = new IndexSort(SortField.String("id")),
    };

    private static object Measure(
        MMapDirectory directory,
        IReadOnlyDictionary<string, float[]?> originalVectors,
        IReadOnlySet<string> deletedIds,
        int lastId)
    {
        using var searcher = new IndexSearcher(directory);
        var recalls = new double[Dimension];
        var queries = new object[Dimension];
        for (int group = 0; group < Dimension; group++)
        {
            float[] queryVector = new float[Dimension];
            queryVector[group] = 1f;
            string[] expected = originalVectors
                .Where(pair => ParseId(pair.Key) <= lastId && !deletedIds.Contains(pair.Key) && pair.Value is not null)
                .OrderByDescending(pair => Cosine(queryVector, pair.Value!))
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(TopK)
                .Select(static pair => pair.Key)
                .ToArray();
            string[] actual = searcher.Search(
                    new VectorQuery("embedding", queryVector, TopK, oversamplingFactor: 1),
                    TopK,
                    TestContext.Current.CancellationToken)
                .ScoreDocs
                .Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single())
                .ToArray();
            recalls[group] = actual.Intersect(expected, StringComparer.Ordinal).Count() / (double)TopK;
            queries[group] = new { group, recallAt10 = recalls[group], expected, actual };
        }

        return new
        {
            meanRecallAt10 = recalls.Average(),
            queries,
        };
    }

    private static object MeasureRowAffine(
        IReadOnlyDictionary<string, float[]?> originalVectors,
        IReadOnlyDictionary<string, float[]> rowAffineVectors,
        IReadOnlySet<string> deletedIds,
        int lastId)
    {
        var recalls = new double[Dimension];
        var queries = new object[Dimension];
        for (int group = 0; group < Dimension; group++)
        {
            float[] queryVector = new float[Dimension];
            queryVector[group] = 1f;
            string[] expected = originalVectors
                .Where(pair => ParseId(pair.Key) <= lastId && !deletedIds.Contains(pair.Key) && pair.Value is not null)
                .OrderByDescending(pair => Cosine(queryVector, pair.Value!))
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(TopK)
                .Select(static pair => pair.Key)
                .ToArray();
            string[] actual = rowAffineVectors
                .Where(pair => ParseId(pair.Key) <= lastId && !deletedIds.Contains(pair.Key))
                .OrderByDescending(pair => Cosine(queryVector, pair.Value))
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .Take(TopK)
                .Select(static pair => pair.Key)
                .ToArray();
            recalls[group] = actual.Intersect(expected, StringComparer.Ordinal).Count() / (double)TopK;
            queries[group] = new { group, recallAt10 = recalls[group], expected, actual };
        }

        return new { meanRecallAt10 = recalls.Average(), queries };
    }

    private static void RequantiseRowAffine(
        Dictionary<string, float[]> rowAffineVectors,
        IReadOnlySet<string> deletedIds,
        int lastId)
    {
        foreach (string id in rowAffineVectors.Keys
                     .Where(id => ParseId(id) <= lastId && !deletedIds.Contains(id))
                     .ToArray())
            rowAffineVectors[id] = QuantisePerVectorAffine(rowAffineVectors[id]);
    }

    private static void AddVectorDocuments(
        IndexWriter writer,
        Dictionary<string, float[]?> originalVectors,
        Dictionary<string, float[]> rowAffineVectors,
        int firstId,
        int count)
    {
        for (int id = firstId; id < firstId + count; id++)
        {
            string documentId = FormatId(id);
            float[]? vector = CreateVector(id);
            originalVectors.Add(documentId, vector);
            if (vector is not null)
                rowAffineVectors.Add(documentId, QuantisePerVectorAffine(vector));
            var document = new LeanDocument();
            document.Add(new StringField("id", documentId, stored: true));
            if (vector is not null)
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
            writer.AddDocument(document);
        }
    }

    private static float[]? CreateVector(int id)
    {
        if (id != 0 && id % 9 == 0)
            return null;
        if (id == 0)
            return new float[Dimension];

        int group = id % Dimension;
        int rank = id / Dimension;
        var vector = new float[Dimension];
        vector[group] = 0.6f + rank * 0.025f;
        for (int component = 0; component < Dimension; component++)
        {
            if (component == group)
                continue;
            int noise = ((id * 7 + component * 11) % 9) - 4;
            vector[component] = noise * 0.015f;
        }
        return vector;
    }

    private static float[] QuantisePerVectorAffine(ReadOnlySpan<float> vector)
    {
        float min = vector[0];
        float max = vector[0];
        for (int component = 1; component < vector.Length; component++)
        {
            min = MathF.Min(min, vector[component]);
            max = MathF.Max(max, vector[component]);
        }

        float alpha = (max - min) / 255f;
        if (alpha == 0f)
            return vector.ToArray();

        var decoded = new float[vector.Length];
        for (int component = 0; component < vector.Length; component++)
        {
            byte code = (byte)Math.Clamp((vector[component] - min) / alpha + 0.5f, 0f, 255f);
            decoded[component] = min + alpha * code;
        }
        return decoded;
    }

    private static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0d;
        double leftSquared = 0d;
        double rightSquared = 0d;
        for (int component = 0; component < left.Length; component++)
        {
            dot += left[component] * right[component];
            leftSquared += left[component] * left[component];
            rightSquared += right[component] * right[component];
        }
        return leftSquared == 0d || rightSquared == 0d
            ? 0d
            : dot / Math.Sqrt(leftSquared * rightSquared);
    }

    private static string FormatId(int id) => $"doc-{id:D3}";

    private static int ParseId(string id) => int.Parse(id.AsSpan("doc-".Length));
}
