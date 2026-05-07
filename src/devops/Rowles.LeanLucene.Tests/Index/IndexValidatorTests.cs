using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Index.Segment;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Contains unit tests for Index Validator.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexValidatorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    public IndexValidatorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Verifies the Validate: Empty Directory Reports No Commit File scenario.
    /// </summary>
    [Fact(DisplayName = "Validate: Empty Directory Reports No Commit File")]
    public void Validate_EmptyDirectory_ReportsNoCommitFile()
    {
        var dir = new MMapDirectory(SubDir("val_empty"));
        var result = IndexValidator.Validate(dir);
        Assert.False(result.IsHealthy);
        Assert.Single(result.Issues);
    }

    /// <summary>
    /// Verifies the Validate: Valid Index Is Healthy scenario.
    /// </summary>
    [Fact(DisplayName = "Validate: Valid Index Is Healthy")]
    public void Validate_ValidIndex_IsHealthy()
    {
        var dir = new MMapDirectory(SubDir("val_valid"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        doc.Add(new StringField("id", "1"));
        writer.AddDocument(doc);
        writer.Commit();

        var result = IndexValidator.Validate(dir);
        Assert.True(result.IsHealthy, string.Join(", ", result.Issues));
        Assert.Equal(1, result.SegmentsChecked);
        Assert.True(result.DocumentsChecked >= 1);
    }

    /// <summary>
    /// Verifies the Validate: Multiple Segments Checks All scenario.
    /// </summary>
    [Fact(DisplayName = "Validate: Multiple Segments Checks All")]
    public void Validate_MultipleSegments_ChecksAll()
    {
        var dir = new MMapDirectory(SubDir("val_multiseg"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document {i}"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        var result = IndexValidator.Validate(dir);
        Assert.True(result.IsHealthy, string.Join(", ", result.Issues));
    }

    /// <summary>
    /// Verifies the Validate: Missing Segment File Reports Issue scenario.
    /// </summary>
    [Fact(DisplayName = "Validate: Missing Segment File Reports Issue")]
    public void Validate_MissingSegmentFile_ReportsIssue()
    {
        var dir = new MMapDirectory(SubDir("val_missing"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        // Delete one of the required segment files
        var dicFiles = Directory.GetFiles(dir.DirectoryPath, "*.dic");
        if (dicFiles.Length > 0)
            File.Delete(dicFiles[0]);

        var result = IndexValidator.Validate(dir);
        Assert.False(result.IsHealthy);
    }

    /// <summary>
    /// Verifies that a commit file with invalid JSON reports an issue.
    /// </summary>
    [Fact(DisplayName = "Validate: Invalid JSON In Commit Reports Issue")]
    public void Validate_InvalidJsonInCommit_ReportsIssue()
    {
        var path = SubDir("val_badjson");
        File.WriteAllText(System.IO.Path.Combine(path, "segments_1"), "not valid json at all");
        var dir = new MMapDirectory(path);
        var result = IndexValidator.Validate(dir);
        Assert.False(result.IsHealthy);
        Assert.Single(result.Issues);
    }

    /// <summary>
    /// Verifies that the validator picks the commit with the highest generation number.
    /// </summary>
    [Fact(DisplayName = "Validate: Picks Highest Generation Commit")]
    public void Validate_PicksHighestGenerationCommit()
    {
        var path = SubDir("val_gen");
        // segments_1: bad JSON (low gen)
        File.WriteAllText(System.IO.Path.Combine(path, "segments_1"), "not json");
        // segments_5: valid JSON with zero segments (high gen) — healthy
        var json5 = "{\"Segments\":[],\"Generation\":5}";
        var wrapped5 = CommitFileFormat.Wrap(json5);
        File.WriteAllText(System.IO.Path.Combine(path, "segments_5"), wrapped5);

        var dir = new MMapDirectory(path);
        var result = IndexValidator.Validate(dir);
        Assert.True(result.IsHealthy);
        Assert.Equal(0, result.SegmentsChecked);
    }

    /// <summary>
    /// Verifies that an empty .fdx file is reported as an issue.
    /// </summary>
    [Fact(DisplayName = "Validate: Empty Fdx Reported As Issue")]
    public void Validate_EmptyFdx_ReportsIssue()
    {
        var path = SubDir("val_fdx");
        const string segId = "seg_fdxtest";
        var segJson = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(System.IO.Path.Combine(path, "segments_1"), CommitFileFormat.Wrap(segJson));

        foreach (var ext in new[] { ".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm" })
            File.WriteAllBytes(System.IO.Path.Combine(path, segId + ext), []);

        var dir = new MMapDirectory(path);
        var result = IndexValidator.Validate(dir);
        Assert.False(result.IsHealthy);
        Assert.True(result.Issues.Any(i => i.Contains(".fdx") || i.Contains(".seg")));
    }

    /// <summary>
    /// Verifies that a .nrm file smaller than 4 bytes is reported as an issue.
    /// </summary>
    [Fact(DisplayName = "Validate: Nrm Smaller Than 4 Bytes Reported As Issue")]
    public void Validate_SmallNrm_ReportsIssue()
    {
        var path = SubDir("val_nrm");
        const string segId = "seg_nrmtest";

        // Write a valid .seg file so the validator proceeds past the metadata read
        var segInfo = new SegmentInfo { SegmentId = segId, DocCount = 1, LiveDocCount = 1, CommitGeneration = 1 };
        segInfo.WriteTo(System.IO.Path.Combine(path, segId + ".seg"));

        // Write non-empty sidecar files so their checks pass
        File.WriteAllBytes(System.IO.Path.Combine(path, segId + ".dic"), [0x01]);
        File.WriteAllBytes(System.IO.Path.Combine(path, segId + ".pos"), [0x01]);
        File.WriteAllBytes(System.IO.Path.Combine(path, segId + ".fdt"), [0x01]);
        File.WriteAllBytes(System.IO.Path.Combine(path, segId + ".fdx"), [0x01]);

        // .nrm is too small (< 4 bytes)
        File.WriteAllBytes(System.IO.Path.Combine(path, segId + ".nrm"), [0x01, 0x02]);

        // Write a valid commit file pointing to this segment
        var segJson = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(System.IO.Path.Combine(path, "segments_1"), CommitFileFormat.Wrap(segJson));

        var dir = new MMapDirectory(path);
        var result = IndexValidator.Validate(dir);
        Assert.True(result.Issues.Any(i => i.Contains(".nrm")));
    }
}
