using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Diagnostics;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Regression tests for C5: durable atomic commit.
/// Verifies that <see cref="IndexWriterConfig.DurableCommits"/> ensures committed
/// data round-trips intact through a writer restart, and that disabling the flag
/// does not regress correctness for the happy path.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public class CommitDurabilityTests : IDisposable
{
    private readonly string _dir;

    public CommitDurabilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ll_durable_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    /// <summary>
    /// Verifies the Durable Commits: Defaults To True scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commits: Defaults To True")]
    public void DurableCommits_DefaultsToTrue()
    {
        Assert.True(new IndexWriterConfig().DurableCommits);
    }

    /// <summary>
    /// Verifies the Durable Commit: Round-trip Preserves All Documents scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commit: Round-trip Preserves All Documents")]
    public void DurableCommit_RoundTrip_PreservesAllDocuments()
    {
        // Arrange — write three commits with durability ON
        var config = new IndexWriterConfig { DurableCommits = true };
        using (var writer = new IndexWriter(new MMapDirectory(_dir), config))
        {
            for (int i = 0; i < 3; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"payload number {i}"));
                writer.AddDocument(doc);
                writer.Commit();
            }
        }

        // Act — re-open and search
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        // Assert — every committed document survives the writer restart
        for (int i = 0; i < 3; i++)
        {
            var results = searcher.Search(new TermQuery("body", $"{i}"), 10);
            Assert.Equal(1, results.TotalHits);
        }
    }

    /// <summary>
    /// Verifies the Durable Commit: All Referenced Segment Files Present After Dispose scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commit: All Referenced Segment Files Present After Dispose")]
    public void DurableCommit_AllReferencedSegmentFilesPresentAfterDispose()
    {
        var config = new IndexWriterConfig { DurableCommits = true };
        using (var writer = new IndexWriter(new MMapDirectory(_dir), config))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"durable {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var segmentsFile = Path.Combine(_dir, "segments_1");
        AssertNonEmptyFile(segmentsFile);
        AssertNonEmptyFile(Path.Combine(_dir, "stats_1.json"));

        var json = Rowles.LeanCorpus.Index.CommitFileFormat.ReadJson(segmentsFile);
        var commit = JsonSerializer.Deserialize<JsonElement>(json);
        var segments = commit.GetProperty("Segments");
        Assert.NotEmpty(segments.EnumerateArray());

        foreach (var segmentElement in segments.EnumerateArray())
        {
            var segmentId = segmentElement.GetString()!;
            foreach (var extension in new[] { ".seg", ".dic", ".pos", ".nrm", ".fdt", ".fdx", ".fln", ".stats.json" })
                AssertNonEmptyFile(Path.Combine(_dir, segmentId + extension));
        }
    }

    /// <summary>
    /// Verifies the Durable Commits Disabled: Still Works scenario.
    /// </summary>
    [Fact(DisplayName = "Durable Commits Disabled: Still Works")]
    public void DurableCommitsDisabled_StillWorks()
    {
        // Arrange — ensure the opt-out path remains functional
        var config = new IndexWriterConfig { DurableCommits = false };
        using (var writer = new IndexWriter(new MMapDirectory(_dir), config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "non-durable but valid"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.Search(new TermQuery("body", "valid"), 10);
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies that a later metadata-only commit does not resynchronise unchanged segment files.
    /// This protects issue #59's dirty-file contract from regressing to directory enumeration.
    /// </summary>
    [Fact(DisplayName = "Durable Commit: Synchronises Only Files Changed Since Prior Commit")]
    public void DurableCommit_SynchronisesOnlyFilesChangedSincePriorCommit()
    {
        var metrics = new DefaultMetricsCollector();
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig
        {
            DurableCommits = true,
            Metrics = metrics
        });
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "dirty file tracking"));
        writer.AddDocument(doc);
        writer.Commit();
        long firstFileCount = metrics.GetSnapshot().FileSyncFileCount;

        writer.Commit();

        var snapshot = metrics.GetSnapshot();
        Assert.True(firstFileCount > 1);
        Assert.Equal(1, snapshot.FileSyncFileCount - firstFileCount);
    }

    /// <summary>
    /// Verifies that the first durable commit after a process restart synchronises every
    /// inherited segment file before the commit publication barrier. The tracker reset models
    /// a fresh process whose in-memory dirty state cannot contain the earlier writer's files.
    /// </summary>
    [Theory(DisplayName = "Durable Commit: Restart Establishes Referenced File Baseline")]
    [InlineData(false)]
    [InlineData(true)]
    public void DurableCommit_Restart_EstablishesReferencedFileBaseline(bool useCompoundFile)
    {
        using (var firstWriter = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig
        {
            DurableCommits = false,
            UseCompoundFile = useCompoundFile
        }))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "restart durability baseline"));
            firstWriter.AddDocument(document);
            firstWriter.Commit();
        }

        string[] inheritedSegmentFiles = Directory.EnumerateFiles(_dir)
            .Where(path => Path.GetFileName(path).StartsWith("seg_", StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .ToArray();
        Assert.NotEmpty(inheritedSegmentFiles);

        DirtyFileTracker.ForgetDirectory(_dir);
        var fileSystem = new RecordingFileSystem();
        using (PlatformFileSystem.OverrideForTesting(fileSystem))
        using (var secondWriter = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig
        {
            DurableCommits = true,
            UseCompoundFile = useCompoundFile
        }))
        {
            secondWriter.Commit();

            int firstDirectoryBarrier = fileSystem.Operations.FindIndex(static operation =>
                operation.StartsWith("directory:", StringComparison.Ordinal));
            Assert.True(firstDirectoryBarrier >= 0);
            foreach (string inheritedFile in inheritedSegmentFiles)
            {
                int fileSync = fileSystem.Operations.IndexOf("file:" + inheritedFile);
                Assert.InRange(fileSync, 0, firstDirectoryBarrier - 1);
            }

            fileSystem.Operations.Clear();
            secondWriter.Commit();
            Assert.DoesNotContain(fileSystem.Operations, operation =>
                operation.StartsWith("file:", StringComparison.Ordinal) &&
                inheritedSegmentFiles.Contains(operation["file:".Length..], StringComparer.Ordinal));
        }
    }

    /// <summary>Verifies that reopening a generation already made durable by this process does not baseline it again.</summary>
    [Fact(DisplayName = "Durable Commit: Same-Process Reopen Reuses Established Baseline")]
    public void DurableCommit_SameProcessReopen_ReusesEstablishedBaseline()
    {
        var fileSystem = new RecordingFileSystem();
        using (PlatformFileSystem.OverrideForTesting(fileSystem))
        {
            using (var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig { DurableCommits = true }))
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "known durable generation"));
                writer.AddDocument(document);
                writer.Commit();
            }

            string[] inheritedSegmentFiles = Directory.EnumerateFiles(_dir)
                .Where(path => Path.GetFileName(path).StartsWith("seg_", StringComparison.Ordinal))
                .Select(Path.GetFullPath)
                .ToArray();
            fileSystem.Operations.Clear();

            using var reopened = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig { DurableCommits = true });
            reopened.Commit();

            Assert.DoesNotContain(fileSystem.Operations, operation =>
                operation.StartsWith("file:", StringComparison.Ordinal) &&
                inheritedSegmentFiles.Contains(operation["file:".Length..], StringComparer.Ordinal));
        }
    }

    private sealed class RecordingFileSystem : IPlatformFileSystem
    {
        internal List<string> Operations { get; } = [];

        public void SyncFile(string path) => Operations.Add("file:" + Path.GetFullPath(path));

        public DirectorySyncResult SyncDirectory(string path)
        {
            Operations.Add("directory:" + Path.GetFullPath(path));
            return DirectorySyncResult.Succeeded;
        }

        public bool IsTransient(Exception exception) => false;
    }

    private static void AssertNonEmptyFile(string path)
    {
        Assert.True(File.Exists(path), $"Expected {path} to exist");
        Assert.True(new FileInfo(path).Length > 0, $"Expected {path} to be non-empty");
    }
}
