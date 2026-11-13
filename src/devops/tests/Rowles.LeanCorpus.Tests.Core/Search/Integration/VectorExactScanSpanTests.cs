using System.Globalization;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Fixtures;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Exact vector scans borrow stored vectors under a query pin instead of copying each one.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class VectorExactScanSpanTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private const string Field = "embedding";

    // Covers dimensions below, at and either side of the 4, 8 and 16 float lane widths.
    [Theory(DisplayName = "Exact Scan Spans: Borrowed Vectors Score And Normalise Like Arrays")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(129)]
    public void BorrowedVectors_ScoreAndNormaliseLikeArrays(int dimension)
    {
        var random = new Random(9_100 + dimension);
        float[][] vectors = Enumerable.Range(0, 40).Select(_ => RandomVector(random, dimension)).ToArray();
        string path = SubDir($"{nameof(BorrowedVectors_ScoreAndNormaliseLikeArrays)}_{dimension}");
        BuildIndex(path, vectors, groups: null);
        float[] query = RandomVector(random, dimension);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        int compared = 0;
        foreach (SegmentReader reader in searcher.GetSegmentReaders())
        {
            float[] scratch = new float[dimension];
            using var pin = reader.AcquireQueryLease();
            for (int docId = 0; docId < reader.MaxDoc; docId++)
            {
                float[] copied = reader.GetVector(Field, docId)!;
                ReadOnlySpan<float> borrowed = reader.BorrowPinnedVector(Field, docId, scratch);

                Assert.True(borrowed.SequenceEqual(copied));
                Assert.Equal(
                    BitConverter.SingleToInt32Bits(SimdVectorOps.CosineSimilarity(query, copied)),
                    BitConverter.SingleToInt32Bits(SimdVectorOps.CosineSimilarity(query, borrowed)));
                Assert.Equal(
                    BitConverter.SingleToInt32Bits(VectorQuery.CosineSimilarity(query, copied)),
                    BitConverter.SingleToInt32Bits(VectorQuery.CosineSimilarity(query, borrowed)));

                float[] normalisedCopy = (float[])copied.Clone();
                float[] normalisedBorrow = borrowed.ToArray();
                Assert.Equal(SimdVectorOps.NormaliseInPlace(normalisedCopy), SimdVectorOps.NormaliseInPlace(normalisedBorrow));
                Assert.Equal(normalisedCopy, normalisedBorrow);
                compared++;
            }
        }

        Assert.Equal(vectors.Length, compared);
    }

    [Fact(DisplayName = "Exact Scan Spans: Borrowing Requires An Active Query Pin")]
    public void BorrowedVector_RequiresActiveQueryPin()
    {
        string path = SubDir(nameof(BorrowedVector_RequiresActiveQueryPin));
        BuildIndex(path, [[1f, 2f, 3f, 4f]], groups: null);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        SegmentReader reader = Assert.Single(searcher.GetSegmentReaders());
        float[] scratch = new float[4];

        Assert.Throws<InvalidOperationException>(() => reader.BorrowPinnedVector(Field, 0, scratch).ToArray());
        using (reader.AcquireQueryLease())
            Assert.Equal([1f, 2f, 3f, 4f], reader.BorrowPinnedVector(Field, 0, scratch).ToArray());

        // Once the pin is released the state may be evicted and unmapped, so a borrow is refused.
        Assert.Throws<InvalidOperationException>(() => reader.BorrowPinnedVector(Field, 0, scratch).ToArray());
    }

    [Fact(DisplayName = "Exact Scan Spans: Filtered Search Returns Only Eligible Documents With Exact Scores")]
    public void FilteredExactScan_ReturnsOnlyEligibleDocumentsWithExactScores()
    {
        var random = new Random(4_242);
        float[][] vectors = Enumerable.Range(0, 300).Select(_ => RandomVector(random, 24)).ToArray();
        string[] groups = vectors.Select((_, index) => index % 7 == 0 ? "eligible" : "other").ToArray();
        string path = SubDir(nameof(FilteredExactScan_ReturnsOnlyEligibleDocumentsWithExactScores));
        BuildIndex(path, vectors, groups);
        float[] query = RandomVector(random, 24);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var results = searcher.Search(
            new VectorQuery(Field, query, topK: vectors.Length, filter: new TermQuery("group", "eligible")),
            vectors.Length);

        int[] eligible = Enumerable.Range(0, vectors.Length).Where(index => groups[index] == "eligible").ToArray();
        Assert.Equal(eligible.Length, results.TotalHits);
        Assert.Equal(eligible.Length, results.ScoreDocs.Length);
        AssertExactRanking(results.ScoreDocs, eligible, vectors, query);
    }

    [Fact(DisplayName = "Exact Scan Spans: Deleted Documents Are Excluded From Filtered And Unfiltered Scans")]
    public void ExactScan_ExcludesDeletedDocuments()
    {
        var random = new Random(5_151);
        float[][] vectors = Enumerable.Range(0, 120).Select(_ => RandomVector(random, 16)).ToArray();
        string[] groups = vectors.Select((_, index) => index % 2 == 0 ? "eligible" : "other").ToArray();
        string path = SubDir(nameof(ExactScan_ExcludesDeletedDocuments));
        BuildIndex(path, vectors, groups);
        int[] deleted = Enumerable.Range(0, vectors.Length).Where(static index => index % 3 == 0).ToArray();
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, WriterConfig()))
        {
            foreach (int index in deleted)
                writer.DeleteDocuments(new TermQuery("id", index.ToString(CultureInfo.InvariantCulture)));
            writer.Commit();
        }
        float[] query = RandomVector(random, 16);

        using var searchDirectory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(searchDirectory, ExactSearcherConfig());
        var unfiltered = searcher.Search(new VectorQuery(Field, query, topK: vectors.Length), vectors.Length);
        var filtered = searcher.Search(
            new VectorQuery(Field, query, topK: vectors.Length, filter: new TermQuery("group", "eligible")),
            vectors.Length);

        int[] live = Enumerable.Range(0, vectors.Length).Except(deleted).ToArray();
        AssertExactRanking(unfiltered.ScoreDocs, live, vectors, query);
        AssertExactRanking(filtered.ScoreDocs, live.Where(index => groups[index] == "eligible").ToArray(), vectors, query);
    }

    [Fact(DisplayName = "Exact Scan Spans: Historical Int8 Vectors Decode Exactly As Before")]
    public void HistoricalInt8_BorrowedVectorsMatchDecodedArrays()
    {
        var random = new Random(8_008);
        float[][] vectors = Enumerable.Range(0, 48).Select(_ => RandomVector(random, 32)).ToArray();
        string path = SubDir(nameof(HistoricalInt8_BorrowedVectorsMatchDecodedArrays));
        BuildIndex(path, vectors, groups: null);
        HistoricalInt8VectorFixtures.ReplaceFloat32SegmentsWithV3Tag1(path, Field);
        float[] query = RandomVector(random, 32);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var decoded = new float[vectors.Length][];
        foreach (SegmentReader reader in searcher.GetSegmentReaders())
        {
            float[] scratch = new float[32];
            using var pin = reader.AcquireQueryLease();
            for (int docId = 0; docId < reader.MaxDoc; docId++)
            {
                float[] copied = reader.GetVector(Field, docId)!;
                Assert.True(reader.BorrowPinnedVector(Field, docId, scratch).SequenceEqual(copied));
                decoded[reader.DocBase + docId] = copied;
            }
        }

        // Historical Int8 decodes to approximate values, so the exact ranking uses decoded arrays.
        Assert.Contains(Enumerable.Range(0, vectors.Length), index => !decoded[index].SequenceEqual(vectors[index]));
        var results = searcher.Search(new VectorQuery(Field, query, topK: vectors.Length), vectors.Length);
        AssertExactRanking(results.ScoreDocs, Enumerable.Range(0, vectors.Length).ToArray(), decoded, query);
    }

    [Fact(DisplayName = "Exact Scan Spans: Flat Scan Does Not Allocate A Copy Per Vector")]
    public void FlatScan_DoesNotAllocatePerVector()
    {
        const int documentCount = 4_000;
        const int dimension = 256;
        var random = new Random(6_464);
        float[][] vectors = Enumerable.Range(0, documentCount).Select(_ => RandomVector(random, dimension)).ToArray();
        string[] groups = vectors.Select((_, index) => index % 2 == 0 ? "eligible" : "other").ToArray();
        string path = SubDir(nameof(FlatScan_DoesNotAllocatePerVector));
        BuildIndex(path, vectors, groups);
        float[] queryVector = RandomVector(random, dimension);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());

        // Copying would allocate at least 1 kB per scanned vector: 4 MB unfiltered and 2 MB for
        // the eligible half. The filtered scan still materialises its filter (about 100 kB here),
        // which this bound leaves room for, but one copy per vector cannot fit under it.
        const long perVectorCopy = dimension * sizeof(float);
        long unfilteredBytes = MeasureSearchAllocation(searcher, new VectorQuery(Field, queryVector, topK: 10));
        Assert.True(unfilteredBytes < documentCount * perVectorCopy / 8,
            $"Unfiltered exact scan allocated {unfilteredBytes} bytes.");

        long filteredBytes = MeasureSearchAllocation(searcher,
            new VectorQuery(Field, queryVector, topK: 10, filter: new TermQuery("group", "eligible")));
        Assert.True(filteredBytes < documentCount / 2 * perVectorCopy / 8,
            $"Filtered exact scan allocated {filteredBytes} bytes.");
    }

    private static long MeasureSearchAllocation(IndexSearcher searcher, Query query)
    {
        for (int warmup = 0; warmup < 3; warmup++)
            Assert.NotEmpty(searcher.Search(query, 10).ScoreDocs);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = searcher.Search(query, 10);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotEmpty(result.ScoreDocs);
        return allocated;
    }

    private static void AssertExactRanking(
        Rowles.LeanCorpus.Search.Scoring.ScoreDoc[] actual,
        int[] eligible,
        float[][] vectors,
        float[] query)
    {
        var expected = eligible
            .Select(index => (DocId: index, Score: VectorQuery.CosineSimilarity(query, vectors[index])))
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.DocId)
            .ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (int rank = 0; rank < expected.Length; rank++)
        {
            Assert.Equal(expected[rank].DocId, actual[rank].DocId);
            Assert.Equal(BitConverter.SingleToInt32Bits(expected[rank].Score), BitConverter.SingleToInt32Bits(actual[rank].Score));
        }
    }

    private void BuildIndex(string path, float[][] vectors, string[]? groups)
    {
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, WriterConfig());
        for (int index = 0; index < vectors.Length; index++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", index.ToString(CultureInfo.InvariantCulture)));
            if (groups is not null)
                document.Add(new StringField("group", groups[index]));
            document.Add(new VectorField(Field, vectors[index]));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    private static IndexWriterConfig WriterConfig() => new()
    {
        NormaliseVectors = false,
        VectorQuantisation = VectorQuantisation.None,
        UseCompoundFile = false,
        DurableCommits = false,
        MaxBufferedDocs = 10_000,
        MaxQueuedDocs = 20_000,
    };

    private static IndexSearcherConfig ExactSearcherConfig() => new()
    {
        ParallelSearch = false,
        EnableQueryCache = false,
    };

    private static float[] RandomVector(Random random, int dimension)
    {
        var vector = new float[dimension];
        for (int index = 0; index < dimension; index++)
            vector[index] = random.NextSingle() * 2f - 1f;
        return vector;
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
