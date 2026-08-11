using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

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
        Assert.Contains(result.Issues, issue =>
            issue.Contains(".fdx", StringComparison.Ordinal) ||
            issue.Contains(".seg", StringComparison.Ordinal));
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

        // .nrm is empty — CodecFileHeader.ReadVersion will throw EndOfStreamException
        File.WriteAllBytes(System.IO.Path.Combine(path, segId + ".nrm"), []);

        // Write a valid commit file pointing to this segment
        var segJson = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(System.IO.Path.Combine(path, "segments_1"), CommitFileFormat.Wrap(segJson));

        var dir = new MMapDirectory(path);
        var result = IndexValidator.Validate(dir);
        Assert.Contains(result.Issues, issue => issue.Contains(".nrm", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Check: Missing Required File Returns Structured Issue")]
    public void Check_MissingRequiredFile_ReturnsStructuredIssue()
    {
        var dir = new MMapDirectory(SubDir("val_structured_missing"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        var dicFile = Directory.GetFiles(dir.DirectoryPath, "*.dic").Single();
        var segmentId = Path.GetFileNameWithoutExtension(dicFile);
        File.Delete(dicFile);

        var result = IndexValidator.Check(dir);

        var issue = Assert.Single(result.DetailedIssues, i => i.Code == IndexCheckIssueCodes.RequiredFileMissing);
        Assert.Equal(IndexCheckSeverity.Error, issue.Severity);
        Assert.Equal(segmentId, issue.SegmentId);
        Assert.Equal(Path.GetFileName(dicFile), issue.FileName);
        Assert.True(issue.IsRepairable);
        Assert.NotEmpty(issue.SuggestedActions);
        Assert.False(result.IsHealthy);
    }

    [Fact(DisplayName = "Check: Invalid DocValues Header Returns Stable Code")]
    public void Check_InvalidDocValuesHeader_ReturnsStableCode()
    {
        var dir = new MMapDirectory(SubDir("val_bad_dss"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new StringField("tag", "alpha"));
        doc.Add(new StringField("tag", "beta"));
        writer.AddDocument(doc);
        writer.Commit();

        var dssFile = Directory.GetFiles(dir.DirectoryPath, "*.dss").Single();
        File.WriteAllBytes(dssFile, [0x01]);

        var result = IndexValidator.Check(dir);

        Assert.Contains(result.DetailedIssues, i => i.Code == IndexCheckIssueCodes.InvalidCodecFrame && i.FileName == Path.GetFileName(dssFile));
    }

    [Fact(DisplayName = "Check: Unregistered Stored Field Compression Returns Issue")]
    public void Check_UnregisteredStoredFieldCompression_ReturnsIssue()
    {
        var dir = new MMapDirectory(SubDir("val_bad_compression"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        var fdtFile = Directory.GetFiles(dir.DirectoryPath, "*.fdt").Single();
        long compressionOffset;
        using (var input = new IndexInput(fdtFile))
        using (var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.stored-fields.data")))
        {
            compressionOffset = frame.Metadata.BodyStart + sizeof(int);
        }
        using (var stream = File.Open(fdtFile, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = compressionOffset;
            stream.WriteByte(250);
        }

        var result = IndexValidator.Check(dir);

        Assert.Contains(result.DetailedIssues, i => i.Code == IndexCheckIssueCodes.UnregisteredCompressionPolicy);
    }

    [Fact(DisplayName = "Check: Deep validation reports canonical checksum mismatch")]
    public void Check_DeepCanonicalChecksumMismatch_ReturnsStableCode()
    {
        var dir = CreateIndex("val_deep_checksum", useCompoundFile: false);
        var normsPath = Directory.GetFiles(dir.DirectoryPath, "*.nrm").Single();
        CorruptCanonicalBody(normsPath, physicalBaseOffset: 0);

        var result = IndexValidator.Check(dir, new IndexCheckOptions { Deep = true });

        Assert.Contains(result.DetailedIssues, issue =>
            issue.Code == IndexCheckIssueCodes.CodecChecksumMismatch &&
            issue.FileName == Path.GetFileName(normsPath));
    }

    [Fact(DisplayName = "Check: Deep validation reports compound member checksum mismatch")]
    public void Check_DeepCompoundMemberChecksumMismatch_ReturnsLogicalMemberIssue()
    {
        var dir = CreateIndex("val_deep_compound_checksum", useCompoundFile: true);
        var cfsPath = Directory.GetFiles(dir.DirectoryPath, "*.cfs").Single();
        var segmentId = Path.GetFileNameWithoutExtension(cfsPath);
        var memberName = segmentId + ".nrm";
        long memberOffset = ReadCompoundMemberOffset(cfsPath, memberName);
        long memberBodyStart;

        using (var compound = CompoundFileReader.Open(dir, Path.GetFileName(cfsPath)))
        using (var member = compound.OpenInput(dir, memberName))
        using (var frame = CodecFileReader.Open(member, CodecCatalog.Default.GetFile("leancorpus.norms.data")))
        {
            memberBodyStart = frame.Metadata.BodyStart;
        }
        CorruptCanonicalBody(cfsPath, memberOffset, memberBodyStart);

        var result = IndexValidator.Check(dir, new IndexCheckOptions { Deep = true });

        Assert.Contains(result.DetailedIssues, issue =>
            issue.Code == IndexCheckIssueCodes.CodecChecksumMismatch &&
            issue.FileName == memberName &&
            issue.SegmentId == segmentId);
    }

    [Fact(DisplayName = "Check: Missing Deletion Generation File Returns Issue")]
    public void Check_MissingDeletionGenerationFile_ReturnsIssue()
    {
        var dir = new MMapDirectory(SubDir("val_missing_del_gen"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var keep = new LeanDocument();
        keep.Add(new TextField("body", "keep"));
        var delete = new LeanDocument();
        delete.Add(new TextField("body", "delete"));
        writer.AddDocument(keep);
        writer.AddDocument(delete);
        writer.Commit();
        writer.DeleteDocuments(new TermQuery("body", "delete"));
        writer.Commit();

        foreach (var delFile in Directory.GetFiles(dir.DirectoryPath, "*_gen_*.del"))
            File.Delete(delFile);

        var result = IndexValidator.Check(dir);

        Assert.Contains(result.DetailedIssues, i => i.Code == IndexCheckIssueCodes.DeletionFileMissing);
    }

    [Fact(DisplayName = "Check: Missing Vector And Hnsw Files Return Issues")]
    public void Check_MissingVectorAndHnswFiles_ReturnIssues()
    {
        var dir = new MMapDirectory(SubDir("val_missing_vector_hnsw"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { BuildHnswOnFlush = true });
        for (int i = 0; i < 2; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("embedding", new ReadOnlyMemory<float>([i + 1f, 0f, 0f])));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var vecFile = Directory.GetFiles(dir.DirectoryPath, "*.vec").Single();
        var hnswFile = Directory.GetFiles(dir.DirectoryPath, "*.hnsw").Single();
        File.Delete(vecFile);
        File.Delete(hnswFile);

        var result = IndexValidator.Check(dir);

        Assert.Contains(result.DetailedIssues, i => i.Code == IndexCheckIssueCodes.VectorFileMissing);
    }

    [Fact(DisplayName = "Check: Deep DocValues Catches Corrupt Offsets")]
    public void Check_WithDeepDocValues_CatchesCorruptOffsets()
    {
        var dir = new MMapDirectory(SubDir("val_deep_bad_dss"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new StringField("tag", "alpha"));
        writer.AddDocument(doc);
        writer.Commit();

        var dssFile = Directory.GetFiles(dir.DirectoryPath, "*.dss").Single();
        using (var stream = File.Create(dssFile))
        using (var binaryWriter = new BinaryWriter(stream))
        {
            binaryWriter.Write(CodecConstants.SortedSetDocValuesVersion);
            binaryWriter.Write((byte)0); // VarInt bodyLen = 0
            binaryWriter.Write(1);
            binaryWriter.Write((byte)3);
            binaryWriter.Write(System.Text.Encoding.UTF8.GetBytes("tag"));
            binaryWriter.Write(1);
            binaryWriter.Write(0);
            binaryWriter.Write(1);
            binaryWriter.Write(0);
            binaryWriter.Write(0);
        }

        var result = IndexValidator.Check(dir, new IndexCheckOptions { VerifyDocValues = true });

        Assert.Contains(result.DetailedIssues, i => i.Code == IndexCheckIssueCodes.DocValuesReadFailure);
    }

    private MMapDirectory CreateIndex(string name, bool useCompoundFile)
    {
        var directory = new MMapDirectory(SubDir(name));
        using var writer = new IndexWriter(directory, new IndexWriterConfig { UseCompoundFile = useCompoundFile });
        var document = new LeanDocument();
        document.Add(new TextField("body", "checksum validation"));
        writer.AddDocument(document);
        writer.Commit();
        return directory;
    }

    private static void CorruptCanonicalBody(string path, long physicalBaseOffset, long? knownBodyStart = null)
    {
        long bodyStart = knownBodyStart ?? ReadCanonicalBodyStart(path);
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = checked(physicalBaseOffset + bodyStart);
        int value = stream.ReadByte();
        Assert.NotEqual(-1, value);
        stream.Position = checked(physicalBaseOffset + bodyStart);
        stream.WriteByte((byte)(value ^ 0xff));
    }

    private static long ReadCanonicalBodyStart(string path)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input);
        Assert.True(frame.Metadata.BodyLength > 0);
        return frame.Metadata.BodyStart;
    }

    private static long ReadCompoundMemberOffset(string path, string memberName)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        reader.ReadInt32();
        reader.ReadInt32();
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string name = reader.ReadString();
            long offset = reader.ReadInt64();
            reader.ReadInt64();
            if (name == memberName)
                return offset;
        }

        throw new InvalidDataException($"Compound member '{memberName}' was not found.");
    }
}
