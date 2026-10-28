using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Integration)]
[Area(TestArea.CodecKit)]
public sealed class FieldLengthCorruptionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "fln-corruption-" + Guid.NewGuid().ToString("N"));
    public FieldLengthCorruptionTests() => Directory.CreateDirectory(_path);
    public void Dispose() => Directory.Delete(_path, true);

    public static IEnumerable<object[]> Cases => new[]
    {
        "negative-fields", "huge-fields", "negative-name", "empty-name", "huge-name", "truncated-name",
        "invalid-name", "invalid-utf8", "negative-docs", "huge-docs", "fewer-docs", "more-docs",
        "value-byte-overflow", "truncated-values", "duplicate-name", "trailing", "checksum", "value-overflow"
    }.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(Cases))]
    public void CorruptionIsBoundedAcrossReaderValidationMergeAndRecovery(string corruption)
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance, UseCompoundFile = false }))
        {
            for (int segment = 0; segment < 2; segment++)
            {
                for (int doc = 0; doc < 3; doc++)
                {
                    var document = new LeanDocument();
                    document.Add(new TextField("body", "hello world"));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }
        }
        var infos = IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)!.SegmentInfos.ToList();
        string file = Path.Combine(_path, infos[0].SegmentId + ".fln");
        WriteCorrupt(file, corruption);
        // Warm the boundary before measuring exception and metadata allocation.
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, 3));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, 3));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000, "Malformed small metadata must not cause large allocations.");
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.EnumerateFields(file, 3));
        SegmentReader? reader = null;
        Assert.Throws<InvalidDataException>(() => reader = new SegmentReader(directory, infos[0]));
        Assert.Null(reader);
        var check = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.False(check.IsHealthy);
        Assert.Contains(check.DetailedIssues, issue => issue.Message.Contains("field-length", StringComparison.OrdinalIgnoreCase) || issue.FileName == Path.GetFileName(file));
        var merger = new SegmentMerger(directory, mergeThreshold: 2);
        int next = 100;
        Assert.Throws<InvalidDataException>(() => merger.MergeAll(infos, ref next));
        Assert.False(File.Exists(Path.Combine(_path, "seg_100.seg")));
        Assert.Throws<InvalidDataException>(() => IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false));
    }

    [Theory]
    [InlineData(1, "canonical")]
    [InlineData(2, "canonical")]
    [InlineData(1, "envelope")]
    [InlineData(2, "envelope")]
    [InlineData(1, "trailer")]
    [InlineData(2, "trailer")]
    public void SupportedBodiesUseTheSameBoundsAndReturnExactValues(int version, string framing)
    {
        string file = Path.Combine(_path, "legacy.fln");
        CodecFileWriter.WriteAtomically(file, "leancorpus.field-lengths.data", version, false, output =>
        {
            output.WriteInt32(1);
            output.WriteInt32(4);
            output.WriteBytes("body"u8);
            output.WriteInt32(3);
            foreach (int value in new[] { 0, 128, int.MaxValue })
                output.Write7BitEncodedInt(value);
        });
        if (framing != "canonical")
        {
            byte[] body;
            using (var input = new IndexInput(file))
            using (var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.field-lengths.data")))
                body = frame.ReadBody();
            using var output = new IndexOutput(file);
            if (framing == "trailer")
            {
                using var header = CodecFileHeader.BeginStreamingWrite(output, checked((byte)version));
                output.WriteBytes(body);
            }
            else
            {
                output.WriteByte(checked((byte)version));
                // The historical envelope length is a signed ZigZag VarInt.
                output.WriteVarInt(checked(body.Length << 1));
                output.WriteBytes(body);
            }
        }
        Assert.Equal(new[] { 0, 128, int.MaxValue }, FieldLengthReader.TryRead(file, 3)!["body"]);
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, 2));
    }

    [Theory]
    [InlineData(1, 1 << 29)]
    [InlineData(1, 1 << 30)]
    [InlineData(1, int.MaxValue)]
    [InlineData(2, 1 << 29)]
    [InlineData(2, 1 << 30)]
    [InlineData(2, int.MaxValue)]
    public void HugeOwningDocumentCountsRejectTinyBodiesBeforeAllocation(int version, int documentCount)
    {
        string file = Path.Combine(_path, "tiny.fln");
        CodecFileWriter.WriteAtomically(file, "leancorpus.field-lengths.data", version, false, output =>
        {
            output.WriteInt32(1);
            output.WriteInt32(4);
            output.WriteBytes("body"u8);
            output.WriteInt32(documentCount);
            output.WriteByte(0);
        });
        // Match the owning count so that a mismatch cannot hide size arithmetic bugs.
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, documentCount));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, documentCount));
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.EnumerateFields(file, documentCount));
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.Validate(new IndexInput(file), documentCount));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000,
            "Matching huge document counts must remain bounded by the tiny encoded body.");
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public void FieldLengthV1MigratesToVarIntV2AndV2RemainsCurrent(int version, bool compound)
        => CheckFieldLengthCompatibility(version, compound, padded: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyZeroPaddingIsDiscardedDuringV1Migration(bool compound)
        => CheckFieldLengthCompatibility(1, compound, padded: true);

    private void CheckFieldLengthCompatibility(int version, bool compound, bool padded)
    {
        using var directory = new MMapDirectory(_path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance, UseCompoundFile = false }))
        {
            for (int doc = 0; doc < 3; doc++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", "hello world"));
                writer.AddDocument(document);
            }
            writer.Commit();
        }
        string file = Directory.GetFiles(_path, "*.fln").Single();
        CodecFileWriter.WriteAtomically(file, "leancorpus.field-lengths.data", version, false, output =>
        {
            output.WriteInt32(1); output.WriteInt32(4); output.WriteBytes("body"u8); output.WriteInt32(padded ? 16 : 3);
            foreach (int value in new[] { 2, 2, 2 }) output.Write7BitEncodedInt(value);
            if (padded)
                for (int document = 3; document < 16; document++) output.WriteByte(0);
        });
        if (compound)
        {
            string metadata = Directory.GetFiles(_path, "*.seg").Single();
            var info = SegmentInfo.ReadFrom(metadata);
            Assert.True(SegmentFileSet.Pack(_path, info.SegmentId));
            info.IsCompoundFile = true;
            info.WriteTo(metadata);
        }
        var plan = Rowles.LeanCorpus.Index.Migration.IndexCodecMigrator.Plan(directory);
        var fieldLengthActions = plan.Actions
            .Where(static action => action.FileName?.EndsWith(".fln", StringComparison.Ordinal) == true)
            .ToArray();
        if (version == 1)
        {
            Assert.Equal(
                Rowles.LeanCorpus.Index.Migration.IndexCodecMigrationActionKind.Rewrite,
                Assert.Single(fieldLengthActions).Kind);
        }
        else
        {
            Assert.Empty(fieldLengthActions);
        }
        var result = Rowles.LeanCorpus.Index.Migration.IndexCodecMigrator.Migrate(directory,
            new Rowles.LeanCorpus.Index.Migration.IndexCodecMigrationOptions { DryRun = false });
        Assert.True(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        var target = IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)!.SegmentInfos.Single();
        Assert.Equal(compound, target.IsCompoundFile);
        using var files = SegmentFileAccess.Open(directory, new SegmentDescriptor(target));
        using (var input = files.OpenInput(".fln"))
        using (var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.field-lengths.data")))
        {
            Assert.Equal(2, frame.Metadata.FormatVersion);
            Assert.Equal(19, frame.Metadata.BodyLength);
            frame.ValidateChecksum();
        }
        Assert.Equal(new[] { 2, 2, 2 }, FieldLengthReader.TryRead(files.OpenInput(".fln"), 3)["body"]);
        Assert.True(IndexValidator.Check(directory, new IndexCheckOptions { Deep = true }).IsHealthy);
    }

    [Theory]
    [InlineData(1, 0, true)]
    [InlineData(2, 0, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 1, false)]
    public void LegacyPaddingIsAcceptedOnlyWhenEveryExtraValueIsZero(int version, int padding, bool accepted)
    {
        string file = Path.Combine(_path, "padded.fln");
        CodecFileWriter.WriteAtomically(file, "leancorpus.field-lengths.data", version, false, output =>
        {
            output.WriteInt32(1); output.WriteInt32(4); output.WriteBytes("body"u8); output.WriteInt32(4);
            foreach (int value in new[] { 2, 2, 2, padding })
                output.Write7BitEncodedInt(value);
        });
        if (accepted)
        {
            Assert.Equal(new[] { 2, 2, 2 }, FieldLengthReader.TryRead(file, 3)!["body"]);
            Assert.Equal(new[] { 2, 2, 2 }, FieldLengthReader.EnumerateFields(file, 3).Single().Lengths);
            FieldLengthReader.Validate(new IndexInput(file), 3);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, 3));
            Assert.Throws<InvalidDataException>(() => FieldLengthReader.EnumerateFields(file, 3));
            Assert.Throws<InvalidDataException>(() => FieldLengthReader.Validate(new IndexInput(file), 3));
        }
    }

    [Fact]
    public void OverlongVarIntIsRejected()
    {
        string file = Path.Combine(_path, "overlong.fln");
        CodecFileWriter.WriteAtomically(file, "leancorpus.field-lengths.data", 2, false, output =>
        {
            output.WriteInt32(1);
            output.WriteInt32(4);
            output.WriteBytes("body"u8);
            output.WriteInt32(1);
            output.WriteBytes([0x81, 0x00]);
        });

        Assert.Throws<InvalidDataException>(() => FieldLengthReader.TryRead(file, 1));
        Assert.Throws<InvalidDataException>(() => FieldLengthReader.Validate(new IndexInput(file), 1));
    }

    private static void WriteCorrupt(string file, string corruption)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1); writer.Write(4); writer.Write("body"u8); writer.Write(3);
            writer.Write((byte)1); writer.Write((byte)2); writer.Write((byte)3);
        }
        byte[] body = stream.ToArray();
        void Set(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset), value);
        int expected = 3;
        switch (corruption)
        {
            case "negative-fields": Set(0, -1); break;
            case "huge-fields": Set(0, int.MaxValue); break;
            case "negative-name": Set(4, -1); break;
            case "empty-name": Set(4, 0); break;
            case "huge-name": Set(4, int.MaxValue); break;
            case "truncated-name": body = body[..10]; break;
            case "invalid-name": body[8] = 0; break;
            case "invalid-utf8": body[8] = 0xff; break;
            case "negative-docs": Set(12, -1); break;
            case "huge-docs": Set(12, int.MaxValue); break;
            case "fewer-docs": Set(12, expected - 1); break;
            case "more-docs": Set(12, expected + 1); break;
            case "value-byte-overflow": Set(12, 1 << 30); break;
            case "truncated-values": body = body[..^1]; break;
            case "duplicate-name": body = body.Concat(body[4..]).ToArray(); Set(0, 2); break;
            case "trailing": body = body.Concat(new byte[] { 0 }).ToArray(); break;
            case "value-overflow":
                body = [.. body[..16], 0xff, 0xff, 0xff, 0xff, 0x0f, .. body[17..]];
                break;
        }
        CodecFileWriter.WriteAtomically(file, "leancorpus.field-lengths.data", CodecConstants.FieldLengthVersion, false, output => output.WriteBytes(body));
        if (corruption == "checksum")
        {
            byte[] framed = File.ReadAllBytes(file); framed[^1] ^= 1; File.WriteAllBytes(file, framed);
        }
    }
}
