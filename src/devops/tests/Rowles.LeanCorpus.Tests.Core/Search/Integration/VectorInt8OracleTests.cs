using System.Globalization;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class VectorInt8OracleTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private const double ScoreTolerance = 1e-5;
    private const double TieTolerance = 1e-6;
    private const int DimensionCaseDocumentCount = 24;
    private const int SegmentedDimension = 32;
    private const int DocumentsPerSegment = 8;

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(384)]
    [InlineData(768)]
    public void ExactInt8_MatchesIndependentStoredCodeOracleAcrossDimensions(int dimension)
    {
        SourceDocument[] documents = BuildDimensionCorpus(dimension);
        string path = SubDir($"{nameof(ExactInt8_MatchesIndependentStoredCodeOracleAcrossDimensions)}_{dimension}");
        BuildIndex(path, documents, buildHnsw: false, useCompoundFile: false, segmentSize: documents.Length);

        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var segments = DecodeInt8Segments(directory, searcher);

        Assert.Single(segments);
        AssertEncodedBodyMatchesSource(searcher, segments, documents);
        Assert.Contains(segments[0].Codes, static code => code >= 128);

        float[] query = RandomVector(new Random(77_000 + dimension), dimension, magnitude: 1.5f);
        AssertDecodedOracle(searcher, segments, query, topK: 10, efSearch: 0);
    }

    [Fact]
    public void ExactAndHnswInt8_MatchIndependentOracleAcrossPerSegmentScales()
    {
        SourceDocument[] documents = BuildPerSegmentScaleCorpus();
        string exactPath = SubDir(nameof(ExactAndHnswInt8_MatchIndependentOracleAcrossPerSegmentScales) + "_exact");
        string hnswPath = SubDir(nameof(ExactAndHnswInt8_MatchIndependentOracleAcrossPerSegmentScales) + "_hnsw");
        BuildIndex(exactPath, documents, buildHnsw: false, useCompoundFile: false, segmentSize: DocumentsPerSegment);
        BuildIndex(hnswPath, documents, buildHnsw: true, useCompoundFile: true, segmentSize: DocumentsPerSegment);

        using var exactDirectory = new MMapDirectory(exactPath);
        using var exactSearcher = new IndexSearcher(exactDirectory, ExactSearcherConfig());
        var exactSegments = DecodeInt8Segments(exactDirectory, exactSearcher);
        Assert.Equal(6, exactSegments.Length);
        AssertEncodedBodyMatchesSource(exactSearcher, exactSegments, documents);
        Assert.True(exactSegments.Select(static segment => segment.Min).Distinct().Count() >= 5);
        Assert.True(exactSegments.Select(static segment => segment.Alpha).Distinct().Count() >= 4);

        var metrics = new DefaultMetricsCollector();
        using var hnswDirectory = new MMapDirectory(hnswPath);
        using var hnswSearcher = new IndexSearcher(hnswDirectory, new IndexSearcherConfig
        {
            ParallelSearch = false,
            EnableQueryCache = false,
            Metrics = metrics,
        });
        var hnswSegments = DecodeInt8Segments(hnswDirectory, hnswSearcher);
        Assert.Equal(6, hnswSegments.Length);
        AssertEncodedBodyMatchesSource(hnswSearcher, hnswSegments, documents);

        float[] query = RandomVector(new Random(18_492), SegmentedDimension, magnitude: 1.25f);
        AssertDecodedOracle(exactSearcher, exactSegments, query, topK: 10, efSearch: 0);
        AssertDecodedOracle(hnswSearcher, hnswSegments, query, topK: 10, efSearch: 512);

        MetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(6, snapshot.HnswSearchCount);
        Assert.True(snapshot.HnswNodesVisited > 0);
        Assert.Equal(0, snapshot.HnswVectorsScanned);
        Assert.Equal(0, snapshot.HnswExhaustiveFallbackCount);
    }

    private SourceDocument[] BuildDimensionCorpus(int dimension)
    {
        var random = new Random(84_000 + dimension);
        float[] duplicate = RandomVector(random, dimension, magnitude: 2f);
        var documents = new SourceDocument[DimensionCaseDocumentCount];
        for (int ordinal = 0; ordinal < documents.Length; ordinal++)
        {
            float[]? vector = ordinal switch
            {
                0 => FilledVector(dimension, -3f),
                1 => FilledVector(dimension, 3f),
                2 => new float[dimension],
                5 or 6 => (float[])duplicate.Clone(),
                9 => null,
                _ => RandomVector(random, dimension, magnitude: 2f),
            };
            documents[ordinal] = new SourceDocument(ordinal, vector);
        }
        return documents;
    }

    private SourceDocument[] BuildPerSegmentScaleCorpus()
    {
        var documents = new SourceDocument[6 * DocumentsPerSegment];
        var random = new Random(102_933);
        for (int segment = 0; segment < 6; segment++)
        {
            float offset = -2.5f + segment * 0.9f;
            float scale = 0.35f + segment * 0.27f;
            for (int local = 0; local < DocumentsPerSegment; local++)
            {
                int ordinal = segment * DocumentsPerSegment + local;
                float[]? vector = local == 1
                    ? null
                    : segment switch
                {
                    0 => FilledVector(SegmentedDimension, 2.5f),
                    1 => new float[SegmentedDimension],
                    _ => RandomVector(random, SegmentedDimension, magnitude: 1f)
                        .Select(value => offset + value * scale).ToArray(),
                };
                documents[ordinal] = new SourceDocument(ordinal, vector);
            }

            if (segment is 2 or 3 or 4 or 5)
                documents[segment * DocumentsPerSegment + 4] = documents[segment * DocumentsPerSegment + 3] with
                {
                    SourceOrdinal = segment * DocumentsPerSegment + 4,
                    Vector = (float[])documents[segment * DocumentsPerSegment + 3].Vector!.Clone(),
                };
        }
        return documents;
    }

    private void BuildIndex(string path, IReadOnlyList<SourceDocument> documents, bool buildHnsw, bool useCompoundFile, int segmentSize)
    {
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = buildHnsw,
            NormaliseVectors = false,
            VectorQuantisation = VectorQuantisation.Int8,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 611_204L,
            UseCompoundFile = useCompoundFile,
            DurableCommits = false,
            MaxBufferedDocs = 10_000,
            MaxQueuedDocs = 20_000,
            MergeThreshold = 1_000,
            MergeThrottleSegments = 1_000,
        });

        foreach ((SourceDocument source, int index) in documents.Select(static (document, index) => (document, index)))
        {
            var document = new LeanDocument();
            document.Add(new StoredField("sourceOrdinal", source.SourceOrdinal));
            if (source.Vector is not null)
                document.Add(new VectorField("embedding", source.Vector));
            writer.AddDocument(document);

            if ((index + 1) % segmentSize == 0)
                writer.Commit();
        }

        if (documents.Count % segmentSize != 0)
            writer.Commit();
    }

    private static ParsedInt8Segment[] DecodeInt8Segments(MMapDirectory directory, IndexSearcher searcher)
        => searcher.GetSegmentReaders()
            .Select(reader => DecodeInt8Segment(directory, reader))
            .OrderBy(static segment => segment.DocBase)
            .ToArray();

    private static ParsedInt8Segment DecodeInt8Segment(MMapDirectory directory, SegmentReader reader)
    {
        string fileName = VectorFilePaths.QuantisedVectorFile(reader.Info.SegmentId, "embedding");
        using ISegmentFileSource files = reader.Info.IsCompoundFile
            ? new CompoundSegmentFileSource(directory, reader.Info.SegmentId)
            : new LooseSegmentFileSource(directory, reader.Info.SegmentId);
        using var input = files.OpenInput(fileName);
        using var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Quantised);
        Assert.Equal(3, frame.FormatVersion);
        frame.ValidateChecksum();

        using var body = frame.OpenBodyInput();
        int documentCount = body.ReadInt32();
        int dimension = body.ReadInt32();
        Assert.Equal(reader.MaxDoc, documentCount);
        Assert.True(dimension > 0);
        Assert.Equal(VectorQuantisation.Int8, (VectorQuantisation)body.ReadByte());

        byte[] presenceBytes = body.ReadBytes(checked((documentCount + 7) / 8));
        var present = new bool[documentCount];
        for (int docId = 0; docId < documentCount; docId++)
            present[docId] = (presenceBytes[docId >> 3] & (1 << (docId & 7))) != 0;

        Assert.Equal((byte)QuantisedVectorStorageKind.LegacyInt8, body.ReadByte());
        float min = body.ReadSingle();
        float alpha = body.ReadSingle();
        Assert.True(float.IsFinite(min));
        Assert.True(float.IsFinite(alpha) && alpha > 0f);

        var corrections = new float[documentCount];
        for (int docId = 0; docId < documentCount; docId++)
        {
            corrections[docId] = body.ReadSingle();
            Assert.True(float.IsFinite(corrections[docId]), $"Int8 correction for local document {docId} is not finite.");
        }

        byte[] codes = body.ReadBytes(checked(documentCount * dimension));
        Assert.Equal(body.Length, body.Position);

        return new ParsedInt8Segment(reader.DocBase, documentCount, dimension, min, alpha, present, corrections, codes,
            Enumerable.Range(0, documentCount).Select(reader.IsLive).ToArray());
    }

    private static void AssertEncodedBodyMatchesSource(
        IndexSearcher searcher,
        IReadOnlyList<ParsedInt8Segment> segments,
        IReadOnlyList<SourceDocument> sourceDocuments)
    {
        var sourceByOrdinal = sourceDocuments.ToDictionary(static document => document.SourceOrdinal);
        foreach (ParsedInt8Segment segment in segments)
        {
            float expectedMaximumMagnitude = 0f;
            for (int localDocId = 0; localDocId < segment.DocumentCount; localDocId++)
            {
                SourceDocument document = sourceByOrdinal[SourceOrdinal(searcher, segment.DocBase + localDocId)];
                if (document.Vector is null)
                    continue;
                foreach (float value in document.Vector!)
                    expectedMaximumMagnitude = MathF.Max(expectedMaximumMagnitude, MathF.Abs(value));
            }

            if (expectedMaximumMagnitude == 0f)
                expectedMaximumMagnitude = 1f;
            float expectedAlpha = expectedMaximumMagnitude / 127f;
            float expectedMin = -128f * expectedAlpha;
            Assert.Equal(expectedMin, segment.Min);
            Assert.Equal(expectedAlpha, segment.Alpha);

            for (int localDocId = 0; localDocId < segment.DocumentCount; localDocId++)
            {
                int sourceOrdinal = SourceOrdinal(searcher, segment.DocBase + localDocId);
                SourceDocument source = sourceByOrdinal[sourceOrdinal];
                Assert.Equal(source.Vector is not null, segment.Present[localDocId]);

                ReadOnlySpan<float> original = source.Vector ?? new float[segment.Dimension];
                float correctionFromCodes = 0f;
                for (int dimension = 0; dimension < segment.Dimension; dimension++)
                {
                    byte code = segment.Codes[localDocId * segment.Dimension + dimension];
                    byte expectedCode = (byte)Math.Clamp((original[dimension] - segment.Min) / segment.Alpha + 0.5f, 0f, 255f);
                    Assert.Equal(expectedCode, code);

                    float reconstructed = segment.Min + segment.Alpha * code;
                    correctionFromCodes += segment.Alpha * code * (original[dimension] - reconstructed);
                }

                Assert.True(MathF.Abs(correctionFromCodes - segment.Corrections[localDocId]) <= 1e-4f,
                    $"Correction mismatch for source document {sourceOrdinal}: stored={segment.Corrections[localDocId]:R}, decoded-code oracle={correctionFromCodes:R}.");
            }
        }
    }

    private static void AssertDecodedOracle(
        IndexSearcher searcher,
        IReadOnlyList<ParsedInt8Segment> segments,
        float[] queryVector,
        int topK,
        int efSearch)
    {
        var expected = new List<ExpectedScore>();
        foreach (ParsedInt8Segment segment in segments)
        {
            for (int localDocId = 0; localDocId < segment.DocumentCount; localDocId++)
            {
                if (!segment.Present[localDocId] || !segment.IsLive[localDocId])
                    continue;
                float[] decoded = segment.DecodeVector(localDocId);
                int sourceOrdinal = SourceOrdinal(searcher, segment.DocBase + localDocId);
                expected.Add(new ExpectedScore(sourceOrdinal, ScalarCosine(queryVector, decoded)));
            }
        }

        VectorQuery vectorQuery = new("embedding", queryVector, topK, efSearch);
        TopDocs actual = searcher.Search(vectorQuery, topK, TestContext.Current.CancellationToken);
        ExpectedScore[] rankedExpected = expected
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.SourceOrdinal)
            .ToArray();
        int expectedHits = Math.Min(topK, expected.Count);
        Assert.Equal(expected.Count, actual.TotalHits);
        Assert.Equal(expectedHits, actual.ScoreDocs.Length);

        int[] actualOrdinals = actual.ScoreDocs.Select(hit => SourceOrdinal(searcher, hit.DocId)).ToArray();
        Assert.Equal(actualOrdinals.Length, actualOrdinals.Distinct().Count());
        int[] requiredHigher = rankedExpected.Take(expectedHits)
            .Where(item => item.Score > rankedExpected[expectedHits - 1].Score + TieTolerance)
            .Select(static item => item.SourceOrdinal)
            .ToArray();
        HashSet<int> boundaryTies = rankedExpected
            .Where(item => Math.Abs(item.Score - rankedExpected[expectedHits - 1].Score) <= TieTolerance)
            .Select(static item => item.SourceOrdinal)
            .ToHashSet();
        int requiredTieCount = expectedHits - requiredHigher.Length;
        Assert.All(requiredHigher, ordinal => Assert.Contains(ordinal, actualOrdinals));
        Assert.All(actualOrdinals, ordinal => Assert.Contains(ordinal, requiredHigher.Concat(boundaryTies)));
        Assert.True(actualOrdinals.Count(boundaryTies.Contains) >= requiredTieCount,
            "The Int8 search omitted too many documents tied at the independent decoded-score boundary.");

        var scoresByOrdinal = expected.ToDictionary(static item => item.SourceOrdinal, static item => item.Score);
        foreach (ScoreDoc hit in actual.ScoreDocs)
        {
            int sourceOrdinal = SourceOrdinal(searcher, hit.DocId);
            Assert.True(float.IsFinite(hit.Score));
            Assert.True(Math.Abs(hit.Score - scoresByOrdinal[sourceOrdinal]) <= ScoreTolerance,
                $"Int8 score mismatch for source document {sourceOrdinal}: production={hit.Score:R}, decoded oracle={scoresByOrdinal[sourceOrdinal]:R}.");
            Explanation? explanation = searcher.Explain(vectorQuery, hit.DocId);
            Assert.NotNull(explanation);
            Assert.True(Math.Abs(explanation.Score - scoresByOrdinal[sourceOrdinal]) <= ScoreTolerance,
                $"Int8 explanation mismatch for source document {sourceOrdinal}: production={explanation.Score:R}, decoded oracle={scoresByOrdinal[sourceOrdinal]:R}.");
        }
    }

    private static double ScalarCosine(ReadOnlySpan<float> query, ReadOnlySpan<float> document)
    {
        double dot = 0d;
        double queryNormSquared = 0d;
        double documentNormSquared = 0d;
        for (int dimension = 0; dimension < query.Length; dimension++)
        {
            double queryValue = query[dimension];
            double documentValue = document[dimension];
            dot += queryValue * documentValue;
            queryNormSquared += queryValue * queryValue;
            documentNormSquared += documentValue * documentValue;
        }

        if (queryNormSquared == 0d || documentNormSquared == 0d)
            return 0d;
        return dot / Math.Sqrt(queryNormSquared * documentNormSquared);
    }

    private static int SourceOrdinal(IndexSearcher searcher, int globalDocId)
        => int.Parse(searcher.GetStoredFields(globalDocId)["sourceOrdinal"].Single(), CultureInfo.InvariantCulture);

    private static IndexSearcherConfig ExactSearcherConfig()
        => new() { ParallelSearch = false, EnableQueryCache = false };

    private static float[] FilledVector(int dimension, float value)
    {
        var vector = new float[dimension];
        Array.Fill(vector, value);
        return vector;
    }

    private static float[] RandomVector(Random random, int dimension, float magnitude)
    {
        var vector = new float[dimension];
        for (int index = 0; index < dimension; index++)
            vector[index] = (float)(random.NextDouble() * 2d - 1d) * magnitude;
        return vector;
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record SourceDocument(int SourceOrdinal, float[]? Vector);

    private sealed record ParsedInt8Segment(
        int DocBase,
        int DocumentCount,
        int Dimension,
        float Min,
        float Alpha,
        bool[] Present,
        float[] Corrections,
        byte[] Codes,
        bool[] IsLive)
    {
        public float[] DecodeVector(int localDocId)
        {
            var vector = new float[Dimension];
            for (int dimension = 0; dimension < Dimension; dimension++)
                vector[dimension] = Min + Alpha * Codes[localDocId * Dimension + dimension];
            return vector;
        }
    }

    private readonly record struct ExpectedScore(int SourceOrdinal, double Score);
}
