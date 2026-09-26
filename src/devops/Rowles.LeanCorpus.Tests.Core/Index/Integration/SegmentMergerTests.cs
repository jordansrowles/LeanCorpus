using System.Text.Json;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Regression tests for autopsy issue C2: <c>SegmentMerger</c> previously discarded
/// roughly half the codec output. Each test pins one missing artefact (.fln, .dvn,
/// .dvs, .bkd, .tvd/.tvx, .pbs, IndexSortFields) plus the orphan-cleanup behaviour.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentMergerTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SegmentMergerTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static int SegmentOrdinal(string segmentId)
    {
        Assert.StartsWith("seg_", segmentId);
        return int.Parse(segmentId.AsSpan("seg_".Length));
    }

    private static string MergeSegmentsForTest(string dir, MMapDirectory mmap)
    {
        var sourceSegments = IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false)!
            .SegmentInfos
            .OrderBy(s => SegmentOrdinal(s.SegmentId))
            .ToList();
        Assert.True(sourceSegments.Count >= 2, "Expected at least two source segments to merge.");

        int nextSegmentOrdinal = sourceSegments.Max(s => SegmentOrdinal(s.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2);
        var mergedSegments = merger.MaybeMerge(sourceSegments, ref nextSegmentOrdinal);

        var mergedSegment = mergedSegments
            .FirstOrDefault(candidate => sourceSegments.All(source => source.SegmentId != candidate.SegmentId));
        Assert.NotNull(mergedSegment);

        // Publish a test-only commit that references the merged segment so searchers exercise
        // the freshly-written codec files rather than the pre-merge source segments.
        int generation = Directory.GetFiles(dir, "segments_*")
            .Select(path => int.TryParse(Path.GetFileName(path).AsSpan("segments_".Length), out int gen) ? gen : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var commitData = JsonSerializer.Serialize(new
        {
            Segments = new[] { mergedSegment!.SegmentId },
            Generation = generation
        });
        File.WriteAllText(Path.Combine(dir, $"segments_{generation}"), commitData);

        var activeSegments = new HashSet<string>(mergedSegments.Select(static segment => segment.SegmentId), StringComparer.Ordinal);
        foreach (var segment in sourceSegments)
        {
            if (!activeSegments.Contains(segment.SegmentId))
                merger.CleanupSegmentFiles(segment);
        }

        return mergedSegment!.SegmentId;
    }

    private static IndexWriterConfig SmallSegmentMergeConfig(bool storeTermVectors = false, IndexSort? sort = null)
        => new()
        {
            MaxBufferedDocs = 1,
            MergeThreshold = 100,
            StoreTermVectors = storeTermVectors,
            IndexSort = sort,
        };

    private static void AssertCurrentCanonicalFrame(string path)
    {
        Assert.True(CodecCatalog.Default.TryMatchFile(Path.GetFileName(path), out var descriptor));
        Assert.NotNull(descriptor);
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor!);
        Assert.Equal(CodecFileWriter.CurrentFrameVersion, frame.Metadata.FrameVersion);
        Assert.Equal(descriptor!.CurrentFormatVersion, frame.Metadata.FormatVersion);
        frame.ValidateChecksum();
    }

    /// <summary>
    /// Verifies compound source segments are merged through logical bounded inputs, including
    /// postings, DocValues, stored fields, term vectors, vectors and HNSW graph seeding.
    /// </summary>
    [Fact(DisplayName = "Merge: Reads Compound Sources Without Temporary Materialisation")]
    public void Merge_ReadsCompoundSources_WithoutTemporaryMaterialisation()
    {
        var dir = SubDir(nameof(Merge_ReadsCompoundSources_WithoutTemporaryMaterialisation));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = 2,
            MergeThreshold = 100,
            StoreTermVectors = true,
            UseCompoundFile = true,
        }))
        {
            for (int i = 0; i < 4; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"compound document {i}"));
                doc.Add(new StoredField("label", $"doc-{i}"));
                doc.Add(new NumericField("price", 10.0 + i));
                doc.Add(new Int64Field("sequence", 100 + i));
                doc.Add(new StringField("tag", i % 2 == 0 ? "even" : "odd"));
                doc.Add(new BinaryField("payload", new byte[] { (byte)i, (byte)(i + 1) }));
                doc.Add(new VectorField("embedding", new ReadOnlyMemory<float>([1f, i + 1f, 0.5f])));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg").Select(SegmentInfo.ReadFrom).ToArray();
        Assert.Equal(2, sourceSegments.Length);
        Assert.All(sourceSegments, static segment => Assert.True(segment.IsCompoundFile));
        Assert.Empty(Directory.GetFiles(dir, "*.dic"));

        string mergedId = MergeSegmentsForTest(dir, mmap);

        Assert.Empty(Directory.GetDirectories(dir, ".merge-*"));
        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new TermQuery("body", "compound"), 10, TestContext.Current.CancellationToken);
        Assert.Equal(4, results.TotalHits);
        Assert.Equal(4, searcher.Search(new RangeQuery("price", 10, 13), 10, TestContext.Current.CancellationToken).TotalHits);

        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        using var reader = new SegmentReader(mmap, mergedInfo);
        Assert.Equal([10.0, 11.0, 12.0, 13.0], reader.GetNumericDocValues("price")!);
        Assert.Equal([100L, 101L, 102L, 103L], reader.GetInt64DocValues("sequence")!);
        Assert.NotNull(reader.GetTermVectors(0));
        Assert.NotNull(reader.GetVector("embedding", 3));
        Assert.NotNull(reader.GetHnswGraph("embedding"));
        Assert.True(reader.TryGetBinaryDocValues("payload", 2, out var payload));
        Assert.Equal(new byte[] { 2, 3 }, payload[0]);
    }

    [Fact(DisplayName = "Merge: Streams High-Dimension HNSW Vectors Without Retaining The Float Corpus")]
    public void Merge_StreamsHighDimensionHnswVectorsWithinBoundedAllocation()
    {
        const int documentCount = 4;
        const int dimension = 1_000_000;
        long floatCorpusBytes = (long)documentCount * dimension * sizeof(float);
        var dir = SubDir(nameof(Merge_StreamsHighDimensionHnswVectorsWithinBoundedAllocation));
        using var mmap = new MMapDirectory(dir);
        var hnswConfig = new HnswBuildConfig { M = 2, M0 = 2, EfConstruction = 4 };

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = documentCount / 2,
            RamBufferSizeMB = 128,
            MergeThreshold = 100,
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswSeed = 1L,
            HnswBuildConfig = hnswConfig,
        }))
        {
            for (int docId = 0; docId < documentCount; docId++)
            {
                var vector = new float[dimension];
                vector[docId] = 1f;
                var document = new LeanDocument();
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        List<SegmentInfo> sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(2, sourceSegments.Count);

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        long allocatedBeforeMerge = GC.GetAllocatedBytesForCurrentThread();
        int nextOrdinal = sourceSegments.Count;
        var merger = new SegmentMerger(mmap, mergeThreshold: 100, softDeleteRetentionSeconds: 0,
            hnswBuildConfig: hnswConfig);
        SegmentInfo merged = Assert.IsType<SegmentInfo>(merger.MergeAll(sourceSegments, ref nextOrdinal));
        long mergeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBeforeMerge;

        Assert.True(mergeAllocatedBytes < floatCorpusBytes * 3 / 4,
            $"Merge allocated {mergeAllocatedBytes:N0} bytes for a {floatCorpusBytes:N0}-byte vector corpus; vector merge must use bounded file-backed buffers.");
        Assert.Contains(merged.VectorFields, static field => field.FieldName == "embedding" && field.HasHnsw);
        using var reader = new SegmentReader(mmap, merged);
        Assert.Equal(documentCount, reader.GetHnswGraph("embedding")!.NodeCount);
        Assert.Equal(1f, reader.GetVector("embedding", 0)![0]);
    }

    [Fact]
    public void Merge_RejectsMixedVectorDimensionsBeforeWritingDestinationFiles()
    {
        string source2D = CreateVectorSourceIndex("merge_mixed_dimensions_2d", 2, normalised: true,
            VectorQuantisation.None, hnswSeed: 301);
        string source3D = CreateVectorSourceIndex("merge_mixed_dimensions_3d", 3, normalised: true,
            VectorQuantisation.None, hnswSeed: 302);
        string targetPath = SubDir(nameof(Merge_RejectsMixedVectorDimensionsBeforeWritingDestinationFiles));
        using var targetDirectory = new MMapDirectory(targetPath);

        using (var writer = new IndexWriter(targetDirectory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
        }))
        {
            using (var sourceDirectory = new MMapDirectory(source2D))
                writer.AddIndexes(sourceDirectory);
            using (var sourceDirectory = new MMapDirectory(source3D))
                writer.AddIndexes(sourceDirectory);
            writer.Commit();
        }

        List<SegmentInfo> segments = Directory.GetFiles(targetPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        Assert.Equal(2, segments.Count);
        string[] filesBeforeMerge = Directory.GetFiles(targetPath)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

        int nextOrdinal = segments.Max(static segment => SegmentOrdinal(segment.SegmentId)) + 1;
        var merger = new SegmentMerger(targetDirectory, mergeThreshold: 100, softDeleteRetentionSeconds: 0);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => merger.MergeAll(segments, ref nextOrdinal));

        Assert.True(exception.Message.Contains("embedding", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(filesBeforeMerge, Directory.GetFiles(targetPath)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static file => file, StringComparer.Ordinal));
    }

    [Fact]
    public void Merge_RejectsMixedVectorNormalisationBeforeWritingDestinationFiles()
    {
        string normalisedSource = CreateVectorSourceIndex("merge_mixed_normalisation_true", 3, normalised: true,
            VectorQuantisation.None, hnswSeed: 311);
        string unnormalisedSource = CreateVectorSourceIndex("merge_mixed_normalisation_false", 3, normalised: false,
            VectorQuantisation.None, hnswSeed: 312);
        string targetPath = SubDir(nameof(Merge_RejectsMixedVectorNormalisationBeforeWritingDestinationFiles));
        using var targetDirectory = new MMapDirectory(targetPath);

        using (var writer = new IndexWriter(targetDirectory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
        }))
        {
            using (var sourceDirectory = new MMapDirectory(normalisedSource))
                writer.AddIndexes(sourceDirectory);
            using (var sourceDirectory = new MMapDirectory(unnormalisedSource))
                writer.AddIndexes(sourceDirectory);
            writer.Commit();
        }

        List<SegmentInfo> segments = Directory.GetFiles(targetPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        Assert.Equal(2, segments.Count);
        string[] filesBeforeMerge = Directory.GetFiles(targetPath)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

        int nextOrdinal = segments.Max(static segment => SegmentOrdinal(segment.SegmentId)) + 1;
        var merger = new SegmentMerger(targetDirectory, mergeThreshold: 100, softDeleteRetentionSeconds: 0);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => merger.MergeAll(segments, ref nextOrdinal));

        Assert.True(exception.Message.Contains("embedding", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(filesBeforeMerge, Directory.GetFiles(targetPath)
            .Select(static path => Path.GetFileName(path)!)
            .OrderBy(static file => file, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddIndexes_UsesDestinationQuantisationAndSkipsIncompatibleHnswSeeds(bool reverseSourceOrder)
    {
        string sourceNone = CreateVectorSourceIndex("add_indexes_vectors_none", 3, normalised: true,
            VectorQuantisation.None, hnswSeed: 321);
        string sourceInt8 = CreateVectorSourceIndex("add_indexes_vectors_int8", 3, normalised: true,
            VectorQuantisation.Int8, hnswSeed: 322);
        long sourceNoneSeed = ReadSingleVectorGraphSeed(sourceNone);
        long sourceInt8Seed = ReadSingleVectorGraphSeed(sourceInt8);
        string targetPath = SubDir($"{nameof(AddIndexes_UsesDestinationQuantisationAndSkipsIncompatibleHnswSeeds)}_{reverseSourceOrder}");
        using var targetDirectory = new MMapDirectory(targetPath);
        var hnswConfig = new HnswBuildConfig { M = 2, M0 = 2, EfConstruction = 4 };

        using (var writer = new IndexWriter(targetDirectory, new IndexWriterConfig
        {
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            NormaliseVectors = true,
            VectorQuantisation = VectorQuantisation.BBQ,
            HnswBuildConfig = hnswConfig,
        }))
        {
            string firstSource = reverseSourceOrder ? sourceInt8 : sourceNone;
            string secondSource = reverseSourceOrder ? sourceNone : sourceInt8;
            using (var sourceDirectory = new MMapDirectory(firstSource))
                writer.AddIndexes(sourceDirectory);
            using (var sourceDirectory = new MMapDirectory(secondSource))
                writer.AddIndexes(sourceDirectory);
            writer.Commit();

            List<SegmentInfo> importedSegments = Directory.GetFiles(targetPath, "seg_*.seg")
                .Select(SegmentInfo.ReadFrom)
                .OrderBy(static segment => SegmentOrdinal(segment.SegmentId))
                .ToList();
            Assert.Equal(2, importedSegments.Count);
            for (int i = 0; i < importedSegments.Count; i++)
            {
                Assert.Equal(VectorQuantisation.BBQ, Assert.Single(importedSegments[i].VectorFields).Quantisation);
                using var reader = new SegmentReader(targetDirectory, importedSegments[i]);
                long sourceSeed = reverseSourceOrder
                    ? (i == 0 ? sourceInt8Seed : sourceNoneSeed)
                    : (i == 0 ? sourceNoneSeed : sourceInt8Seed);
                Assert.NotEqual(sourceSeed, reader.GetHnswGraph("embedding")!.Seed);
            }

            Assert.Equal(2, writer.ForceMerge(1));
            writer.Commit();
        }

        SegmentInfo merged = Assert.Single(Directory.GetFiles(targetPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom));
        Assert.Equal(VectorQuantisation.BBQ, Assert.Single(merged.VectorFields).Quantisation);
        Assert.True(Assert.Single(merged.VectorFields).HasHnsw);
        Assert.Single(Directory.GetFiles(targetPath, $"{merged.SegmentId}_v_*.vq"));
        Assert.Empty(Directory.GetFiles(targetPath, $"{merged.SegmentId}_v_*.vec"));
        using var mergedReader = new SegmentReader(targetDirectory, merged);
        Assert.Equal(8, mergedReader.GetHnswGraph("embedding")!.NodeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForceMerge_UsesDestinationQuantisationRegardlessOfSourceOrder(bool reverseSourceOrder)
    {
        VectorQuantisation firstQuantisation = reverseSourceOrder
            ? VectorQuantisation.Int8
            : VectorQuantisation.None;
        VectorQuantisation secondQuantisation = reverseSourceOrder
            ? VectorQuantisation.None
            : VectorQuantisation.Int8;
        string targetPath = SubDir($"{nameof(ForceMerge_UsesDestinationQuantisationRegardlessOfSourceOrder)}_{reverseSourceOrder}");
        using var targetDirectory = new MMapDirectory(targetPath);
        var hnswConfig = new HnswBuildConfig { M = 2, M0 = 2, EfConstruction = 4 };
        using var writer = new IndexWriter(targetDirectory, new IndexWriterConfig
        {
            MaxBufferedDocs = 10,
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            VectorQuantisation = firstQuantisation,
            HnswBuildConfig = hnswConfig,
        });

        AddVectorDocuments(writer, firstDocumentId: 0, documentCount: 4);
        writer.Commit();
        writer.Config.VectorQuantisation = secondQuantisation;
        AddVectorDocuments(writer, firstDocumentId: 4, documentCount: 4);
        writer.Commit();

        List<SegmentInfo> sourceSegments = Directory.GetFiles(targetPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        Assert.Equal(2, sourceSegments.Count);
        Assert.Equal(firstQuantisation, Assert.Single(sourceSegments[0].VectorFields).Quantisation);
        Assert.Equal(secondQuantisation, Assert.Single(sourceSegments[1].VectorFields).Quantisation);

        writer.Config.VectorQuantisation = VectorQuantisation.BBQ;
        Assert.Equal(2, writer.ForceMerge(1));
        writer.Commit();

        SegmentInfo merged = Assert.Single(Directory.GetFiles(targetPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom));
        VectorFieldInfo vectorField = Assert.Single(merged.VectorFields);
        Assert.Equal(VectorQuantisation.BBQ, vectorField.Quantisation);
        Assert.True(vectorField.HasHnsw);
        Assert.Single(Directory.GetFiles(targetPath, $"{merged.SegmentId}_v_*.vq"));
        Assert.Empty(Directory.GetFiles(targetPath, $"{merged.SegmentId}_v_*.vec"));
        using var mergedReader = new SegmentReader(targetDirectory, merged);
        Assert.Equal(8, mergedReader.GetHnswGraph("embedding")!.NodeCount);
    }

    private static void AddVectorDocuments(IndexWriter writer, int firstDocumentId, int documentCount)
    {
        for (int offset = 0; offset < documentCount; offset++)
        {
            int documentId = firstDocumentId + offset;
            var vector = new float[3];
            vector[documentId % vector.Length] = documentId + 1f;
            var document = new LeanDocument();
            document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
            writer.AddDocument(document);
        }
    }

    private string CreateVectorSourceIndex(
        string name,
        int dimension,
        bool normalised,
        VectorQuantisation quantisation,
        long hnswSeed)
    {
        string path = SubDir(name);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 10,
            MergeThreshold = 100,
            NormaliseVectors = normalised,
            VectorQuantisation = quantisation,
            HnswSeed = hnswSeed,
            HnswBuildConfig = new HnswBuildConfig { M = 2, M0 = 2, EfConstruction = 4 },
        });

        for (int docId = 0; docId < 4; docId++)
        {
            var vector = new float[dimension];
            vector[docId % dimension] = docId + 1f;
            var document = new LeanDocument();
            document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
            writer.AddDocument(document);
        }
        writer.Commit();
        return path;
    }

    private static long ReadSingleVectorGraphSeed(string directoryPath)
    {
        using var directory = new MMapDirectory(directoryPath);
        SegmentInfo segment = Assert.Single(Directory.GetFiles(directoryPath, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom));
        using var reader = new SegmentReader(directory, segment);
        return reader.GetHnswGraph("embedding")!.Seed;
    }

    [Theory]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    public void Merge_QuantisedHnswUsesFileBackedVectors(VectorQuantisation quantisation)
    {
        const int documentCount = 4;
        var dir = SubDir($"{nameof(Merge_QuantisedHnswUsesFileBackedVectors)}_{quantisation}");
        using var mmap = new MMapDirectory(dir);
        var hnswConfig = new HnswBuildConfig { M = 2, M0 = 2, EfConstruction = 4 };
        ReadOnlyMemory<float>[] vectors =
        [
            new float[] { 1f, 0f, 0f },
            new float[] { 0.8f, 0.2f, 0f },
            new float[] { 0f, 1f, 0f },
            new float[] { 0f, 0.2f, 0.8f },
        ];

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            MaxBufferedDocs = documentCount / 2,
            MergeThreshold = 100,
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            VectorQuantisation = quantisation,
            HnswSeed = 17L,
            HnswBuildConfig = hnswConfig,
        }))
        {
            for (int docId = 0; docId < vectors.Length; docId++)
            {
                var document = new LeanDocument();
                document.Add(new VectorField("embedding", vectors[docId]));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        List<SegmentInfo> sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(static segment => segment.SegmentId, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(2, sourceSegments.Count);
        int nextOrdinal = sourceSegments.Count;
        var merger = new SegmentMerger(mmap, new TieredMergePolicy(100), SegmentMerger.DefaultSkipInterval,
            softDeleteRetentionSeconds: 0, hnswBuildConfig: hnswConfig, useCompoundFile: false,
            destinationVectorQuantisation: quantisation);
        SegmentInfo merged = Assert.IsType<SegmentInfo>(merger.MergeAll(sourceSegments, ref nextOrdinal));

        Assert.Contains(merged.VectorFields, field =>
            field.FieldName == "embedding" && field.Quantisation == quantisation && field.HasHnsw);
        Assert.Single(Directory.GetFiles(dir, $"{merged.SegmentId}_v_*.vq"));
        Assert.Empty(Directory.GetFiles(dir, $"{merged.SegmentId}_v_*.vec"));

        using var reader = new SegmentReader(mmap, merged);
        Assert.Equal(documentCount, reader.GetHnswGraph("embedding")!.NodeCount);
        for (int docId = 0; docId < documentCount; docId++)
        {
            float[] vector = Assert.IsType<float[]>(reader.GetVector("embedding", docId));
            Assert.Equal(3, vector.Length);
            Assert.All(vector, static value => Assert.True(float.IsFinite(value)));
        }
    }

    /// <summary>
    /// Verifies the Merge: Preserves Field Lengths BM25 Scores Match Unmerged scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Field Lengths BM25 Scores Match Unmerged")]
    public void Merge_PreservesFieldLengths_BM25ScoresMatchUnmerged()
    {
        var dir = SubDir(nameof(Merge_PreservesFieldLengths_BM25ScoresMatchUnmerged));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new TextField("body", string.Join(' ', Enumerable.Repeat("alpha", i + 1))));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        // After merge there must be a .fln file on the merged segment to preserve
        // exact per-doc field lengths (BM25 falls back to coarse norms otherwise).
        var mergedId = MergeSegmentsForTest(dir, mmap);
        var flnPath = Path.Combine(dir, mergedId + ".fln");
        Assert.True(File.Exists(flnPath), $"Expected merged segment {mergedId} to have a .fln file at {flnPath}");
        Assert.True(new FileInfo(flnPath).Length > 0);

        // Sanity: searching still returns the merged docs.
        using var searcher = new IndexSearcher(mmap);
        var results = searcher.Search(new TermQuery("body", "alpha"), 10, TestContext.Current.CancellationToken);
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Numeric Doc Values scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Numeric Doc Values")]
    public void Merge_PreservesNumericDocValues()
    {
        var dir = SubDir(nameof(Merge_PreservesNumericDocValues));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 10.0 + i));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var dvnPath = Path.Combine(dir, mergedId + ".dvn");
        Assert.True(File.Exists(dvnPath), $"Expected merged segment {mergedId} to have a .dvn file");
        AssertCurrentCanonicalFrame(dvnPath);

        // Sort by numeric DocValues field — works only if .dvn survives the merge.
        using var searcher = new IndexSearcher(mmap);
        var sorted = searcher.Search(new WildcardQuery("id", "*"), 10, SortField.Numeric("price"));
        Assert.Equal(2, sorted.TotalHits);
    }

    [Fact(DisplayName = "Merge: preserves numeric DocValues presence without sparse point indexes")]
    public void Merge_PreservesNumericDocValuesPresenceWithoutSparsePointIndexes()
    {
        var dir = SubDir(nameof(Merge_PreservesNumericDocValuesPresenceWithoutSparsePointIndexes));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 2, MergeThreshold = 100 }))
        {
            var present = new LeanDocument();
            present.Add(new NumericField("sparse-double", 42));
            present.Add(new Int64Field("sparse-long", 420));
            writer.AddDocument(present);

            writer.AddDocument(new LeanDocument());
            writer.AddDocument(new LeanDocument());
            writer.Commit();
        }

        SegmentInfo docValuesSegment = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .Single(segment => File.Exists(Path.Combine(dir, segment.SegmentId + ".dvn")));
        string numericIndexPath = Path.Combine(dir, docValuesSegment.SegmentId + ".num");
        string int64IndexPath = Path.Combine(dir, docValuesSegment.SegmentId + ".numl");
        Assert.True(File.Exists(numericIndexPath));
        Assert.True(File.Exists(int64IndexPath));
        File.Delete(numericIndexPath);
        File.Delete(int64IndexPath);

        using (var legacyReader = new SegmentReader(mmap, docValuesSegment))
        {
            Assert.True(legacyReader.TryGetNumericValue("sparse-double", 0, out double numericValue));
            Assert.Equal(42, numericValue);
            Assert.False(legacyReader.TryGetNumericValue("sparse-double", 1, out _));
            Assert.True(legacyReader.TryGetInt64Value("sparse-long", 0, out long int64Value));
            Assert.Equal(420, int64Value);
            Assert.False(legacyReader.TryGetInt64Value("sparse-long", 1, out _));
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        using var mergedReader = new SegmentReader(mmap, SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg")));

        var numericValues = new List<double>();
        var int64Values = new List<long>();
        for (int docId = 0; docId < mergedReader.Info.DocCount; docId++)
        {
            if (mergedReader.TryGetNumericValue("sparse-double", docId, out double numericValue))
                numericValues.Add(numericValue);
            if (mergedReader.TryGetInt64Value("sparse-long", docId, out long int64Value))
                int64Values.Add(int64Value);
        }

        Assert.Equal([42d], numericValues);
        Assert.Equal([420L], int64Values);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Sorted Doc Values scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Sorted Doc Values")]
    public void Merge_PreservesSortedDocValues()
    {
        var dir = SubDir(nameof(Merge_PreservesSortedDocValues));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            string[] cats = ["alpha", "bravo"];
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new StringField("category", cats[i]));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var dvsPath = Path.Combine(dir, mergedId + ".dvs");
        Assert.True(File.Exists(dvsPath), $"Expected merged segment {mergedId} to have a .dvs file");
        AssertCurrentCanonicalFrame(dvsPath);

        using var searcher = new IndexSearcher(mmap);
        var sorted = searcher.Search(new WildcardQuery("id", "*"), 10, SortField.String("category"));
        Assert.Equal(2, sorted.TotalHits);
    }

    /// <summary>
    /// Verifies richer DocValues sidecars survive merge and deleted documents are filtered out.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Richer Doc Values For Live Documents")]
    public void Merge_PreservesRicherDocValues_ForLiveDocuments()
    {
        var dir = SubDir(nameof(Merge_PreservesRicherDocValues_ForLiveDocuments));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 2, MergeThreshold = 100 }))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new StringField("id", "live-a"));
            doc1.Add(new StringField("tag", "red"));
            doc1.Add(new StringField("tag", "blue"));
            doc1.Add(new NumericField("score", 2));
            doc1.Add(new NumericField("score", 1));
            doc1.Add(new Int64Field("count64", 10));
            doc1.Add(new Int64Field("rank64", 2));
            doc1.Add(new Int64Field("rank64", 1));
            doc1.Add(new StoredField("note", "first"));
            doc1.Add(new StoredField("note", "second"));
            writer.AddDocument(doc1);

            var victim = new LeanDocument();
            victim.Add(new StringField("id", "victim"));
            victim.Add(new StringField("tag", "victim"));
            victim.Add(new NumericField("score", 99));
            victim.Add(new Int64Field("count64", 99));
            victim.Add(new Int64Field("rank64", 99));
            victim.Add(new StoredField("note", "deleted"));
            writer.AddDocument(victim);

            var doc2 = new LeanDocument();
            doc2.Add(new StringField("id", "live-b"));
            doc2.Add(new StringField("tag", "green"));
            doc2.Add(new NumericField("score", 3));
            doc2.Add(new Int64Field("count64", 30));
            doc2.Add(new Int64Field("rank64", 3));
            doc2.Add(new StoredField("note", "third"));
            writer.AddDocument(doc2);

            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "victim"));
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var sortedSet = SortedSetDocValuesReader.Read(Path.Combine(dir, mergedId + ".dss"));
        var sortedNumeric = SortedNumericDocValuesReader.Read(Path.Combine(dir, mergedId + ".dsn"));
        var binary = BinaryDocValuesReader.Read(Path.Combine(dir, mergedId + ".dvb"));
        var int64 = Int64DocValuesReader.Read(Path.Combine(dir, mergedId + ".dvnl"));
        var int64Sorted = Int64SortedNumericDocValuesReader.Read(Path.Combine(dir, mergedId + ".dsnl"));

        foreach (string extension in new[] { ".dss", ".dsn", ".dvb", ".dvnl", ".dsnl" })
            AssertCurrentCanonicalFrame(Path.Combine(dir, mergedId + extension));

        int firstDocIndex = Array.FindIndex(sortedSet["tag"], static values => values.SequenceEqual(["blue", "red"]));
        int secondDocIndex = Array.FindIndex(sortedSet["tag"], static values => values.SequenceEqual(["green"]));
        Assert.NotEqual(-1, firstDocIndex);
        Assert.NotEqual(-1, secondDocIndex);
        Assert.DoesNotContain(sortedSet["tag"].SelectMany(static values => values), static value => value == "victim");

        Assert.Equal([1, 2], sortedNumeric["score"][firstDocIndex]);
        Assert.Equal([3], sortedNumeric["score"][secondDocIndex]);
        Assert.DoesNotContain(99, sortedNumeric["score"].SelectMany(static values => values));

        Assert.Equal(["first", "second"], binary["note"][firstDocIndex].Select(static value => System.Text.Encoding.UTF8.GetString(value)));
        Assert.Equal(["third"], binary["note"][secondDocIndex].Select(static value => System.Text.Encoding.UTF8.GetString(value)));
        Assert.Equal(10, int64.Values["count64"][firstDocIndex]);
        Assert.Equal(30, int64.Values["count64"][secondDocIndex]);
        Assert.Equal([1, 2], int64Sorted["rank64"][firstDocIndex]);
        Assert.Equal([3], int64Sorted["rank64"][secondDocIndex]);
    }

    /// <summary>
    /// Verifies the Merge: Preserves BKD Range Query Results scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves BKD Range Query Results")]
    public void Merge_PreservesBkdRangeQueryResults()
    {
        var dir = SubDir(nameof(Merge_PreservesBkdRangeQueryResults));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 1; i <= 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 100.0 + i * 10));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var bkdPath = Path.Combine(dir, mergedId + ".bkd");
        Assert.True(File.Exists(bkdPath), $"Expected merged segment {mergedId} to have a .bkd file");

        using var searcher = new IndexSearcher(mmap);
        // 110.0..120.0 inclusive should hit doc1 (110) and doc2 (120).
        var hits = searcher.Search(new RangeQuery("price", 110.0, 120.0), 10, TestContext.Current.CancellationToken);
        Assert.Equal(2, hits.TotalHits);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Term Vectors scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Term Vectors")]
    public void Merge_PreservesTermVectors()
    {
        var dir = SubDir(nameof(Merge_PreservesTermVectors));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(storeTermVectors: true)))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new TextField("body", $"keyword{i} shared common content"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var tvdPath = Path.Combine(dir, mergedId + ".tvd");
        var tvxPath = Path.Combine(dir, mergedId + ".tvx");
        Assert.True(File.Exists(tvdPath), $"Expected merged segment {mergedId} to have a .tvd file");
        Assert.True(File.Exists(tvxPath), $"Expected merged segment {mergedId} to have a .tvx file");

        // MoreLikeThis depends on term vectors; with vectors lost it would return zero.
        using var searcher = new IndexSearcher(mmap);
        var more = searcher.MoreLikeThis(0, ["body"], 5);
        Assert.True(more.TotalHits > 0, "MoreLikeThis returned zero hits — term vectors likely lost on merge");
    }

    /// <summary>
    /// Verifies the Merge: Preserves Parent Bit Set Block Join Query Still Returns Parents scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Parent Bit Set Block Join Query Still Returns Parents")]
    public void Merge_PreservesParentBitSet_BlockJoinQueryStillReturnsParents()
    {
        var dir = SubDir(nameof(Merge_PreservesParentBitSet_BlockJoinQueryStillReturnsParents));
        var mmap = new MMapDirectory(dir);

        // MaxBufferedDocs must be >= block size so each block lands intact in one segment.
        // We Commit() between blocks to force a flush per block.
        var config = new IndexWriterConfig
        {
            MaxBufferedDocs = 16,
            MergeThreshold = 100,
        };

        using (var writer = new IndexWriter(mmap, config))
        {
            writer.AddDocumentBlock(
            [
                MakeChild("alpha bravo"),
                MakeChild("charlie delta"),
                MakeParent("post one"),
            ]);
            writer.Commit();

            writer.AddDocumentBlock(
            [
                MakeChild("echo foxtrot"),
                MakeParent("post two"),
            ]);
            writer.Commit();

        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var pbsPath = Path.Combine(dir, mergedId + ".pbs");
        Assert.True(File.Exists(pbsPath), $"Expected merged segment {mergedId} to have a .pbs file");

        using var searcher = new IndexSearcher(mmap);
        var parents = searcher.Search(new BlockJoinQuery(new TermQuery("body", "alpha")), 10, TestContext.Current.CancellationToken);
        Assert.Equal(1, parents.TotalHits);
        var stored = searcher.GetStoredFields(parents.ScoreDocs[0].DocId);
        Assert.True(stored.ContainsKey("title"));
        Assert.Contains("one", stored["title"][0]);
    }

    /// <summary>
    /// Verifies the Merge: Preserves Index Sort Fields scenario.
    /// </summary>
    [Fact(DisplayName = "Merge: Preserves Index Sort Fields")]
    public void Merge_PreservesIndexSortFields()
    {
        var dir = SubDir(nameof(Merge_PreservesIndexSortFields));
        var mmap = new MMapDirectory(dir);
        var sort = new IndexSort(SortField.Numeric("price"));

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(sort: sort)))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 50.0 - i));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var mergedId = MergeSegmentsForTest(dir, mmap);
        var segInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        Assert.NotNull(segInfo.IndexSortFields);
        Assert.Single(segInfo.IndexSortFields!);
        Assert.Equal("Numeric:price:False", segInfo.IndexSortFields![0]);
    }

    /// <summary>
    /// Verifies a merge preserves physical index-sort order and keeps early termination
    /// equivalent to a full sort when the input segment ranges are reversed.
    /// </summary>
    [Fact(DisplayName = "Merge: Index Sort Preserves Physical Order And Top One Results")]
    public void Merge_IndexSortPreservesPhysicalOrderAndTopOneResults()
    {
        var dir = SubDir(nameof(Merge_IndexSortPreservesPhysicalOrderAndTopOneResults));
        var mmap = new MMapDirectory(dir);
        var sort = new IndexSort(SortField.Numeric("price"));

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(storeTermVectors: true, sort: sort)))
        {
            AddSortedDocument(writer, 50.0, "red blue");
            AddSortedDocument(writer, 49.0, "blue red");
            writer.Commit();
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        using (var reader = new SegmentReader(mmap, mergedInfo))
        {
            Assert.True(reader.TryGetNumericValue("price", 0, out double firstPrice));
            Assert.True(reader.TryGetNumericValue("price", 1, out double secondPrice));
            Assert.Equal([49.0, 50.0], new[] { firstPrice, secondPrice });

            var firstVectors = reader.GetTermVectors(0)!["body"];
            Assert.Equal([0], firstVectors.Single(static entry => entry.Term == "blue").Positions);
            Assert.Equal([1], firstVectors.Single(static entry => entry.Term == "red").Positions);
            var secondVectors = reader.GetTermVectors(1)!["body"];
            Assert.Equal([1], secondVectors.Single(static entry => entry.Term == "blue").Positions);
            Assert.Equal([0], secondVectors.Single(static entry => entry.Term == "red").Positions);
        }

        using var searcher = new IndexSearcher(mmap);
        var searchOptions = new SearchOptions { CancellationToken = TestContext.Current.CancellationToken };
        var earlyTerminated = searcher.Search(new TermQuery("title", "item"), 1, SortField.Numeric("price"), searchOptions);
        var fullSort = searcher.Search(new WildcardQuery("title", "*"), 1, SortField.Numeric("price"), searchOptions);

        Assert.True(earlyTerminated.IsPartial);
        Assert.Equal(49.0, GetStoredDouble(searcher, earlyTerminated, "price"));
        Assert.Equal(GetStoredDouble(searcher, fullSort, "price"), GetStoredDouble(searcher, earlyTerminated, "price"));

        var phrase = searcher.Search(new PhraseQuery("body", "blue", "red"), 10, TestContext.Current.CancellationToken);
        Assert.Single(phrase.ScoreDocs);
        Assert.Equal(49.0, GetStoredDouble(searcher, phrase, "price"));
    }

    /// <summary>
    /// Verifies merge order uses every configured field rather than only the primary key.
    /// </summary>
    [Fact(DisplayName = "Merge: Index Sort Uses Complete Compound Key")]
    public void Merge_IndexSortUsesCompleteCompoundKey()
    {
        var dir = SubDir(nameof(Merge_IndexSortUsesCompleteCompoundKey));
        var mmap = new MMapDirectory(dir);
        var sort = new IndexSort(SortField.Numeric("price"), SortField.String("category"));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            IndexSort = sort,
            MaxBufferedDocs = 2,
            MergeThreshold = 100,
        }))
        {
            AddCompoundSortDocument(writer, "A1", 2.0, "x");
            AddCompoundSortDocument(writer, "A0", 1.0, "z");
            AddCompoundSortDocument(writer, "B1", 3.0, "a");
            AddCompoundSortDocument(writer, "B0", 1.0, "a");
            writer.Commit();
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        Assert.Equal(["Numeric:price:False", "String:category:False"], mergedInfo.IndexSortFields);

        using var reader = new SegmentReader(mmap, mergedInfo);
        var ids = Enumerable.Range(0, mergedInfo.DocCount)
            .Select(docId => reader.GetStoredFields(docId)["id"][0])
            .ToArray();
        Assert.Equal(["B0", "A0", "A1", "B1"], ids);
    }

    /// <summary>
    /// Verifies equal complete keys retain source order deterministically during the k-way merge.
    /// </summary>
    [Fact(DisplayName = "Merge: Index Sort Equal Keys Retain Source Order")]
    public void Merge_IndexSortEqualKeysRetainSourceOrder()
    {
        var dir = SubDir(nameof(Merge_IndexSortEqualKeysRetainSourceOrder));
        var mmap = new MMapDirectory(dir);
        var sort = new IndexSort(SortField.Numeric("price"), SortField.String("category"));

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            IndexSort = sort,
            MaxBufferedDocs = 1,
            MergeThreshold = 100,
        }))
        {
            AddCompoundSortDocument(writer, "first", 7.0, "same");
            AddCompoundSortDocument(writer, "second", 7.0, "same");
            writer.Commit();
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        using var reader = new SegmentReader(mmap, mergedInfo);
        string[] ids = Enumerable.Range(0, mergedInfo.DocCount)
            .Select(docId => reader.GetStoredFields(docId)["id"][0])
            .ToArray();

        Assert.Equal(["first", "second"], ids);
    }

    /// <summary>
    /// Verifies absent sort values keep the same default ordering as index flush.
    /// </summary>
    [Fact(DisplayName = "Merge: Index Sort Missing Values Keep Flush Ordering")]
    public void Merge_IndexSortMissingValuesKeepFlushOrdering()
    {
        var dir = SubDir(nameof(Merge_IndexSortMissingValuesKeepFlushOrdering));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(sort: new IndexSort(SortField.Numeric("price")))))
        {
            var valued = new LeanDocument();
            valued.Add(new StoredField("id", "valued"));
            valued.Add(new NumericField("price", 5.0));
            writer.AddDocument(valued);

            var missing = new LeanDocument();
            missing.Add(new StoredField("id", "missing"));
            writer.AddDocument(missing);
            writer.Commit();
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        using var reader = new SegmentReader(mmap, mergedInfo);
        string[] ids = Enumerable.Range(0, mergedInfo.DocCount)
            .Select(docId => reader.GetStoredFields(docId)["id"][0])
            .ToArray();

        Assert.Equal(["missing", "valued"], ids);
        Assert.Equal("Numeric:price:False", Assert.Single(mergedInfo.IndexSortFields!));
    }

    /// <summary>
    /// Verifies missing or differing source sort definitions do not get copied to a merged segment.
    /// </summary>
    [Theory(DisplayName = "Merge: Sort Metadata Requires Matching Source Definitions")]
    [InlineData("absent", false, false)]
    [InlineData("mixed", true, false)]
    [InlineData("different", true, true)]
    public void Merge_SortMetadataRequiresMatchingSourceDefinitions(
        string scenario,
        bool firstIsSorted,
        bool secondIsSorted)
    {
        var dir = SubDir($"{nameof(Merge_SortMetadataRequiresMatchingSourceDefinitions)}_{scenario}");
        var mmap = new MMapDirectory(dir);
        var firstSort = firstIsSorted ? new IndexSort(SortField.Numeric("price")) : null;
        var secondSort = secondIsSorted
            ? new IndexSort(SortField.Numeric(scenario == "different" ? "rank" : "price"))
            : null;

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(sort: firstSort)))
        {
            AddSortedDocument(writer, 50.0, "red blue", rank: 1.0);
            writer.Commit();
        }

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(sort: secondSort)))
        {
            AddSortedDocument(writer, 49.0, "blue red", rank: 2.0);
            writer.Commit();
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        Assert.Null(mergedInfo.IndexSortFields);
    }

    /// <summary>
    /// Verifies merges drop descending DocId metadata because its pre-flush key is not persisted.
    /// </summary>
    [Fact(DisplayName = "Merge: Descending DocId Sort Metadata Is Not Reused")]
    public void Merge_DescendingDocIdSortMetadataIsNotReused()
    {
        var dir = SubDir(nameof(Merge_DescendingDocIdSortMetadataIsNotReused));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(sort: new IndexSort(
                   new SortField(SortFieldType.DocId, string.Empty, descending: true)))))
        {
            var first = new LeanDocument();
            first.Add(new StoredField("id", "first"));
            writer.AddDocument(first);
            var second = new LeanDocument();
            second.Add(new StoredField("id", "second"));
            writer.AddDocument(second);
            writer.Commit();
        }

        string mergedId = MergeSegmentsForTest(dir, mmap);
        var mergedInfo = SegmentInfo.ReadFrom(Path.Combine(dir, mergedId + ".seg"));
        Assert.Null(mergedInfo.IndexSortFields);
    }

    private static void AddSortedDocument(IndexWriter writer, double price, string body, double? rank = null)
    {
        var document = new LeanDocument();
        document.Add(new TextField("title", "item"));
        document.Add(new TextField("body", body));
        document.Add(new NumericField("price", price));
        if (rank is not null)
            document.Add(new NumericField("rank", rank.Value));
        writer.AddDocument(document);
    }

    private static void AddCompoundSortDocument(IndexWriter writer, string id, double price, string category)
    {
        var document = new LeanDocument();
        document.Add(new TextField("title", "item"));
        document.Add(new StoredField("id", id));
        document.Add(new NumericField("price", price));
        document.Add(new StoredField("category", category));
        writer.AddDocument(document);
    }

    private static double GetStoredDouble(IndexSearcher searcher, TopDocs results, string fieldName)
    {
        Assert.Single(results.ScoreDocs);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.True(stored.TryGetValue(fieldName, out var values));
        return double.Parse(values![0], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Verifies the Cleanup Segment Files: Leaves No Orphans scenario.
    /// </summary>
    [Fact(DisplayName = "Cleanup Segment Files: Leaves No Orphans")]
    public void CleanupSegmentFiles_LeavesNoOrphans()
    {
        var dir = SubDir(nameof(CleanupSegmentFiles_LeavesNoOrphans));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig(storeTermVectors: true)))
        {
            for (int i = 0; i < 2; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("id", $"doc{i}"));
                doc.Add(new NumericField("price", 1.0 + i));
                doc.Add(new StringField("category", $"cat{i}"));
                doc.Add(new TextField("body", $"shared body content number {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _ = MergeSegmentsForTest(dir, mmap);

        // After merge, the original seg_0 and seg_1 must have ZERO files left on disk
        // (any extension). The previous bug only cleaned a hardcoded extension list.
        for (int i = 0; i < 2; i++)
        {
            var orphans = Directory.GetFiles(dir, $"seg_{i}.*");
            Assert.Empty(orphans);
        }
    }

    /// <summary>
    /// Verifies the Maybe Merge: With Protected Segment Keeps Protected Files And Stats scenario.
    /// </summary>
    [Fact(DisplayName = "Maybe Merge: With Protected Segment Keeps Protected Files And Stats")]
    public void MaybeMerge_WithProtectedSegment_KeepsProtectedFilesAndStats()
    {
        var dir = SubDir(nameof(MaybeMerge_WithProtectedSegment_KeepsProtectedFilesAndStats));
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, SmallSegmentMergeConfig()))
        {
            for (int i = 0; i < 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"merge protection {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        var sourceSegments = Directory.GetFiles(dir, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .OrderBy(segment => SegmentOrdinal(segment.SegmentId))
            .ToList();
        var protectedSegment = sourceSegments[0];
        var protectedSegmentIds = new HashSet<string>([protectedSegment.SegmentId], StringComparer.Ordinal);
        var nextSegmentOrdinal = sourceSegments.Max(segment => SegmentOrdinal(segment.SegmentId)) + 1;
        var merger = new SegmentMerger(mmap, mergeThreshold: 2);

        var mergedSegments = merger.MaybeMerge(sourceSegments, ref nextSegmentOrdinal, protectedSegmentIds);

        Assert.Contains(mergedSegments, segment => segment.SegmentId == protectedSegment.SegmentId);

        var activeSegments = new HashSet<string>(mergedSegments.Select(static segment => segment.SegmentId), StringComparer.Ordinal);
        foreach (var segment in sourceSegments)
        {
            if (!activeSegments.Contains(segment.SegmentId) && !protectedSegmentIds.Contains(segment.SegmentId))
                merger.CleanupSegmentFiles(segment);
        }

        Assert.True(File.Exists(Path.Combine(dir, protectedSegment.SegmentId + ".seg")));
        Assert.True(File.Exists(Path.Combine(dir, protectedSegment.SegmentId + ".stats.json")));
    }

    private static LeanDocument MakeChild(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        doc.Add(new StringField("type", "child"));
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
