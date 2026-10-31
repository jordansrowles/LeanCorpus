using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class BbqV3RetrievalTests : IClassFixture<TestDirectoryFixture>
{
    private const int Dimension = 128;
    private readonly TestDirectoryFixture _fixture;

    public BbqV3RetrievalTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "BBQ v3: Omitted oversampling uses three candidates per result")]
    public void DefaultOversampling_IsThreeForBbqV3AndOneForOtherRepresentations()
    {
        using var bbq = BuildIndex("bbq_v3_auto_oversampling", VectorQuantisation.BBQ, buildGraph: true, 24, seed: 71);
        using var bbqFlat = BuildIndex("bbq_v3_auto_oversampling_flat", VectorQuantisation.BBQ, buildGraph: false, 24, seed: 74);
        using var int8 = BuildIndex("int8_auto_oversampling", VectorQuantisation.Int8, buildGraph: true, 24, seed: 72);
        using var bbqSearcher = new IndexSearcher(bbq);
        using var bbqFlatSearcher = new IndexSearcher(bbqFlat);
        using var int8Searcher = new IndexSearcher(int8);

        var automatic = new VectorQuery("embedding", BuildVector(seed: 73), topK: 5, efSearch: 64);
        Assert.Equal(1, automatic.OversamplingFactor);
        var bbqExplanation = bbqSearcher.Explain(automatic, 0);
        var int8Explanation = int8Searcher.Explain(automatic, 0);

        Assert.NotNull(bbqExplanation);
        Assert.NotNull(int8Explanation);
        Assert.Contains(bbqExplanation!.Details, detail => detail.Description.Contains("shortlistSize=15", StringComparison.Ordinal));
        Assert.Contains(int8Explanation!.Details, detail => detail.Description.Contains("shortlistSize=5", StringComparison.Ordinal));
        var bbqFlatExplanation = bbqFlatSearcher.Explain(automatic, 0);
        Assert.NotNull(bbqFlatExplanation);
        Assert.Contains("flat-scan", bbqFlatExplanation!.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bbqFlatExplanation.Details,
            detail => detail.Description.Contains("shortlistSize=15", StringComparison.Ordinal));

        var explicitOne = new VectorQuery("embedding", BuildVector(seed: 73), topK: 5, efSearch: 64, oversamplingFactor: 1);
        var explicitZero = new VectorQuery("embedding", BuildVector(seed: 73), topK: 5, efSearch: 64, oversamplingFactor: 0);
        Assert.Contains(bbqSearcher.Explain(explicitOne, 0)!.Details,
            detail => detail.Description.Contains("shortlistSize=5", StringComparison.Ordinal));
        Assert.Contains(bbqSearcher.Explain(explicitZero, 0)!.Details,
            detail => detail.Description.Contains("shortlistSize=5", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "BBQ v3: Final ranking and scores use decoded canonical Int8 vectors")]
    public void Search_ReranksEveryCandidateFromCanonicalVectorsAndKeepsTiesStable()
    {
        const int count = 24;
        float[][] vectors = BuildVectors(count, seed: 81);
        vectors[1] = (float[])vectors[0].Clone();
        using var directory = BuildIndex(SubDir("bbq_v3_canonical_rerank"), vectors, VectorQuantisation.BBQ, buildGraph: true);
        using var searcher = new IndexSearcher(directory);
        var query = new VectorQuery("embedding", vectors[0], topK: count, efSearch: count);

        TopDocs results = searcher.Search(query, count, TestContext.Current.CancellationToken);
        Assert.Equal(count, results.ScoreDocs.Length);

        var segments = searcher.GetSegmentReaders();
        Assert.Equal(count, segments.Sum(static segment => segment.MaxDoc));
        var expected = Enumerable.Range(0, count)
            .Select(docId =>
            {
                var segment = Assert.Single(segments.Where(segment =>
                    docId >= segment.DocBase && docId < segment.DocBase + segment.MaxDoc));
                float[] canonical = Assert.IsType<float[]>(segment.GetVector("embedding", docId - segment.DocBase));
                return (DocId: docId, Score: CosineSimilarityDouble(query.QueryVector, canonical));
            })
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.DocId)
            .ToArray();

        Assert.Equal(expected.Select(static item => item.DocId), results.ScoreDocs.Select(static hit => hit.DocId));
        for (int i = 0; i < results.ScoreDocs.Length; i++)
            Assert.InRange(Math.Abs(results.ScoreDocs[i].Score - expected[i].Score), 0d, 1e-6d);
        Assert.Equal(0, results.ScoreDocs[0].DocId);
        Assert.Equal(1, results.ScoreDocs[1].DocId);

        int[] expectedOrder = results.ScoreDocs.Select(static hit => hit.DocId).ToArray();
        var parallelOrders = new int[4][];
        Parallel.For(0, parallelOrders.Length, i =>
        {
            parallelOrders[i] = searcher.Search(query, count, TestContext.Current.CancellationToken)
                .ScoreDocs.Select(static hit => hit.DocId).ToArray();
        });
        foreach (int[] order in parallelOrders)
            Assert.Equal(expectedOrder, order);
    }

    [Fact(DisplayName = "BBQ v3: Filtered HNSW results exclude deleted and non-matching documents")]
    public void Search_FilteredShortlist_ContainsOnlyLiveMatchingDocuments()
    {
        const int count = 96;
        float[][] vectors = BuildVectors(count, seed: 91);
        using var directory = new MMapDirectory(SubDir("bbq_v3_filtered_deleted"));
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            VectorQuantisation = VectorQuantisation.BBQ,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1337L,
        };

        using (var writer = new IndexWriter(directory, config))
        {
            for (int i = 0; i < count; i++)
            {
                var document = new LeanDocument();
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vectors[i])));
                document.Add(new TextField("category", i < 80 ? "keep" : "skip"));
                document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                writer.AddDocument(document);
            }
            writer.DeleteDocuments(new TermQuery("id", "12"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory);
        var query = new VectorQuery(
            "embedding",
            BuildVector(seed: 92),
            topK: 5,
            efSearch: 64,
            filter: new TermQuery("category", "keep"));
        TopDocs results = searcher.Search(query, 5, TestContext.Current.CancellationToken);

        Assert.NotEmpty(results.ScoreDocs);
        Assert.DoesNotContain(results.ScoreDocs, static hit => hit.DocId == 12);
        Assert.All(results.ScoreDocs, static hit => Assert.InRange(hit.DocId, 0, 79));
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private MMapDirectory BuildIndex(
        string name,
        VectorQuantisation quantisation,
        bool buildGraph,
        int count,
        int seed)
        => BuildIndex(SubDir(name), BuildVectors(count, seed), quantisation, buildGraph);

    private static MMapDirectory BuildIndex(
        string path,
        float[][] vectors,
        VectorQuantisation quantisation,
        bool buildGraph)
    {
        var directory = new MMapDirectory(path);
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = buildGraph,
            NormaliseVectors = true,
            VectorQuantisation = quantisation,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1337L,
        };

        using (var writer = new IndexWriter(directory, config))
        {
            foreach (float[] vector in vectors)
            {
                var document = new LeanDocument();
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
                writer.AddDocument(document);
            }
            writer.Commit();
        }
        return directory;
    }

    private static float[][] BuildVectors(int count, int seed)
    {
        var random = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            var vector = new float[Dimension];
            for (int d = 0; d < vector.Length; d++)
                vector[d] = (float)(random.NextDouble() * 2 - 1);
            vectors[i] = vector;
        }
        return vectors;
    }

    private static float[] BuildVector(int seed) => BuildVectors(1, seed)[0];

    private static double CosineSimilarityDouble(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (int i = 0; i < left.Length; i++)
        {
            dot += (double)left[i] * right[i];
            leftNorm += (double)left[i] * left[i];
            rightNorm += (double)right[i] * right[i];
        }
        return dot / Math.Sqrt(leftNorm * rightNorm);
    }
}
