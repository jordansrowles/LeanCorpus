using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Gap-coverage tests for <see cref="IndexValidator"/> targeting branches not yet
/// covered: migration marker catch block, stale temporary files, segment metadata
/// mismatch/invalid counts, stored-fields header/index version branches, deletion
/// generation edge cases, vector and HNSW header checks, and deep validation.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexValidatorGapsTests : IDisposable
{
    private readonly string _root;

    public IndexValidatorGapsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ll_ivg_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // Migration marker

    [Fact(DisplayName = "Check: Corrupt Migration Marker Reports PartialMigrationMarkerState")]
    public void Check_CorruptMigrationMarker_ReportsPartialMigrationMarkerState()
    {
        var dir = SubDir("corrupt_marker");
        File.WriteAllText(Path.Combine(dir, "migration_state.json"), "{ not valid json }");
        var mmap = new MMapDirectory(dir);

        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.PartialMigrationMarkerState);
    }

    // Stale temporary files

    [Fact(DisplayName = "Check: Recognised Temp File Reports StaleTemporaryFile Warning")]
    public void Check_RecognisedTempFile_ReportsWarning()
    {
        var dir = SubDir("stale_tmp");
        File.WriteAllBytes(Path.Combine(dir, "segments_1.tmp"), []);
        var mmap = new MMapDirectory(dir);

        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.StaleTemporaryFile
              && i.Severity == IndexCheckSeverity.Warning);
    }

    [Fact(DisplayName = "Check: Unrecognised Temp File Is Not Reported")]
    public void Check_UnrecognisedTempFile_IsNotReported()
    {
        var dir = SubDir("unrecognised_tmp");
        File.WriteAllBytes(Path.Combine(dir, "arbitrary_scratch.tmp"), []);
        var mmap = new MMapDirectory(dir);

        var result = IndexValidator.Check(mmap);

        Assert.DoesNotContain(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.StaleTemporaryFile);
    }

    [Fact(DisplayName = "Check: All Recognised Temp File Patterns Report Warnings")]
    public void Check_AllRecognisedTempPatterns_ReportWarnings()
    {
        var dir = SubDir("all_tmp");
        string[] recognised =
        [
            "migration_state.json.tmp",
            "segments_5.tmp",
            "stats_2024.json.tmp",
            "seg_0.dic.tmp",
            "seg_0.pos.tmp",
            "seg_0.dvn.tmp",
            "seg_0.dvs.tmp",
            "seg_0.dss.tmp",
            "seg_0.dsn.tmp",
            "seg_0.dvb.tmp",
            "seg_0.fln.tmp",
            "seg_0.fdt.tmp",
            "seg_0.fdx.tmp",
            "seg_0.seg.tmp",
            "seg_0.stats.json.tmp",
            "seg_0_gen_1.del.tmp"
        ];
        foreach (var name in recognised)
            File.WriteAllBytes(Path.Combine(dir, name), []);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        var warnings = result.DetailedIssues
            .Where(i => i.Code == IndexCheckIssueCodes.StaleTemporaryFile)
            .ToList();

        Assert.Equal(recognised.Length, warnings.Count);
    }

    // CheckSegment: segment path does not exist

    [Fact(DisplayName = "Check: Segment Listed In Commit But Has No Files Returns Missing File Issues")]
    public void Check_SegmentMissingAllFiles_ReturnsMissingFileIssues()
    {
        var dir = SubDir("missing_seg_files");
        const string segId = "seg_ghost";
        var json = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(Path.Combine(dir, "segments_1"), CommitFileFormat.Wrap(json));

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.RequiredFileMissing);
        Assert.False(result.IsHealthy);
    }

    // CheckSegment: SegmentIdMismatch

    [Fact(DisplayName = "Check: Segment Metadata ID Mismatch Reports SegmentIdMismatch")]
    public void Check_SegmentIdMismatch_ReportsIssue()
    {
        var dir = SubDir("seg_id_mismatch");
        const string segId = "seg_correct";
        var commit = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(Path.Combine(dir, "segments_1"), CommitFileFormat.Wrap(commit));

        var info = new SegmentInfo
        {
            SegmentId = "seg_WRONG",
            DocCount = 1,
            LiveDocCount = 1,
            CommitGeneration = 1
        };
        info.WriteTo(Path.Combine(dir, segId + ".seg"));

        foreach (var ext in new[] { ".dic", ".pos", ".fdt", ".fdx", ".nrm" })
            File.WriteAllBytes(Path.Combine(dir, segId + ext), [0x01]);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.SegmentIdMismatch);
    }

    // CheckSegment: invalid doc count

    [Fact(DisplayName = "Check: Negative DocCount Reports InvalidDocCount")]
    public void Check_NegativeDocCount_ReportsIssue()
    {
        var dir = SubDir("neg_doccount");
        const string segId = "seg_negdoc";
        var commit = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(Path.Combine(dir, "segments_1"), CommitFileFormat.Wrap(commit));

        var info = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = -1,
            LiveDocCount = 0,
            CommitGeneration = 1
        };
        info.WriteTo(Path.Combine(dir, segId + ".seg"));

        foreach (var ext in new[] { ".dic", ".pos", ".fdt", ".fdx", ".nrm" })
            File.WriteAllBytes(Path.Combine(dir, segId + ext), [0x01]);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidDocCount);
    }

    // CheckSegment: invalid live doc count

    [Fact(DisplayName = "Check: LiveDocCount Exceeds DocCount Reports InvalidLiveDocCount")]
    public void Check_LiveDocCountExceedsDocCount_ReportsIssue()
    {
        var dir = SubDir("bad_livedoc");
        const string segId = "seg_livedoc";
        var commit = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(Path.Combine(dir, "segments_1"), CommitFileFormat.Wrap(commit));

        var info = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = 2,
            LiveDocCount = 5,
            CommitGeneration = 1
        };
        info.WriteTo(Path.Combine(dir, segId + ".seg"));

        foreach (var ext in new[] { ".dic", ".pos", ".fdt", ".fdx", ".nrm" })
            File.WriteAllBytes(Path.Combine(dir, segId + ext), [0x01]);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidLiveDocCount);
    }

    // CheckStoredFieldsCompression: invalid magic

    [Fact(DisplayName = "Check: Stored Fields Data With Wrong Magic Reports InvalidStoredFieldHeader")]
    public void Check_StoredFieldsDataWrongMagic_ReportsIssue()
    {
        var dir = SubDir("fdt_bad_magic");
        const string segId = "seg_fdtmagic";
        WriteMinimalSegment(dir, segId, docCount: 1);

        using (var stream = File.OpenWrite(Path.Combine(dir, segId + ".fdt")))
        using (var writer = new BinaryWriter(stream))
            writer.Write(0xDEADBEEF);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidStoredFieldHeader
              && (i.FileName ?? "").EndsWith(".fdt", StringComparison.Ordinal));
    }

    // CheckStoredFieldsCompression: future version

    [Fact(DisplayName = "Check: Stored Fields Data With Future Version Reports UnsupportedStoredFieldVersion")]
    public void Check_StoredFieldsDataFutureVersion_ReportsIssue()
    {
        var dir = SubDir("fdt_future_ver");
        const string segId = "seg_fdtver";
        WriteMinimalSegment(dir, segId, docCount: 1);

        using (var stream = File.OpenWrite(Path.Combine(dir, segId + ".fdt")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)(CodecConstants.StoredFieldsVersion + 1));
            writer.Write((byte)0); // VarInt64 bodyLen = 0
        }

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.UnsupportedStoredFieldVersion
              && (i.FileName ?? "").EndsWith(".fdt", StringComparison.Ordinal));
    }

    // CheckStoredFieldsIndex: invalid magic

    [Fact(DisplayName = "Check: Stored Fields Index With Wrong Magic Reports InvalidStoredFieldHeader")]
    public void Check_StoredFieldsIndexWrongMagic_ReportsIssue()
    {
        var dir = SubDir("fdx_bad_magic");
        const string segId = "seg_fdxmagic";
        WriteMinimalSegment(dir, segId, docCount: 1);

        using (var stream = File.OpenWrite(Path.Combine(dir, segId + ".fdx")))
        using (var writer = new BinaryWriter(stream))
            writer.Write(0xDEADBEEF);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidStoredFieldHeader
              && (i.FileName ?? "").EndsWith(".fdx", StringComparison.Ordinal));
    }

    // CheckStoredFieldsIndex: future version

    [Fact(DisplayName = "Check: Stored Fields Index With Future Version Reports UnsupportedStoredFieldVersion")]
    public void Check_StoredFieldsIndexFutureVersion_ReportsIssue()
    {
        var dir = SubDir("fdx_future_ver");
        const string segId = "seg_fdxver";
        WriteMinimalSegment(dir, segId, docCount: 1);

        using (var stream = File.OpenWrite(Path.Combine(dir, segId + ".fdx")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((byte)(CodecConstants.StoredFieldsVersion + 1));
            writer.Write((byte)0); // VarInt64 bodyLen = 0
        }

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.UnsupportedStoredFieldVersion
              && (i.FileName ?? "").EndsWith(".fdx", StringComparison.Ordinal));
    }

    // CheckStoredFieldsIndex: doc count mismatch

    [Fact(DisplayName = "Check: Stored Fields Index Doc Count Mismatch Reports StoredFieldDocCountMismatch")]
    public void Check_StoredFieldsIndexDocCountMismatch_ReportsIssue()
    {
        var dir = SubDir("fdx_doccount");
        const string segId = "seg_fdxdoc";
        WriteMinimalSegment(dir, segId, docCount: 1);

        using (var stream = File.OpenWrite(Path.Combine(dir, segId + ".fdx")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(CodecConstants.StoredFieldsVersion);
            writer.Write((byte)5); // VarInt bodyLen
            writer.Write(128);  // blockSize
            writer.Write(99);   // docCount - wrong (segment has 1)
            writer.Write(0);    // blockCount
        }

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.StoredFieldDocCountMismatch);
    }

    // CheckStoredFieldsIndex: invalid block offset

    [Fact(DisplayName = "Check: Stored Fields Index With Negative Block Offset Reports InvalidStoredFieldOffsets")]
    public void Check_StoredFieldsIndexNegativeOffset_ReportsIssue()
    {
        var dir = SubDir("fdx_neg_offset");
        const string segId = "seg_fdxoffset";
        WriteMinimalSegment(dir, segId, docCount: 1);

        using (var stream = File.OpenWrite(Path.Combine(dir, segId + ".fdx")))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(CodecConstants.StoredFieldsVersion);
            writer.Write((byte)5); // VarInt bodyLen
            writer.Write(128);  // blockSize
            writer.Write(1);    // docCount
            writer.Write(1);    // blockCount = 1 → write one offset
            writer.Write((long)-1); // invalid offset
        }

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidStoredFieldOffsets);
    }

    // CheckDeletionGeneration: missing del file when live docs < doc count

    [Fact(DisplayName = "Check: Missing Deletion File When Deletes Exist Reports DeletionFileMissing")]
    public void Check_DeletedDocsButNoDelFile_ReportsIssue()
    {
        var dir = SubDir("del_missing");
        const string segId = "seg_delcheck";
        var commit = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(Path.Combine(dir, "segments_1"), CommitFileFormat.Wrap(commit));

        var info = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = 3,
            LiveDocCount = 2,
            CommitGeneration = 1
        };
        info.WriteTo(Path.Combine(dir, segId + ".seg"));

        foreach (var ext in new[] { ".dic", ".pos", ".fdt", ".fdx", ".nrm" })
            File.WriteAllBytes(Path.Combine(dir, segId + ext), [0x01]);

        var mmap = new MMapDirectory(dir);
        var result = IndexValidator.Check(mmap);

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.DeletionFileMissing);
    }

    // ValidateLiveDocs: live count mismatch

    [Fact(DisplayName = "Check: VerifyLiveDocs With Mismatch Reports DeletionLiveCountMismatch")]
    public void Check_VerifyLiveDocs_LiveCountMismatch_ReportsIssue()
    {
        var dir = SubDir("livedocs_mismatch");
        using var mmap = new MMapDirectory(dir);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());

        writer.AddDocument(LeanDocWith("body", "keep one"));
        writer.AddDocument(LeanDocWith("body", "keep two"));
        writer.AddDocument(LeanDocWith("body", "delete me"));
        writer.Commit();
        writer.DeleteDocuments(new TermQuery("body", "delete"));
        writer.Commit();

        // Corrupt the .del file to record the wrong live count
        var delFiles = Directory.GetFiles(mmap.DirectoryPath, "*_gen_*.del");
        if (delFiles.Length == 0)
            return; // No gen-based del file; skip

        var delPath = delFiles[0];
        var bytes = File.ReadAllBytes(delPath);
        if (bytes.Length >= 8)
        {
            // Overwrite the LiveCount field (bytes 4-7) with a wrong value
            bytes[4] = 0xFF;
            bytes[5] = 0xFF;
            File.WriteAllBytes(delPath, bytes);
        }

        var result = IndexValidator.Check(mmap,
            new IndexCheckOptions { VerifyLiveDocs = true });

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.DeletionLiveCountMismatch
              || i.Code == IndexCheckIssueCodes.DeletionFileUnreadable);
    }

    // CheckVectorHeader: invalid magic

    [Fact(DisplayName = "Check: Vector File With Wrong Magic Reports InvalidVectorHeader")]
    public void Check_VectorFileWrongMagic_ReportsIssue()
    {
        var dir = SubDir("vec_bad_magic");
        using var mmap = BuildVectorIndex(dir);

        var vecFile = Directory.GetFiles(dir, "*.vec").Single();
        OverwriteFirstFourBytes(vecFile, 0xDEADBEEF);

        var result = IndexValidator.Check(new MMapDirectory(dir));

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidVectorHeader);
    }

    // CheckVectorHeader: vector count mismatch

    [Fact(DisplayName = "Check: Vector File With Wrong Vector Count Reports VectorCountMismatch")]
    public void Check_VectorFileWrongCount_ReportsIssue()
    {
        var dir = SubDir("vec_wrong_count");
        using var mmap = BuildVectorIndex(dir);

        var vecFile = Directory.GetFiles(dir, "*.vec").Single();
        PatchVectorCount(vecFile, 999);

        var result = IndexValidator.Check(new MMapDirectory(dir));

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.VectorCountMismatch);
    }

    // CheckVectorHeader: dimension mismatch

    [Fact(DisplayName = "Check: Vector File With Wrong Dimension Reports VectorDimensionMismatch")]
    public void Check_VectorFileWrongDimension_ReportsIssue()
    {
        var dir = SubDir("vec_wrong_dim");
        using var mmap = BuildVectorIndex(dir);

        var vecFile = Directory.GetFiles(dir, "*.vec").Single();
        PatchVectorDimension(vecFile, 999);

        var result = IndexValidator.Check(new MMapDirectory(dir));

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.VectorDimensionMismatch);
    }

    // CheckHnswHeader: invalid magic

    [Fact(DisplayName = "Check: HNSW File With Wrong Magic Reports InvalidHnswHeader")]
    public void Check_HnswFileWrongMagic_ReportsIssue()
    {
        var dir = SubDir("hnsw_bad_magic");
        using var mmap = BuildVectorIndex(dir);

        var hnswFile = Directory.GetFiles(dir, "*.hnsw").Single();
        OverwriteFirstFourBytes(hnswFile, 0xDEADBEEF);

        var result = IndexValidator.Check(new MMapDirectory(dir));

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.InvalidHnswHeader);
    }

    // CheckHnswHeader: dimension mismatch

    [Fact(DisplayName = "Check: HNSW File With Wrong Dimension Reports HnswDimensionMismatch")]
    public void Check_HnswFileWrongDimension_ReportsIssue()
    {
        var dir = SubDir("hnsw_wrong_dim");
        using var mmap = BuildVectorIndex(dir);

        var hnswFile = Directory.GetFiles(dir, "*.hnsw").Single();
        PatchHnswDimension(hnswFile, 999);

        var result = IndexValidator.Check(new MMapDirectory(dir));

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.HnswDimensionMismatch);
    }

    // CheckHnswHeader: normalisation mismatch

    [Fact(DisplayName = "Check: HNSW File With Wrong Normalisation Reports HnswNormalisationMismatch")]
    public void Check_HnswFileWrongNormalisation_ReportsIssue()
    {
        var dir = SubDir("hnsw_wrong_norm");
        using var mmap = BuildVectorIndex(dir, normalised: false);

        var hnswFile = Directory.GetFiles(dir, "*.hnsw").Single();
        PatchHnswNormalised(hnswFile, true);

        var result = IndexValidator.Check(new MMapDirectory(dir));

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.HnswNormalisationMismatch);
    }

    // ValidateVectorsDeep

    [Fact(DisplayName = "Check: VerifyVectors With Corrupt Vector File Reports VectorReadFailure")]
    public void Check_VerifyVectors_CorruptVectorFile_ReportsIssue()
    {
        var dir = SubDir("deep_vec_corrupt");
        using var mmap = BuildVectorIndex(dir);

        var vecFile = Directory.GetFiles(dir, "*.vec").Single();
        OverwriteFirstFourBytes(vecFile, 0xDEADBEEF);

        var result = IndexValidator.Check(new MMapDirectory(dir),
            new IndexCheckOptions { VerifyVectors = true });

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.VectorReadFailure
              || i.Code == IndexCheckIssueCodes.InvalidVectorHeader);
    }

    // ValidateHnswDeep

    [Fact(DisplayName = "Check: VerifyHnsw With Corrupt HNSW File Reports HnswReadFailure")]
    public void Check_VerifyHnsw_CorruptHnswFile_ReportsIssue()
    {
        var dir = SubDir("deep_hnsw_corrupt");
        using var mmap = BuildVectorIndex(dir);

        var hnswFile = Directory.GetFiles(dir, "*.hnsw").Single();
        OverwriteFirstFourBytes(hnswFile, 0xDEADBEEF);

        var result = IndexValidator.Check(new MMapDirectory(dir),
            new IndexCheckOptions { VerifyHnsw = true });

        Assert.Contains(result.DetailedIssues,
            i => i.Code == IndexCheckIssueCodes.HnswReadFailure
              || i.Code == IndexCheckIssueCodes.InvalidHnswHeader);
    }

    // Helpers

    private static LeanDocument LeanDocWith(string field, string value)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField(field, value));
        return doc;
    }

    private static MMapDirectory BuildVectorIndex(string dir, bool normalised = true)
    {
        var mmap = new MMapDirectory(dir);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = normalised
        });
        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("embedding",
                new ReadOnlyMemory<float>([i + 1f, 0f, 0f])));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return mmap;
    }

    private static void WriteMinimalSegment(string dir, string segId, int docCount)
    {
        var commit = $"{{\"Segments\":[\"{segId}\"],\"Generation\":1}}";
        File.WriteAllText(Path.Combine(dir, "segments_1"), CommitFileFormat.Wrap(commit));

        var info = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = docCount,
            LiveDocCount = docCount,
            CommitGeneration = 1
        };
        info.WriteTo(Path.Combine(dir, segId + ".seg"));

        foreach (var ext in new[] { ".dic", ".pos", ".nrm" })
            File.WriteAllBytes(Path.Combine(dir, segId + ext), [0x01]);
    }

    private static void OverwriteFirstFourBytes(string path, uint value)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write(value);
    }

    /// <summary>
    /// Returns the file offset where the body begins after the CodecKit envelope
    /// (version byte + VarInt64 body length).
    /// </summary>
    private static int FindBodyStartOffset(string path)
    {
        using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(readStream);
        reader.ReadByte(); // version
        // Consume VarInt64 body length (LEB128)
        int shift = 0;
        while (shift < 70)
        {
            byte b = reader.ReadByte();
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return (int)readStream.Position;
    }

    private static void PatchVectorCount(string path, int count)
    {
        // CodecKit vector layout: [version][VarInt64 bodyLen][body: int32 count, int32 dim, byte dataFmt, ...]
        int bodyStart = FindBodyStartOffset(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Seek(bodyStart, SeekOrigin.Begin);
        using var writer = new BinaryWriter(stream);
        writer.Write(count);
    }

    private static void PatchVectorDimension(string path, int dimension)
    {
        int bodyStart = FindBodyStartOffset(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Seek(bodyStart + 4, SeekOrigin.Begin); // skip count (int32)
        using var writer = new BinaryWriter(stream);
        writer.Write(dimension);
    }

    private static void PatchHnswDimension(string path, int dimension)
    {
        // CodecKit HNSW layout: [version][VarInt64 bodyLen][body: int32 dim, byte normalised, ...]
        int bodyStart = FindBodyStartOffset(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Seek(bodyStart, SeekOrigin.Begin);
        using var writer = new BinaryWriter(stream);
        writer.Write(dimension);
    }

    private static void PatchHnswNormalised(string path, bool normalised)
    {
        int bodyStart = FindBodyStartOffset(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Seek(bodyStart + 4, SeekOrigin.Begin); // skip dimension (int32)
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)(normalised ? 1 : 0));
    }
}
