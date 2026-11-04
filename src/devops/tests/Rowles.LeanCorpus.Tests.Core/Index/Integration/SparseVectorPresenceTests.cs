using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Tests.Core.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
[Area(TestArea.CodecKit)]
public sealed class SparseVectorPresenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"sparse-vector-{Guid.NewGuid():N}");
    public SparseVectorPresenceTests() => Directory.CreateDirectory(_path);
    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Theory]
    [InlineData(VectorQuantisation.None, false)]
    [InlineData(VectorQuantisation.None, true)]
    [InlineData(VectorQuantisation.BBQ, false)]
    [InlineData(VectorQuantisation.BBQ, true)]
    public void SearchAndMergeExcludePadding(VectorQuantisation quantisation, bool hnsw)
    {
        using var directory = new MMapDirectory(_path);
        int dimension = VectorDimension(quantisation);
        var config = Config(quantisation);
        if (quantisation == VectorQuantisation.BBQ)
        {
            Assert.Throws<ArgumentException>(() => new IndexWriter(directory, config));
            Assert.Empty(Directory.EnumerateFiles(_path));
            return;
        }
        using (var writer = new IndexWriter(directory, config))
        {
            AddSparseDocuments(writer, dimension);
            writer.Commit();
            AssertSparseSearch(directory, [0, 2], dimension);
        }
        if (hnsw)
            HistoricalHnswGraphFixtures.AttachToFloat32Index(_path);
        using (var writer = new IndexWriter(directory, config))
        {
            // A second segment ensures ForceMerge actually rewrites the first segment.
            writer.AddDocument(Document(3, null, dimension));
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }
        AssertSparseSearch(directory, [0, 2], dimension);
        using var searcher = new IndexSearcher(directory);
        var segment = Assert.Single(searcher.GetSegmentReaders());
        Assert.True(segment.HasVector("embedding", 0));
        Assert.False(segment.HasVector("embedding", 1));
        Assert.True(segment.HasVector("embedding", 2));
        Assert.False(segment.HasVector("embedding", 3));
        Assert.Null(segment.GetHnswGraph("embedding"));
    }

    [Theory]
    [InlineData(VectorQuantisation.None, false)]
    [InlineData(VectorQuantisation.None, true)]
    public void LegacyMigrationPreservesPayloadAndPresence(VectorQuantisation quantisation, bool compound)
    {
        using var directory = new MMapDirectory(_path);
        CreateLegacyIndex(directory, quantisation, hnsw: true, compound);
        string oldSegment = SegmentId();
        string oldVectorName = VectorName(oldSegment, quantisation);
        var descriptor = Descriptor(quantisation);
        byte[] legacyBody = ReadBody(directory, oldSegment, oldVectorName, descriptor);
        byte[] graphBody = ReadBody(directory, oldSegment, VectorFilePaths.HnswFile(oldSegment, "embedding"), VectorCodecFiles.Hnsw);
        (int DocId, float Score)[] scoresBefore = CaptureSparseVectorScores(directory);
        var action = Assert.Single(IndexCodecMigrator.Plan(directory).Actions, item => item.FormatId == descriptor.FormatId);
        Assert.True(action.CanExecute, action.ReasonCannotExecute);
        Assert.Equal(IndexCodecMigrationActionKind.Rewrite, action.Kind);
        Assert.Equal((byte)1, action.FromVersion);
        Assert.Equal(quantisation == VectorQuantisation.None ? (byte)2 : (byte)3, action.ToVersion);
        AssertSparseSearch(directory, [0, 2]);

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.True(result.Succeeded, string.Join("; ", result.Issues.Select(item => item.Message)));
        string newSegment = SegmentId();
        byte currentVersion = quantisation == VectorQuantisation.None ? (byte)2 : (byte)3;
        byte[] currentBody = ReadBody(directory, newSegment, VectorName(newSegment, quantisation), descriptor, expectedVersion: currentVersion);
        Assert.Equal(legacyBody[..9], currentBody[..9]);
        Assert.Equal(5, currentBody[9]);
        if (quantisation == VectorQuantisation.None)
        {
            Assert.Equal(legacyBody[9..], currentBody[10..]);
        }
        else
        {
            byte expectedStorageKind = quantisation == VectorQuantisation.Int8
                ? (byte)QuantisedVectorStorageKind.LegacyInt8
                : (byte)QuantisedVectorStorageKind.LegacyBbqSign;
            Assert.Equal(expectedStorageKind, currentBody[10]);
            Assert.Equal(legacyBody[9..], currentBody[11..]);
        }
        Assert.Equal(graphBody, ReadBody(directory, newSegment, VectorFilePaths.HnswFile(newSegment, "embedding"), VectorCodecFiles.Hnsw));
        Assert.Equal(scoresBefore, CaptureSparseVectorScores(directory));
        AssertSparseSearch(directory, [0, 2]);
        using (var writer = new IndexWriter(directory, Config(VectorQuantisation.None)))
        {
            writer.AddDocument(Document(3, null));
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }
        AssertSparseSearch(directory, [0, 2]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyV2Int8MigrationPreservesPayloadPresenceAndScores(bool compound)
    {
        using var directory = new MMapDirectory(_path);
        CreateLegacyIndex(directory, VectorQuantisation.Int8, hnsw: true, compound: false);
        string segmentId = SegmentId();
        string vectorName = VectorName(segmentId, VectorQuantisation.Int8);
        byte[] v1Body = ReadBody(directory, segmentId, vectorName, VectorCodecFiles.Quantised, expectedVersion: 1);
        byte[] v2Body = new byte[v1Body.Length + 1];
        v1Body.AsSpan(0, 9).CopyTo(v2Body);
        v2Body[9] = 0b0000_0101; // Documents 0 and 2 are authoritative graph members.
        v1Body.AsSpan(9).CopyTo(v2Body.AsSpan(10));
        using (var output = new IndexOutput(Path.Combine(_path, vectorName)))
        {
            output.WriteByte(2);
            output.WriteBytes(v2Body);
            output.WriteInt64(v2Body.Length);
        }

        if (compound)
        {
            Assert.True(SegmentFileSet.Pack(_path, segmentId));
            SegmentInfo info = SegmentInfo.ReadFrom(Path.Combine(_path, segmentId + ".seg"));
            info.IsCompoundFile = true;
            info.WriteTo(Path.Combine(_path, segmentId + ".seg"));
        }

        byte[] sourceBody = ReadBody(directory, segmentId, vectorName, VectorCodecFiles.Quantised, expectedVersion: 2);
        (int DocId, float Score)[] scoresBefore = CaptureSparseVectorScores(directory);
        var action = Assert.Single(IndexCodecMigrator.Plan(directory).Actions,
            item => item.FormatId == VectorCodecFiles.Quantised.FormatId);
        Assert.Equal((byte)2, action.FromVersion);
        Assert.Equal((byte)3, action.ToVersion);
        Assert.True(action.CanExecute, action.ReasonCannotExecute);

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.True(result.Succeeded, string.Join("; ", result.Issues.Select(item => item.Message)));

        string migratedSegment = SegmentId();
        byte[] migratedBody = ReadBody(directory, migratedSegment,
            VectorName(migratedSegment, VectorQuantisation.Int8), VectorCodecFiles.Quantised, expectedVersion: 3);
        Assert.Equal(sourceBody[..9], migratedBody[..9]);
        Assert.Equal(sourceBody[9], migratedBody[9]);
        Assert.Equal(sourceBody[10..], migratedBody[11..]);
        Assert.Equal(scoresBefore, CaptureSparseVectorScores(directory));
        AssertSparseSearch(directory, [0, 2]);
    }

    [Theory]
    [InlineData(VectorQuantisation.None)]
    public void LegacyWithoutGraphIsNonExecutableAndUnchanged(VectorQuantisation quantisation)
    {
        using var directory = new MMapDirectory(_path);
        CreateLegacyIndex(directory, quantisation, false, false);
        var before = Directory.GetFiles(_path).ToDictionary(Path.GetFileName, File.ReadAllBytes);
        var plan = IndexCodecMigrator.Plan(directory);
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Actions, action => !action.CanExecute && action.ReasonCannotExecute!.Contains("no persisted HNSW"));
        Assert.False(IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false }).Succeeded);
        foreach (var (name, bytes) in before)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_path, name!)));
    }

    [Fact]
    public void VectorRewritePublicationFailurePreservesSourceAndRetrySucceeds()
    {
        using var directory = new MMapDirectory(_path);
        CreateLegacyIndex(directory, VectorQuantisation.None, true, false);
        string segmentId = SegmentId();
        var before = SegmentFileSet.Enumerate(_path, segmentId).FileNames
            .ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(_path, name)));
        var originalCommit = Assert.Single(IndexFileInspector.FindCommitFiles(_path));
        string blocked = Path.Combine(_path, $"segments_{originalCommit.Generation + 1}");
        Directory.CreateDirectory(blocked);
        var options = new IndexCodecMigrationOptions { DryRun = false, ValidateBeforeMigration = false, ValidateAfterMigration = false };
        Assert.False(IndexCodecMigrator.Migrate(directory, options).Succeeded);
        Assert.Equal(segmentId, SegmentId());
        foreach (var (name, bytes) in before)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_path, name!)));
        Directory.Delete(blocked);
        var resumed = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false, ValidateBeforeMigration = false });
        Assert.True(resumed.Succeeded, string.Join("; ", resumed.Issues.Select(issue => issue.Message)));
        AssertSparseSearch(directory, [0, 2]);
    }

    [Theory]
    [InlineData(VectorQuantisation.None, false)]
    [InlineData(VectorQuantisation.None, true)]
    public void MergeRemapsPresenceThroughSortingDeletionAndZeroVectors(VectorQuantisation quantisation, bool deleteZero)
    {
        using var directory = new MMapDirectory(_path);
        int dimension = VectorDimension(quantisation);
        var config = Config(quantisation);
        config.IndexSort = new IndexSort(SortField.String("id", descending: true));
        using (var writer = new IndexWriter(directory, config))
        {
            writer.AddDocument(Document(0, [0, 0], dimension));
            writer.AddDocument(Document(1, null, dimension));
            writer.AddDocument(Document(2, [1, 0], dimension));
            writer.Commit();
        }
        HistoricalHnswGraphFixtures.AttachToFloat32Index(_path);
        using (var writer = new IndexWriter(directory, config))
        {
            writer.AddDocument(Document(3, [0, 1], dimension));
            writer.Commit();
            if (deleteZero) writer.DeleteDocuments(new TermQuery("id", "0"));
            writer.ForceMerge(1);
            writer.Commit();
        }
        using var searcher = new IndexSearcher(directory);
        var segment = Assert.Single(searcher.GetSegmentReaders());
        int[] present = deleteZero ? [0, 1] : [0, 1, 3];
        Assert.Equal(present, Enumerable.Range(0, segment.MaxDoc).Where(docId => segment.HasVector("embedding", docId)).ToArray());
        Assert.Null(segment.GetHnswGraph("embedding"));
        for (int docId = 0; docId < segment.MaxDoc; docId++)
            Assert.Equal(present.Contains(docId), segment.GetVector("embedding", docId) is not null);
        if (!deleteZero && quantisation == VectorQuantisation.None)
            Assert.Equal(new float[dimension], segment.GetVector("embedding", 3));
        var hits = searcher.Search(new VectorQuery("embedding", BasisVector(dimension, 0), 10), 10, TestContext.Current.CancellationToken);
        Assert.Equal(present, hits.ScoreDocs.Select(hit => hit.DocId).Order().ToArray());
        byte[] body = ReadBody(directory, segment.Info.SegmentId, VectorName(segment.Info.SegmentId, quantisation), Descriptor(quantisation));
        Assert.Equal(deleteZero ? 3 : 11, body[9]);
    }

    [Theory]
    [InlineData(VectorQuantisation.None)]
    [InlineData(VectorQuantisation.BBQ)]
    public void AddIndexesPreservesSparsePresenceWhenRewriting(VectorQuantisation destinationQuantisation)
    {
        int dimension = VectorDimension(destinationQuantisation);
        string sourcePath = Path.Combine(_path, "source");
        string targetPath = Path.Combine(_path, "target");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        using var source = new MMapDirectory(sourcePath);
        using var target = new MMapDirectory(targetPath);
        using (var sourceWriter = new IndexWriter(source, Config(VectorQuantisation.None)))
        {
            AddSparseDocuments(sourceWriter, dimension);
            sourceWriter.Commit();
        }
        if (destinationQuantisation == VectorQuantisation.BBQ)
        {
            Assert.Throws<ArgumentException>(() => new IndexWriter(target, Config(destinationQuantisation)));
            Assert.Empty(Directory.EnumerateFiles(targetPath));
            return;
        }
        using (var targetWriter = new IndexWriter(target, Config(destinationQuantisation)))
        {
            targetWriter.AddIndexes(source);
            targetWriter.AddDocument(Document(3, null, dimension));
            targetWriter.Commit();
            targetWriter.ForceMerge(1);
            targetWriter.Commit();
        }
        AssertSparseSearch(target, [0, 2], dimension);
        using var searcher = new IndexSearcher(target);
        var mergedField = Assert.Single(searcher.GetSegmentReaders()).Info.VectorFields.Single();
        Assert.Equal(destinationQuantisation, mergedField.Quantisation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyGraphWithInvalidMembershipCannotMigrate(bool duplicate)
    {
        using var directory = new MMapDirectory(_path);
        CreateLegacyIndex(directory, VectorQuantisation.None, true, false);
        string segmentId = SegmentId();
        string name = VectorFilePaths.HnswFile(segmentId, "embedding");
        byte[] body = ReadBody(directory, segmentId, name, VectorCodecFiles.Hnsw);
        using var stream = new MemoryStream(body);
        using var reader = new BinaryReader(stream);
        stream.Position = 37;
        int levels = reader.ReadInt32();
        var baseOffsets = new List<int>();
        for (int level = levels - 1; level >= 0; level--)
        {
            int count = reader.ReadInt32();
            for (int node = 0; node < count; node++)
            {
                int offset = checked((int)stream.Position);
                _ = reader.ReadInt32();
                int neighbours = reader.ReadInt32();
                if (level == 0) baseOffsets.Add(offset);
                stream.Position += neighbours * sizeof(int);
            }
        }
        Assert.Equal(2, baseOffsets.Count);
        int invalidId = duplicate ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(baseOffsets[0])) : 3;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(baseOffsets[1]), invalidId);
        CodecFileWriter.WriteAtomically(Path.Combine(_path, name), VectorCodecFiles.Hnsw, false,
            output => output.WriteBytes(body, 0, body.Length));
        var plan = IndexCodecMigrator.Plan(directory);
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Actions, action => !action.CanExecute && action.ReasonCannotExecute is not null);
        Assert.False(IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false }).Succeeded);
    }

    [Fact]
    public void LegacyZeroVectorRemainsPresentAfterMigration()
    {
        using var directory = new MMapDirectory(_path);
        CreateLegacyIndex(directory, VectorQuantisation.None, true, false);
        string segmentId = SegmentId();
        string name = VectorName(segmentId, VectorQuantisation.None);
        byte[] body = ReadBody(directory, segmentId, name, VectorCodecFiles.Float32);
        body.AsSpan(9, 2 * sizeof(float)).Clear();
        using (var output = new IndexOutput(Path.Combine(_path, name)))
        {
            output.WriteByte(1);
            output.WriteBytes(body);
            output.WriteInt64(body.Length);
        }
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.True(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        using var searcher = new IndexSearcher(directory);
        var segment = Assert.Single(searcher.GetSegmentReaders());
        Assert.True(segment.HasVector("embedding", 0));
        Assert.False(segment.HasVector("embedding", 1));
        Assert.Equal(new float[2], segment.GetVector("embedding", 0));
        AssertSparseSearch(directory, [0, 2]);
    }

    private void CreateLegacyIndex(MMapDirectory directory, VectorQuantisation quantisation, bool hnsw, bool compound)
    {
        using (var writer = new IndexWriter(directory, Config(VectorQuantisation.None)))
        {
            AddSparseDocuments(writer);
            writer.Commit();
        }
        if (hnsw)
            HistoricalHnswGraphFixtures.AttachToFloat32Index(_path);

        string segmentId = SegmentId();
        var segment = Assert.Single(IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)!.SegmentInfos);
        if (quantisation == VectorQuantisation.Int8)
        {
            File.Delete(Path.Combine(_path, VectorFilePaths.VectorFile(segmentId, "embedding")));
            segment.VectorFields[0] = new VectorFieldInfo
            {
                FieldName = "embedding",
                Dimension = 2,
                Normalised = false,
                HasHnsw = hnsw,
                Quantisation = VectorQuantisation.Int8,
            };
        }
        string name = VectorName(segmentId, quantisation);
        // Construct the historical body independently of the current vector writer.
        using var bodyStream = new MemoryStream();
        using (var legacy = new BinaryWriter(bodyStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            legacy.Write(3);
            legacy.Write(2);
            legacy.Write((byte)quantisation);
            if (quantisation == VectorQuantisation.None)
                foreach (float value in new float[] { 1, 0, 0, 0, 0, 1 }) legacy.Write(value);
            else if (quantisation == VectorQuantisation.Int8)
            {
                legacy.Write(0f);
                legacy.Write(1f / 255f);
                foreach (float correction in new float[] { 0, 0, 0 }) legacy.Write(correction);
                legacy.Write(new byte[] { 255, 0, 0, 0, 0, 255 });
            }
            else
            {
                legacy.Write(0.5f);
                legacy.Write(0.5f);
                foreach (float correction in new float[] { 0, 1, 0.5f, -1, 1, 0.5f, 0, 1, 0.5f }) legacy.Write(correction);
                legacy.Write(new byte[] { 1, 0, 2 });
            }
        }
        byte[] legacyBody = bodyStream.ToArray();
        using (var output = new IndexOutput(Path.Combine(_path, name)))
        {
            output.WriteByte(1);
            output.WriteBytes(legacyBody);
            output.WriteInt64(legacyBody.Length);
        }
        if (compound)
        {
            Assert.True(SegmentFileSet.Pack(_path, segmentId));
            segment.IsCompoundFile = true;
        }
        SegmentFlusher.RefreshSegmentSize(segment, _path);
    }

    private static IndexWriterConfig Config(VectorQuantisation quantisation) => new()
    {
        NormaliseVectors = false, VectorQuantisation = quantisation, BuildHnswOnFlush = false,
        UseCompoundFile = false, HnswSeed = 42, IndexSort = new IndexSort(SortField.String("id"))
    };

    private static LeanDocument Document(int id, float[]? vector, int dimension = 2)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id.ToString(), stored: true));
        document.Add(new StringField("filter", "all"));
        if (vector is not null)
        {
            if (vector.Length > dimension)
                throw new ArgumentException("Vector dimension exceeds the selected sparse-test dimension.", nameof(vector));
            var expanded = new float[dimension];
            vector.CopyTo(expanded, 0);
            document.Add(new VectorField("embedding", expanded));
        }
        return document;
    }
    private static void AddSparseDocuments(IndexWriter writer, int dimension = 2)
    {
        writer.AddDocument(Document(0, [1, 0], dimension));
        writer.AddDocument(Document(1, null, dimension));
        writer.AddDocument(Document(2, [0, 1], dimension));
    }
    private static void AssertSparseSearch(MMapDirectory directory, int[] expected, int dimension = 2)
    {
        using var searcher = new IndexSearcher(directory);
        foreach (Query? filter in new Query?[] { null, new TermQuery("filter", "all") })
        {
            var query = new VectorQuery("embedding", BasisVector(dimension, 0), 10, oversamplingFactor: 2, filter: filter);
            var hits = searcher.Search(query, 10, TestContext.Current.CancellationToken);
            Assert.Equal(expected, hits.ScoreDocs.Select(hit => hit.DocId).Order().ToArray());
        }
    }

    private static (int DocId, float Score)[] CaptureSparseVectorScores(MMapDirectory directory)
    {
        using var searcher = new IndexSearcher(directory);
        var query = new VectorQuery("embedding", BasisVector(2, 0), 10, oversamplingFactor: 2);
        return searcher.Search(query, 10, TestContext.Current.CancellationToken).ScoreDocs
            .Select(static hit => (hit.DocId, hit.Score))
            .ToArray();
    }
    private static int VectorDimension(VectorQuantisation quantisation)
        => quantisation == VectorQuantisation.BBQ ? 128 : 2;

    private static float[] BasisVector(int dimension, int index)
    {
        var vector = new float[dimension];
        vector[index] = 1f;
        return vector;
    }
    private string SegmentId() => Assert.Single(IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)!.SegmentIds);
    private static CodecFileDescriptor Descriptor(VectorQuantisation quantisation) => quantisation == VectorQuantisation.None ? VectorCodecFiles.Float32 : VectorCodecFiles.Quantised;
    private static string VectorName(string segmentId, VectorQuantisation quantisation) => quantisation == VectorQuantisation.None
        ? VectorFilePaths.VectorFile(segmentId, "embedding") : VectorFilePaths.QuantisedVectorFile(segmentId, "embedding");
    private static byte[] ReadBody(MMapDirectory directory, string segmentId, string name, CodecFileDescriptor descriptor, int? expectedVersion = null)
    {
        using ISegmentFileSource source = File.Exists(Path.Combine(directory.DirectoryPath, segmentId + ".cfs"))
            ? new CompoundSegmentFileSource(directory, segmentId) : new LooseSegmentFileSource(directory, segmentId);
        using var input = source.OpenInput(name);
        using var frame = CodecFileReader.OpenSupported(input, descriptor);
        if (expectedVersion is int version) Assert.Equal(version, frame.FormatVersion);
        return frame.ReadBody();
    }
}
