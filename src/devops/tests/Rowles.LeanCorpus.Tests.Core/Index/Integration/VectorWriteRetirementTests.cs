using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Tests.Core.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
public sealed class VectorWriteRetirementTests
{
    [Fact]
    public void DefaultWriterBuildsExactOnlyVectorSegments()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-exact-only-{Guid.NewGuid():N}");
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
            {
                writer.AddDocument(Document("a", [1f, 0f]));
                writer.AddDocument(Document("b", [0f, 1f]));
                writer.Commit();
            }

            using var searcher = new IndexSearcher(directory);
            var reader = Assert.Single(searcher.GetSegmentReaders());
            Assert.Null(reader.GetHnswGraph("embedding"));
            var result = searcher.Search(new VectorQuery("embedding", [1f, 0f], topK: 2), 2);
            Assert.Equal(new[] { 0, 1 }, result.ScoreDocs.Select(static hit => hit.DocId));
            Assert.Equal(1f, result.ScoreDocs[0].Score);
            Assert.Equal(0f, result.ScoreDocs[1].Score);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact]
    public void ExistingFloat32GraphIsIgnoredAndReportedAsIntentionalExactSearch()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-retired-graph-read-{Guid.NewGuid():N}");
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
            {
                writer.AddDocument(Document("a", [1f, 0f]));
                writer.AddDocument(Document("b", [0f, 1f]));
                writer.Commit();
            }

            SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos);
            AddHistoricalFloat32Graph(path, segment);

            var metrics = new DefaultMetricsCollector();
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
            {
                EnableQueryCache = false,
                Metrics = metrics,
            });
            var result = searcher.Search(new VectorQuery("embedding", [1f, 0f], topK: 2), 2);
            Assert.Equal(new[] { 0, 1 }, result.ScoreDocs.Select(static hit => hit.DocId));
            Assert.Contains("exact Float32", searcher.Explain(new VectorQuery("embedding", [1f, 0f]), 0)!.Description,
                StringComparison.OrdinalIgnoreCase);

            MetricsSnapshot snapshot = metrics.GetSnapshot();
            Assert.Equal(1, ReadMetric(snapshot, "VectorExactScanCount"));
            Assert.Equal(2, ReadMetric(snapshot, "VectorExactVectorsScanned"));
            Assert.Equal(1, ReadMetric(snapshot, "VectorRetiredHnswExactScanCount"));
            Assert.Equal(0, snapshot.HnswSearchCount);
            Assert.Equal(0, snapshot.HnswExhaustiveFallbackCount);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact]
    public void ExistingInt8GraphIsIgnoredAndReportedAsExactDecodedSearch()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-retired-int8-graph-read-{Guid.NewGuid():N}");
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
            {
                writer.AddDocument(Document("a", [1f, 0f]));
                writer.AddDocument(Document("b", [0f, 1f]));
                writer.Commit();
            }

            SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos);
            AddHistoricalFloat32Graph(path, segment);
            AddHistoricalInt8Vectors(path, segment);

            var metrics = new DefaultMetricsCollector();
            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
            {
                EnableQueryCache = false,
                Metrics = metrics,
            });
            var query = new VectorQuery("embedding", [1f, 0f], topK: 2);
            var result = searcher.Search(query, 2);
            Assert.Equal(new[] { 0, 1 }, result.ScoreDocs.Select(static hit => hit.DocId));
            Assert.Equal(new[] { 1f, 0f }, result.ScoreDocs.Select(static hit => hit.Score));
            Assert.Contains("exact decoded Int8", searcher.Explain(query, 0)!.Description,
                StringComparison.OrdinalIgnoreCase);

            MetricsSnapshot snapshot = metrics.GetSnapshot();
            Assert.Equal(1, ReadMetric(snapshot, "VectorExactScanCount"));
            Assert.Equal(2, ReadMetric(snapshot, "VectorExactVectorsScanned"));
            Assert.Equal(1, ReadMetric(snapshot, "VectorRetiredHnswExactScanCount"));
            Assert.Equal(0, snapshot.HnswSearchCount);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact]
    public void MergeDoesNotRecreateRetiredGraphsFromHistoricalSegments()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-retired-graph-merge-{Guid.NewGuid():N}");
        try
        {
            using var directory = new MMapDirectory(path);
            using (var writer = new IndexWriter(directory, new IndexWriterConfig { UseCompoundFile = false }))
            {
                writer.AddDocument(Document("a", [1f, 0f]));
                writer.Commit();
                writer.AddDocument(Document("b", [0f, 1f]));
                writer.Commit();
            }

            SegmentInfo[] segments = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos.ToArray();
            Assert.Equal(2, segments.Length);
            foreach (SegmentInfo segment in segments)
                AddHistoricalFloat32Graph(path, segment);

            using (var writer = new IndexWriter(directory, new IndexWriterConfig { UseCompoundFile = false }))
            {
                writer.ForceMerge(1);
                writer.Commit();
            }

            using var searcher = new IndexSearcher(directory);
            var merged = Assert.Single(searcher.GetSegmentReaders());
            Assert.False(merged.Info.VectorFields.Single().HasHnsw);
            Assert.Null(merged.GetHnswGraph("embedding"));
            var result = searcher.Search(new VectorQuery("embedding", [1f, 0f], topK: 2), 2);
            Assert.Equal(new[] { 0, 1 }, result.ScoreDocs.Select(static hit => hit.DocId));
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact]
    public void ExplicitHnswCreationIsRejectedBeforeIndexSideEffects()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-hnsw-retired-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            var exception = Assert.Throws<ArgumentException>(() =>
                new IndexWriter(directory, new IndexWriterConfig { BuildHnswOnFlush = true }));

            Assert.Contains("HnswGraphCreationRetired", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(path));
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact]
    public void Int8WriterConfigurationIsRejectedBeforeIndexSideEffects()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-int8-retired-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            var exception = Assert.Throws<ArgumentException>(() =>
                new IndexWriter(directory, new IndexWriterConfig { VectorQuantisation = VectorQuantisation.Int8 }));

            Assert.Contains("Int8WriteRetired", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(path));
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }

    [Fact]
    public void DirectInt8WriterIsRejectedBeforeCreatingOutput()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vector-int8-direct-{Guid.NewGuid():N}.vq");
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                QuantisedVectorWriter.WriteInt8(path, 1, 2,
                    new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[] { 1f, 0f } }));

            Assert.Contains("Int8WriteRetired", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static LeanDocument Document(string id, float[] vector)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new VectorField("embedding", vector));
        return document;
    }

    private static long ReadMetric(MetricsSnapshot snapshot, string name)
    {
        var property = snapshot.GetType().GetProperty(name);
        Assert.NotNull(property);
        return Assert.IsType<long>(property!.GetValue(snapshot));
    }

    private static void AddHistoricalFloat32Graph(string directoryPath, SegmentInfo segment)
    {
        string basePath = Path.Combine(directoryPath, segment.SegmentId);
        string graphPath = VectorFilePaths.HnswFile(basePath, "embedding");
        using (var vectorReader = VectorReader.Open(VectorFilePaths.VectorFile(basePath, "embedding")))
        using (var graph = HnswGraphBuilder.Build(
                   new VectorReaderSource(vectorReader),
                   Enumerable.Range(0, vectorReader.VectorCount).Where(vectorReader.HasVector).ToArray(),
                   new HnswBuildConfig { M = 2, EfConstruction = 4 }, seed: 7))
        {
            HnswWriter.Write(graphPath, graph, dimension: 2, normalised: true);
        }

        VectorFieldInfo field = segment.VectorFields.Single();
        segment.VectorFields[0] = new VectorFieldInfo
        {
            FieldName = field.FieldName,
            Dimension = field.Dimension,
            Normalised = field.Normalised,
            Quantisation = field.Quantisation,
            HasHnsw = true,
        };
        segment.WriteTo(Path.Combine(directoryPath, segment.SegmentId + ".seg"));
    }

    private static void AddHistoricalInt8Vectors(string directoryPath, SegmentInfo segment)
    {
        string basePath = Path.Combine(directoryPath, segment.SegmentId);
        string quantisedPath = VectorFilePaths.QuantisedVectorFile(basePath, "embedding");
        HistoricalInt8VectorFixtures.WriteV3Tag1(quantisedPath, segment.DocCount, 2,
            new Dictionary<int, ReadOnlyMemory<float>>
            {
                [0] = new float[] { 1f, 0f },
                [1] = new float[] { 0f, 1f },
            });

        File.Delete(VectorFilePaths.VectorFile(basePath, "embedding"));
        VectorFieldInfo field = segment.VectorFields.Single();
        segment.VectorFields[0] = new VectorFieldInfo
        {
            FieldName = field.FieldName,
            Dimension = field.Dimension,
            Normalised = field.Normalised,
            Quantisation = VectorQuantisation.Int8,
            HasHnsw = field.HasHnsw,
        };
        segment.WriteTo(Path.Combine(directoryPath, segment.SegmentId + ".seg"));
    }
}
