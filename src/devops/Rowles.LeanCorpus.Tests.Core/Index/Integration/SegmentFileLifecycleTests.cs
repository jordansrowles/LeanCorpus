using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>Regression coverage for physical segment file ownership and deletion generations.</summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentFileLifecycleTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SegmentFileLifecycleTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Ownership: Matches Exact Segment File Boundaries")]
    public void Ownership_MatchesExactSegmentFileBoundaries()
    {
        var segmentIds = new HashSet<string>(["seg_1"], StringComparer.Ordinal);

        Assert.True(SegmentFileSet.IsOwnedByAnySegment("seg_1.seg", segmentIds));
        Assert.True(SegmentFileSet.IsOwnedByAnySegment("seg_1_v_embedding.hnsw", segmentIds));
        Assert.True(SegmentFileSet.IsOwnedByAnySegment("seg_1_gen_4.del", segmentIds));
        Assert.False(SegmentFileSet.IsOwnedByAnySegment("seg_10.seg", segmentIds));
        Assert.False(SegmentFileSet.IsOwnedByAnySegment("seg_1_extra.seg", segmentIds));
    }

    [Fact(DisplayName = "Merge: Removes All Consumed Loose Vector And HNSW Files")]
    public void Merge_RemovesAllConsumedLooseVectorAndHnswFiles()
    {
        string path = SubDir(nameof(Merge_RemovesAllConsumedLooseVectorAndHnswFiles));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 2,
            MergeThreshold = 100,
            BuildHnswOnFlush = true,
            UseCompoundFile = false,
        });

        for (int i = 0; i < 4; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", $"doc-{i}"));
            document.Add(new TextField("body", "merge vector lifecycle"));
            document.Add(new VectorField("embedding", new ReadOnlyMemory<float>([i + 1f, 0f, 0f])));
            writer.AddDocument(document);
        }
        writer.Commit();

        string[] sourceIds = Directory.GetFiles(path, "seg_*.seg")
            .Select(SegmentInfo.ReadFrom)
            .Select(static segment => segment.SegmentId)
            .ToArray();
        Assert.Equal(2, sourceIds.Length);
        var consumedFiles = sourceIds
            .SelectMany(segmentId => GetOwnedFilePaths(path, segmentId))
            .ToArray();
        Assert.Contains(consumedFiles, static file => file.EndsWith("_v_embedding.vec", StringComparison.Ordinal));
        Assert.Contains(consumedFiles, static file => file.EndsWith("_v_embedding.hnsw", StringComparison.Ordinal));

        Assert.True(writer.ForceMerge(1) > 0);

        Assert.All(consumedFiles, file => Assert.False(File.Exists(file), $"Consumed segment file remains: {file}"));
        using var searcher = new IndexSearcher(directory);
        Assert.Equal(4, searcher.Search(new TermQuery("body", "lifecycle"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Whole Segment Deletion: Removes Deletion Generations")]
    public void WholeSegmentDeletion_RemovesDeletionGenerations()
    {
        string path = SubDir(nameof(WholeSegmentDeletion_RemovesDeletionGenerations));
        const string segmentId = "seg_lifecycle_delete";
        string[] names =
        [
            segmentId + ".seg",
            segmentId + "_gen_7.del",
            segmentId + "_v_embedding.vec",
            segmentId + "_v_embedding.hnsw",
            "seg_lifecycle_delete_extra.seg",
        ];
        foreach (string name in names)
            File.WriteAllBytes(Path.Combine(path, name), [1]);

        using var directory = new MMapDirectory(path);
        CommitManager.DeleteSegmentFiles(segmentId, directory);

        Assert.All(names[..^1], name => Assert.False(File.Exists(Path.Combine(path, name)), name));
        Assert.True(File.Exists(Path.Combine(path, names[^1])));
    }

    [Fact(DisplayName = "Recovery: Removes Deletion Files For Orphan Segments")]
    public void Recovery_RemovesDeletionFilesForOrphanSegments()
    {
        string path = SubDir(nameof(Recovery_RemovesDeletionFilesForOrphanSegments));
        using (var initialDirectory = new MMapDirectory(path))
        using (var writer = new IndexWriter(initialDirectory, new IndexWriterConfig()))
        {
            writer.AddDocument(CreateDocument("active"));
            writer.Commit();
        }

        const string orphanId = "orphan_77";
        string[] orphanFiles =
        [
            orphanId + ".seg",
            orphanId + "_gen_9.del",
            orphanId + "_v_embedding.vec",
            orphanId + "_v_embedding.hnsw",
        ];
        foreach (string name in orphanFiles)
            File.WriteAllBytes(Path.Combine(path, name), [1]);

        using (var recoveryDirectory = new MMapDirectory(path))
        using (var recoveredWriter = new IndexWriter(recoveryDirectory, new IndexWriterConfig()))
            Assert.Single(recoveredWriter.GetNrtSegments());

        Assert.All(orphanFiles, name => Assert.False(File.Exists(Path.Combine(path, name)), name));
    }

    [Fact(DisplayName = "Commit: Prunes Unprotected Deletion Generations")]
    public void Commit_PrunesUnprotectedDeletionGenerations()
    {
        string path = SubDir(nameof(Commit_PrunesUnprotectedDeletionGenerations));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 100,
            MergeThreshold = 100,
            DeletionPolicy = new KeepLastNCommitsPolicy(5),
        });
        writer.AddDocument(CreateDocument("first"));
        writer.AddDocument(CreateDocument("second"));
        writer.AddDocument(CreateDocument("third"));
        writer.Commit();

        writer.DeleteDocuments(new TermQuery("id", "first"));
        writer.Commit();
        writer.DeleteDocuments(new TermQuery("id", "second"));
        writer.Commit();

        SegmentInfo currentSegment = ReadOnlySegment(path);
        Assert.NotNull(currentSegment.DelGeneration);
        string currentDeletionFile = Path.Combine(path, $"{currentSegment.SegmentId}_gen_{currentSegment.DelGeneration}.del");
        Assert.True(File.Exists(currentDeletionFile));
        Assert.Equal([currentDeletionFile], GetDeletionGenerationPaths(path, currentSegment.SegmentId));
    }

    [Fact(DisplayName = "Held Snapshot: Preserves And Releases Its Exact Deletion Generation")]
    public void HeldSnapshot_PreservesAndReleasesItsExactDeletionGeneration()
    {
        string path = SubDir(nameof(HeldSnapshot_PreservesAndReleasesItsExactDeletionGeneration));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 100,
            MergeThreshold = 100,
            DeletionPolicy = new KeepLastNCommitsPolicy(5),
        });
        writer.AddDocument(CreateDocument("first"));
        writer.AddDocument(CreateDocument("second"));
        writer.AddDocument(CreateDocument("third"));
        writer.Commit();

        writer.DeleteDocuments(new TermQuery("id", "first"));
        writer.Commit();
        var snapshot = writer.CreateSnapshot();
        Assert.NotNull(Assert.Single(snapshot.Segments).DelGeneration);
        int protectedGeneration = snapshot.Segments[0].DelGeneration!.Value;
        string protectedFile = Path.Combine(path, $"{snapshot.Segments[0].SegmentId}_gen_{protectedGeneration}.del");

        writer.DeleteDocuments(new TermQuery("id", "second"));
        writer.Commit();
        Assert.True(File.Exists(protectedFile));

        using (var snapshotSearcher = new IndexSearcher(directory, snapshot.Segments))
        {
            Assert.Equal(0, snapshotSearcher.Search(new TermQuery("id", "first"), 10, TestContext.Current.CancellationToken).TotalHits);
            Assert.Equal(1, snapshotSearcher.Search(new TermQuery("id", "second"), 10, TestContext.Current.CancellationToken).TotalHits);
        }
        using (var currentSearcher = new IndexSearcher(directory))
            Assert.Equal(0, currentSearcher.Search(new TermQuery("id", "second"), 10, TestContext.Current.CancellationToken).TotalHits);

        writer.ReleaseSnapshot(snapshot);
        Assert.False(File.Exists(protectedFile));
        SegmentInfo currentSegment = ReadOnlySegment(path);
        Assert.Equal([$"{currentSegment.SegmentId}_gen_{currentSegment.DelGeneration}.del"],
            GetDeletionGenerationPaths(path, currentSegment.SegmentId).Select(Path.GetFileName));
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument CreateDocument(string id)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", id));
        return document;
    }

    private static IEnumerable<string> GetOwnedFilePaths(string directoryPath, string segmentId)
        => Directory.GetFiles(directoryPath)
            .Where(path => IsOwnedFileName(Path.GetFileName(path), segmentId));

    private static IEnumerable<string> GetDeletionGenerationPaths(string directoryPath, string segmentId)
        => Directory.GetFiles(directoryPath, segmentId + "_gen_*.del")
            .OrderBy(static path => path, StringComparer.Ordinal);

    private static bool IsOwnedFileName(string fileName, string segmentId)
        => fileName.StartsWith(segmentId + ".", StringComparison.Ordinal)
            || fileName.StartsWith(segmentId + "_v_", StringComparison.Ordinal)
            || fileName.StartsWith(segmentId + "_gen_", StringComparison.Ordinal);

    private static SegmentInfo ReadOnlySegment(string directoryPath)
        => SegmentInfo.ReadFrom(Directory.GetFiles(directoryPath, "seg_*.seg").Single());
}
