using System.Security.Cryptography;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.CodecKit)]
[Area(TestArea.Search)]
public sealed class BbqV3LifecycleDurabilityTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public BbqV3LifecycleDurabilityTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Historical BBQ framing refusal and partial graph writes preserve the committed segment")]
    public void PartialVqAndHnswWritesLeaveTheCommittedIndexReadable()
    {
        string indexPath = Path.Combine(_fixture.Path, $"bbq-v3-atomic-{Guid.NewGuid():N}");
        CreateIndex(indexPath, useCompoundFile: false);
        long generation = IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.Generation;
        byte[] commitBefore = File.ReadAllBytes(Path.Combine(indexPath, $"segments_{generation}"));
        SegmentInfo segment = IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos
            .First(static info => info.VectorFields.Any(static field => field.FieldName == "embedding"));
        (string FileName, CodecFileDescriptor Descriptor)[] files =
        [
            (VectorFilePaths.QuantisedVectorFile(Path.Combine(indexPath, segment.SegmentId), "embedding"), VectorCodecFiles.Quantised),
            (VectorFilePaths.HnswFile(Path.Combine(indexPath, segment.SegmentId), "embedding"), VectorCodecFiles.Hnsw),
        ];

        foreach ((string filePath, CodecFileDescriptor descriptor) in files)
        {
            byte[] original = File.ReadAllBytes(filePath);
            if (descriptor == VectorCodecFiles.Quantised)
            {
                bool bodyCalled = false;
                var error = Assert.Throws<InvalidOperationException>(() => CodecFileWriter.WriteAtomically(
                    filePath, descriptor, VectorCodecFiles.QuantisedIdentity(QuantisedVectorStorageKind.BbqOsqCanonicalInt8),
                    durable: false, _ => bodyCalled = true));
                Assert.Contains("CodecReadOnly", error.Message, StringComparison.Ordinal);
                Assert.False(bodyCalled);
                Assert.Equal(original, File.ReadAllBytes(filePath));
                continue;
            }
            IOException injected = Assert.Throws<IOException>(() => CodecFileWriter.WriteAtomically(
                filePath,
                descriptor,
                durable: false,
                static body =>
                {
                    body.WriteByte(0xA5);
                    throw new IOException("injected partial BBQ codec write");
                }));

            Assert.Equal("injected partial BBQ codec write", injected.Message);
            Assert.Equal(original, File.ReadAllBytes(filePath));
            Assert.Empty(Directory.GetFiles(indexPath, Path.GetFileName(filePath) + ".*.codec.tmp"));
            Assert.Equal(commitBefore, File.ReadAllBytes(Path.Combine(indexPath, $"segments_{generation}")));
        }

        using var directory = new MMapDirectory(indexPath);
        IndexCheckResult health = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(health.IsHealthy, string.Join("; ", health.DetailedIssues.Select(static issue => issue.Message)));
        using var searcher = new IndexSearcher(directory);
        Assert.Equal(6, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.NotEmpty(searcher.Search(
            new VectorQuery("embedding", CreateVector(2), topK: 4, efSearch: 32),
            4,
            TestContext.Current.CancellationToken).ScoreDocs);
    }

    [Fact(DisplayName = "Historical BBQ backup, refused merge and parallel pinned search preserve canonical records")]
    public async Task BackupRestoreMergeAndPinnedParallelSearchPreserveCanonicalRecords()
    {
        string sourcePath = Path.Combine(_fixture.Path, $"bbq-v3-backup-source-{Guid.NewGuid():N}");
        string backupPath = Path.Combine(_fixture.Path, $"bbq-v3-backup-{Guid.NewGuid():N}");
        string restorePath = Path.Combine(_fixture.Path, $"bbq-v3-restore-{Guid.NewGuid():N}");
        CreateIndex(sourcePath, useCompoundFile: true);
        Dictionary<string, string> expectedHashes;
        using (var source = new MMapDirectory(sourcePath))
        {
            expectedHashes = ReadCanonicalHashes(source);
            Assert.Equal(4, expectedHashes.Count);
            IndexBackup.Backup(sourcePath, backupPath, cancellationToken: TestContext.Current.CancellationToken);
        }

        IndexBackup.Restore(backupPath, restorePath, cancellationToken: TestContext.Current.CancellationToken);
        using var pinnedDirectory = new MMapDirectory(restorePath);
        var pinnedSearcher = new IndexSearcher(pinnedDirectory);
        try
        {
            string[] originalFiles = pinnedSearcher.GetSegmentReaders()
                .Select(static reader => reader.Info.SegmentId + ".cfs")
                .ToArray();
            string initialFingerprint = SearchFingerprint(pinnedSearcher, TestContext.Current.CancellationToken);
            Assert.Equal(expectedHashes, ReadCanonicalHashes(pinnedDirectory));

            using var mergeStarted = new ManualResetEventSlim();
            var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int started = 0;
            Task[] parallelSearches = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                if (Interlocked.Increment(ref started) == 4)
                    allStarted.TrySetResult();
                mergeStarted.Wait(TestContext.Current.CancellationToken);
                for (int iteration = 0; iteration < 64; iteration++)
                    Assert.Equal(initialFingerprint, SearchFingerprint(pinnedSearcher, TestContext.Current.CancellationToken));
            }, TestContext.Current.CancellationToken)).ToArray();
            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

            mergeStarted.Set();
            using (var mergeDirectory = new MMapDirectory(restorePath))
                Assert.Throws<InvalidOperationException>(() =>
                    new IndexWriter(mergeDirectory, CreateWriterConfig(useCompoundFile: false)));
            await Task.WhenAll(parallelSearches);

            Assert.Equal(initialFingerprint, SearchFingerprint(pinnedSearcher, TestContext.Current.CancellationToken));
            Assert.All(originalFiles, fileName => Assert.True(File.Exists(Path.Combine(restorePath, fileName))));
        }
        finally
        {
            pinnedSearcher.Dispose();
        }

        using var mergedDirectory = new MMapDirectory(restorePath);
        Assert.Equal(expectedHashes, ReadCanonicalHashes(mergedDirectory));
        var preserved = IndexRecovery.RecoverLatestCommit(restorePath, cleanupOrphans: false)!.SegmentInfos;
        Assert.All(preserved, segment => Assert.True(segment.IsCompoundFile));
        IndexCheckResult health = IndexValidator.Check(mergedDirectory, new IndexCheckOptions { Deep = true });
        Assert.True(health.IsHealthy, string.Join("; ", health.DetailedIssues.Select(static issue => issue.Message)));
        using var mergedSearcher = new IndexSearcher(mergedDirectory);
        Assert.Equal(6, mergedSearcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Historical BBQ mapped files survive refused merge and release their leases on Windows")]
    public void WindowsMappedBbqFilesRemainLeasedUntilThePinnedSearcherIsReleased()
    {
        if (!OperatingSystem.IsWindows())
            return;

        string indexPath = Path.Combine(_fixture.Path, $"bbq-v3-windows-lifetime-{Guid.NewGuid():N}");
        CreateIndex(indexPath, useCompoundFile: false);
        using var pinnedDirectory = new MMapDirectory(indexPath);
        var pinnedSearcher = new IndexSearcher(pinnedDirectory);
        string[] sourceSegments = pinnedSearcher.GetSegmentReaders().Select(static reader => reader.Info.SegmentId).ToArray();
        string[] sourceFiles = sourceSegments
            .SelectMany(segmentId => new[]
            {
                VectorFilePaths.QuantisedVectorFile(Path.Combine(indexPath, segmentId), "embedding"),
                VectorFilePaths.HnswFile(Path.Combine(indexPath, segmentId), "embedding"),
            })
            .Where(File.Exists)
            .ToArray();
        Assert.NotEmpty(sourceFiles);
        string expectedSearch = SearchFingerprint(pinnedSearcher, TestContext.Current.CancellationToken);

        try
        {
            using (var mergeDirectory = new MMapDirectory(indexPath))
                Assert.Throws<InvalidOperationException>(() =>
                    new IndexWriter(mergeDirectory, CreateWriterConfig(useCompoundFile: false)));

            Assert.All(sourceFiles, filePath => Assert.True(File.Exists(filePath), $"Mapped source file was removed before lease release: {filePath}"));
            Assert.Equal(expectedSearch, SearchFingerprint(pinnedSearcher, TestContext.Current.CancellationToken));
        }
        finally
        {
            pinnedSearcher.Dispose();
        }
        Assert.All(sourceFiles, filePath => Assert.True(File.Exists(filePath), $"Read-only source file was removed after lease release: {filePath}"));
        pinnedDirectory.Dispose();
        foreach (string file in sourceFiles)
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    private static void CreateIndex(string path, bool useCompoundFile)
    {
        HistoricalBbqIndexFixtures.CopyTo(path, useCompoundFile ? "lifecycle-compound" : "lifecycle-loose");
    }

    private static IndexWriterConfig CreateWriterConfig(bool useCompoundFile) => new()
    {
        MaxBufferedDocs = 3,
        MergePolicy = NoMergePolicy.Instance,
        MergeThreshold = 100,
        UseCompoundFile = useCompoundFile,
        BuildHnswOnFlush = true,
        HnswSeed = 42,
        NormaliseVectors = true,
        VectorQuantisation = VectorQuantisation.Int8,
    };

    private static float[] CreateVector(int id)
    {
        var vector = new float[128];
        if (id % 4 == 0)
            return vector;
        for (int index = 0; index < vector.Length; index++)
            vector[index] = ((index * 31 + id * 17) % 9) - 4;
        return vector;
    }

    private static Dictionary<string, string> ReadCanonicalHashes(MMapDirectory directory)
    {
        using var searcher = new IndexSearcher(directory);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SegmentReader segment in searcher.GetSegmentReaders())
        {
            if (!segment.Info.VectorFields.Any(static field => field.FieldName == "embedding"))
                continue;
            using var vectors = QuantisedVectorReader.Open(
                segment.OpenInput(VectorFilePaths.QuantisedVectorFile(string.Empty, "embedding")));
            Assert.Equal(QuantisedVectorStorageKind.BbqOsqCanonicalInt8, vectors.StorageKind);
            byte[] record = new byte[CanonicalInt8VectorMetadata.Size + vectors.Dimension];
            for (int docId = 0; docId < segment.MaxDoc; docId++)
            {
                if (!segment.IsLive(docId) || !vectors.HasVector(docId))
                    continue;
                string id = Assert.Single(segment.GetStoredFields(docId)["id"]);
                vectors.CopyCanonicalRecordTo(docId, record);
                hashes.Add(id, Convert.ToHexString(SHA256.HashData(record)));
            }
        }
        return hashes;
    }

    private static string SearchFingerprint(IndexSearcher searcher, CancellationToken cancellationToken)
        => string.Join(",", searcher.Search(
                new VectorQuery("embedding", CreateVector(2), topK: 4, efSearch: 32),
                4,
                cancellationToken)
            .ScoreDocs.Select(static hit => $"{hit.DocId}:{BitConverter.SingleToInt32Bits(hit.Score)}"));
}
