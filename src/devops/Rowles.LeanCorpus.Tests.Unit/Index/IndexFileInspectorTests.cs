using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Serialization;

namespace Rowles.LeanCorpus.Tests.Unit.Index;

[Trait("Category", "UnitTest")]
public sealed class IndexFileInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-ifi-{Guid.NewGuid():N}");

    public IndexFileInspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // -------------------------------------------------------------------------
    // FindCommitFiles
    // -------------------------------------------------------------------------

    [Fact]
    public void FindCommitFiles_NonExistentDirectory_ReturnsEmptyList()
    {
        var missing = Path.Combine(_dir, "does_not_exist");

        var result = IndexFileInspector.FindCommitFiles(missing);

        Assert.Empty(result);
    }

    [Fact]
    public void FindCommitFiles_EmptyDirectory_ReturnsEmptyList()
    {
        var result = IndexFileInspector.FindCommitFiles(_dir);

        Assert.Empty(result);
    }

    [Fact]
    public void FindCommitFiles_TmpFilesIgnored_ReturnsOnlyParseable()
    {
        File.WriteAllText(Path.Combine(_dir, "segments_1"), "x");
        File.WriteAllText(Path.Combine(_dir, "segments_2.tmp"), "x");
        File.WriteAllText(Path.Combine(_dir, "segments_abc"), "x");

        var result = IndexFileInspector.FindCommitFiles(_dir);

        Assert.Single(result);
        Assert.Equal(1, result[0].Generation);
    }

    [Fact]
    public void FindCommitFiles_MultipleGenerations_ReturnsSortedDescending()
    {
        File.WriteAllText(Path.Combine(_dir, "segments_1"), "x");
        File.WriteAllText(Path.Combine(_dir, "segments_3"), "x");
        File.WriteAllText(Path.Combine(_dir, "segments_2"), "x");

        var result = IndexFileInspector.FindCommitFiles(_dir);

        Assert.Equal(3, result.Count);
        Assert.Equal([3, 2, 1], result.Select(static r => r.Generation));
    }

    // -------------------------------------------------------------------------
    // TryReadCommit
    // -------------------------------------------------------------------------

    [Fact]
    public void TryReadCommit_IOException_AddsCommitUnreadableIssue()
    {
        var filePath = Path.Combine(_dir, "segments_1");
        File.WriteAllText(filePath, "placeholder");

        // Lock the file so File.ReadAllText throws IOException.
        using var lockHandle = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = new IndexCheckResult();

        var commit = IndexFileInspector.TryReadCommit(filePath, 1, result);

        Assert.Null(commit);
        Assert.Single(result.DetailedIssues);
        Assert.Equal(IndexCheckIssueCodes.CommitUnreadable, result.DetailedIssues[0].Code);
    }

    [Fact]
    public void TryReadCommit_BadCrc_AddsCommitCrcMismatchIssue()
    {
        var filePath = Path.Combine(_dir, "segments_1");
        // Write a file whose CRC trailer is present but incorrect.
        File.WriteAllText(filePath, "{}\n#crc32=deadbeef\n");
        var result = new IndexCheckResult();

        var commit = IndexFileInspector.TryReadCommit(filePath, 1, result);

        Assert.Null(commit);
        Assert.Single(result.DetailedIssues);
        Assert.Equal(IndexCheckIssueCodes.CommitCrcMismatch, result.DetailedIssues[0].Code);
    }

    [Fact]
    public void TryReadCommit_NullDeserialise_AddsCommitInvalidJsonIssue()
    {
        var filePath = Path.Combine(_dir, "segments_1");
        // "null" with a valid CRC deserialises to null for a reference type.
        File.WriteAllText(filePath, CommitFileFormat.Wrap("null"));
        var result = new IndexCheckResult();

        var commit = IndexFileInspector.TryReadCommit(filePath, 1, result);

        Assert.Null(commit);
        Assert.Single(result.DetailedIssues);
        Assert.Equal(IndexCheckIssueCodes.CommitInvalidJson, result.DetailedIssues[0].Code);
    }

    [Fact]
    public void TryReadCommit_GenerationMismatch_AddsGenerationMismatchIssue()
    {
        var filePath = Path.Combine(_dir, "segments_1");
        var commitData = new CommitData { Generation = 5, Segments = [], ContentToken = 0 };
        var json = System.Text.Json.JsonSerializer.Serialize(commitData, LeanCorpusJsonContext.Default.CommitData);
        File.WriteAllText(filePath, CommitFileFormat.Wrap(json));
        var result = new IndexCheckResult();

        // File says generation 5, caller expects generation 1.
        var commit = IndexFileInspector.TryReadCommit(filePath, 1, result);

        Assert.Null(commit);
        Assert.Single(result.DetailedIssues);
        Assert.Equal(IndexCheckIssueCodes.CommitGenerationMismatch, result.DetailedIssues[0].Code);
    }

    // -------------------------------------------------------------------------
    // CheckCodecHeader
    // -------------------------------------------------------------------------

    [Fact]
    public void CheckCodecHeader_FileLocked_AddsInvalidCodecMagicIssue()
    {
        var filePath = Path.Combine(_dir, "seg_0.dic");
        // Write a minimal CodecKit file with version then truncate (no VarInt body length).
        using (var stream = File.Create(filePath))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            writer.Write((byte)1); // version
            // Intentionally truncated: no VarInt body length bytes
        }

        var result = new IndexCheckResult();

        // Lock the file to force an IOException when CheckCodecHeader opens it.
        using var lockHandle = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        IndexFileInspector.CheckCodecHeader(
            filePath,
            CodecConstants.TermDictionaryVersion,
            CodecFormats.TermDictionary,
            "term-dictionary",
            "seg_0",
            result);

        Assert.Single(result.DetailedIssues);
        Assert.Equal(IndexCheckIssueCodes.InvalidCodecMagic, result.DetailedIssues[0].Code);
    }
}
