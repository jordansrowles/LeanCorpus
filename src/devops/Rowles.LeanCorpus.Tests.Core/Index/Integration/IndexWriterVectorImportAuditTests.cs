using System.Text.Json;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Serialisation;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexWriterVectorImportAuditTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ll-vector-import-audit-{Guid.NewGuid():N}");

    public IndexWriterVectorImportAuditTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Rowles.LeanCorpus.Tests.Shared.Fixtures.TestDirectoryFixture.TryDeleteDirectory(_root);

    [Fact(DisplayName = "AddIndexes: Successful vector import registers target dimensions")]
    public void AddIndexes_SuccessfulVectorImportRegistersTargetDimensions()
    {
        string sourcePath = SubDir("successful_source");
        string targetPath = SubDir("successful_target");
        using var sourceDirectory = new MMapDirectory(sourcePath);
        CreateVectorIndex(sourcePath, [1f, 0f, 0f], "source vector");

        using var targetDirectory = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(targetDirectory, CreateWriterConfig());
        writer.AddIndexes(sourceDirectory);

        Assert.Throws<ArgumentException>(() => writer.AddDocument(CreateVectorDocument([1f, 0f], "wrong dimension")));
        writer.AddDocument(CreateVectorDocument([0f, 1f, 0f], "matching dimension"));
        writer.Commit();

        using var searcher = new IndexSearcher(targetDirectory);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        writer.Dispose();
        using var reopenedDirectory = new MMapDirectory(targetPath);
        using var reopened = new IndexWriter(reopenedDirectory, CreateWriterConfig());
        Assert.Throws<ArgumentException>(() => reopened.AddDocument(CreateVectorDocument([1f, 0f], "wrong after reopen")));
        reopened.AddDocument(CreateVectorDocument([0f, 0f, 1f], "matching after reopen"));
        reopened.Commit();
    }

    [Fact(DisplayName = "AddIndexes: Target vector mismatch is rejected before flushing pending documents")]
    public void AddIndexes_TargetVectorMismatchIsRejectedBeforeFlushingPendingDocuments()
    {
        string sourcePath = SubDir("target_mismatch_source");
        string targetPath = SubDir("target_mismatch_target");
        using var sourceDirectory = new MMapDirectory(sourcePath);
        CreateVectorIndex(sourcePath, [1f, 0f, 0f], "source dimension three");

        using var targetDirectory = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(targetDirectory, CreateWriterConfig());
        writer.AddDocument(CreateVectorDocument([1f, 0f], "pending dimension two"));
        string[] filesBeforeImport = targetDirectory.ListAll();

        Assert.Throws<ArgumentException>(() => writer.AddIndexes(sourceDirectory));

        Assert.Equal(
            filesBeforeImport.OrderBy(static name => name, StringComparer.Ordinal),
            targetDirectory.ListAll().OrderBy(static name => name, StringComparer.Ordinal));
        writer.Commit();

        using var searcher = new IndexSearcher(targetDirectory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "pending"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "source"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "AddIndexes: Mixed vector source is rejected before target flush or registry mutation")]
    public void AddIndexes_MixedVectorSourceIsRejectedBeforeTargetFlushOrRegistryMutation()
    {
        string mixedSourcePath = SubDir("mixed_source");
        string dimensionTwoPath = SubDir("mixed_dimension_two");
        string dimensionThreePath = SubDir("mixed_dimension_three");
        string targetPath = SubDir("mixed_target");
        CreateVectorIndex(dimensionTwoPath, [1f, 0f], "dimension two source");
        CreateVectorIndex(dimensionThreePath, [1f, 0f, 0f], "dimension three source");
        CreateMixedVectorIndex(mixedSourcePath, dimensionTwoPath, dimensionThreePath);

        using var mixedSourceDirectory = new MMapDirectory(mixedSourcePath);
        using var targetDirectory = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(targetDirectory, CreateWriterConfig());
        writer.AddDocument(CreateTextDocument("pending target document"));
        string[] filesBeforeImport = targetDirectory.ListAll();

        Assert.Throws<InvalidDataException>(() => writer.AddIndexes(mixedSourceDirectory));

        Assert.Equal(
            filesBeforeImport.OrderBy(static name => name, StringComparer.Ordinal),
            targetDirectory.ListAll().OrderBy(static name => name, StringComparer.Ordinal));
        writer.AddDocument(CreateVectorDocument([0f, 1f, 0f], "unpoisoned target registry"));
        writer.Commit();

        using var searcher = new IndexSearcher(targetDirectory);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "source"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact]
    public void AddIndexes_PhysicalImportFailureDoesNotRegisterSourceDimensions()
    {
        string sourcePath = SubDir("physical_failure_source");
        string targetPath = SubDir("physical_failure_target");
        CreateVectorIndex(sourcePath, [1f, 0f, 0f], "source");
        using var source = new MMapDirectory(sourcePath);
        using var target = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(target, CreateWriterConfig());
        Directory.CreateDirectory(Path.Combine(targetPath, "seg_0.seg"));
        var failure = Record.Exception(() => writer.AddIndexes(source));
        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString() ?? "Import unexpectedly succeeded.");
        writer.AddDocument(CreateVectorDocument([1f, 0f], "target dimension two"));
        writer.Commit();
        using var searcher = new IndexSearcher(new MMapDirectory(targetPath));
        Assert.Equal(1, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriterConfig CreateWriterConfig() => new()
    {
        MergePolicy = NoMergePolicy.Instance,
        MergeThreshold = 100,
        MaxBufferedDocs = 100,
        UseCompoundFile = false,
    };

    private static void CreateVectorIndex(string path, float[] vector, string body)
    {
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, CreateWriterConfig());
        writer.AddDocument(CreateVectorDocument(vector, body));
        writer.Commit();
    }

    internal static void CreateMixedVectorIndex(string destinationPath, string firstSourcePath, string secondSourcePath)
    {
        Directory.CreateDirectory(destinationPath);
        SegmentInfo first = CopySegmentAs(firstSourcePath, destinationPath, "seg_0");
        SegmentInfo second = CopySegmentAs(secondSourcePath, destinationPath, "seg_1");
        var commit = new CommitData
        {
            Generation = 1,
            ContentToken = 2,
            Segments = [first.SegmentId, second.SegmentId],
            SegmentStates =
            [
                SegmentCommitState.FromSegmentInfo(first),
                SegmentCommitState.FromSegmentInfo(second),
            ],
        };
        string json = JsonSerializer.Serialize(commit, LeanCorpusJsonContext.Default.CommitData);
        File.WriteAllText(Path.Combine(destinationPath, "segments_1"), CommitFileFormat.Wrap(json));
    }

    private static SegmentInfo CopySegmentAs(string sourcePath, string destinationPath, string destinationSegmentId)
    {
        const string sourceSegmentId = "seg_0";
        foreach (string sourceFile in Directory.GetFiles(sourcePath))
        {
            string fileName = Path.GetFileName(sourceFile);
            if (!SegmentFileSet.IsOwnedFileName(sourceSegmentId, fileName)
                || fileName.Equals(sourceSegmentId + ".seg", StringComparison.Ordinal))
            {
                continue;
            }

            string destinationName = destinationSegmentId + fileName[sourceSegmentId.Length..];
            File.Copy(sourceFile, Path.Combine(destinationPath, destinationName));
        }

        SegmentInfo source = SegmentInfo.ReadFrom(Path.Combine(sourcePath, sourceSegmentId + ".seg"));
        SegmentInfo copied = new()
        {
            SegmentId = destinationSegmentId,
            DocCount = source.DocCount,
            LiveDocCount = source.LiveDocCount,
            TotalBytes = source.TotalBytes,
            CodecBytes = new Dictionary<string, long>(source.CodecBytes, StringComparer.Ordinal),
            CommitGeneration = source.CommitGeneration,
            IsCompoundFile = source.IsCompoundFile,
            FieldNames = [.. source.FieldNames],
            IndexSortFields = source.IndexSortFields is null ? null : [.. source.IndexSortFields],
            VectorFields = source.VectorFields.Select(static field => new VectorFieldInfo
            {
                FieldName = field.FieldName,
                Dimension = field.Dimension,
                Normalised = field.Normalised,
                HasHnsw = field.HasHnsw,
                Quantisation = field.Quantisation,
            }).ToList(),
            SpatialFields = source.SpatialFields.Select(static field => new SpatialFieldInfo
            {
                FieldName = field.FieldName,
                Kind = field.Kind,
            }).ToList(),
            DelGeneration = source.DelGeneration,
            MinSequenceNumber = source.MinSequenceNumber,
            MaxSequenceNumber = source.MaxSequenceNumber,
            EarliestSoftDeleteTimestamp = source.EarliestSoftDeleteTimestamp,
        };
        copied.WriteTo(Path.Combine(destinationPath, destinationSegmentId + ".seg"));
        return copied;
    }

    private static LeanDocument CreateTextDocument(string body)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        return document;
    }

    private static LeanDocument CreateVectorDocument(float[] vector, string body)
    {
        var document = CreateTextDocument(body);
        document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
        return document;
    }
}
