using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecFileHeaderTests : IDisposable
{
    private readonly string _tempDirectory;

    public CodecFileHeaderTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LeanCorpus_CodecFileHeaderTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            try { Directory.Delete(_tempDirectory, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private string TempFile(string name) => Path.Combine(_tempDirectory, name);

    // ═══════════════════════════════════════════════════
    //  BinaryWriter / BinaryReader tests
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Write(BinaryWriter) → Read(BinaryReader) round-trip with empty body")]
    public void BinaryWriter_Read_BinaryReader_EmptyBody()
    {
        var path = TempFile("bw_br_empty.dat");
        byte[] body = [];

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            CodecFileHeader.Write(writer, CodecFormats.Postings, body);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            var result = CodecFileHeader.Read(reader, CodecFormats.Postings);

            Assert.Equal(CodecConstants.PostingsVersion, result.Version);
            Assert.Empty(result.Body);
        }
    }

    [Fact(DisplayName = "Write(BinaryWriter) → Read(BinaryReader) round-trip with 4-byte body")]
    public void BinaryWriter_Read_BinaryReader_FourByteBody()
    {
        var path = TempFile("bw_br_4b.dat");
        byte[] body = [0xAA, 0xBB, 0xCC, 0xDD];

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            CodecFileHeader.Write(writer, CodecFormats.Postings, body);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            var result = CodecFileHeader.Read(reader, CodecFormats.Postings);

            Assert.Equal(CodecConstants.PostingsVersion, result.Version);
            Assert.Equal(body, result.Body);
        }
    }

    [Fact(DisplayName = "Write(BinaryWriter) → Read(BinaryReader) round-trip with 1KB body")]
    public void BinaryWriter_Read_BinaryReader_1KBBody()
    {
        var path = TempFile("bw_br_1k.dat");
        byte[] body = new byte[1024];
        Random.Shared.NextBytes(body);

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            CodecFileHeader.Write(writer, CodecFormats.Postings, body);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            var result = CodecFileHeader.Read(reader, CodecFormats.Postings);

            Assert.Equal(CodecConstants.PostingsVersion, result.Version);
            Assert.Equal(body, result.Body);
        }
    }

    [Fact(DisplayName = "Write(BinaryWriter) → Read(BinaryReader) round-trip with 64KB body")]
    public void BinaryWriter_Read_BinaryReader_64KBBody()
    {
        var path = TempFile("bw_br_64k.dat");
        byte[] body = new byte[65536];
        Random.Shared.NextBytes(body);

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            CodecFileHeader.Write(writer, CodecFormats.Postings, body);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            var result = CodecFileHeader.Read(reader, CodecFormats.Postings);

            Assert.Equal(CodecConstants.PostingsVersion, result.Version);
            Assert.Equal(body, result.Body);
        }
    }

    [Fact(DisplayName = "Write(BinaryWriter) → ReadVersion(BinaryReader) verifies version and stream position")]
    public void BinaryWriter_ReadVersion_BinaryReader_PositionCorrect()
    {
        var path = TempFile("bw_readversion.dat");
        byte[] body = [0x01, 0x02, 0x03];

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            CodecFileHeader.Write(writer, CodecFormats.Postings, body);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(reader, CodecFormats.Postings);

            Assert.Equal(CodecConstants.PostingsVersion, version);
            // Version (1) + VarInt(3) = 1 byte → total header = 2 bytes
            Assert.Equal(2, fs.Position);
        }
    }

    // ═══════════════════════════════════════════════════
    //  IndexOutput / IndexInput tests
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Write(IndexOutput) → Read(IndexInput) round-trip with empty body")]
    public void IndexOutput_Read_IndexInput_EmptyBody()
    {
        var path = TempFile("io_ii_empty.dat");
        byte[] body = [];

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.Postings, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        var result = CodecFileHeader.Read(input, CodecFormats.Postings);

        Assert.Equal(CodecConstants.PostingsVersion, result.Version);
        Assert.Empty(result.Body);
    }

    [Fact(DisplayName = "Write(IndexOutput) → Read(IndexInput) round-trip with 4-byte body")]
    public void IndexOutput_Read_IndexInput_FourByteBody()
    {
        var path = TempFile("io_ii_4b.dat");
        byte[] body = [0xDE, 0xAD, 0xBE, 0xEF];

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.Postings, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        var result = CodecFileHeader.Read(input, CodecFormats.Postings);

        Assert.Equal(CodecConstants.PostingsVersion, result.Version);
        Assert.Equal(body, result.Body);
    }

    [Fact(DisplayName = "Write(IndexOutput) → Read(IndexInput) round-trip with 1KB body")]
    public void IndexOutput_Read_IndexInput_1KBBody()
    {
        var path = TempFile("io_ii_1k.dat");
        byte[] body = new byte[1024];
        Random.Shared.NextBytes(body);

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.Postings, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        var result = CodecFileHeader.Read(input, CodecFormats.Postings);

        Assert.Equal(CodecConstants.PostingsVersion, result.Version);
        Assert.Equal(body, result.Body);
    }

    [Fact(DisplayName = "Write(IndexOutput) → ReadVersion(IndexInput) verifies version and position")]
    public void IndexOutput_ReadVersion_IndexInput_PositionCorrect()
    {
        var path = TempFile("io_readversion.dat");
        byte[] body = [0x01, 0x02, 0x03, 0x04, 0x05];

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.TermDictionary, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.TermDictionary);

        Assert.Equal(CodecConstants.TermDictionaryVersion, version);
        // Position should be past the header
        Assert.True(input.Position > 0);
    }

    [Fact(DisplayName = "Read<T> generic overload chains decode correctly")]
    public void IndexInput_ReadGeneric_ChainsCorrectly()
    {
        var path = TempFile("io_readgeneric.dat");
        // Write a header wrapping a UInt32 body
        byte[] body = [0x01, 0x00, 0x00, 0x00]; // uint 1 LE

        using (var output = new IndexOutput(path))
        {
            CodecFileHeader.Write(output, CodecFormats.FieldLengths, body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        var (value, version) = CodecFileHeader.Read(input, CodecFormats.FieldLengths, Codec.UInt32LE);

        Assert.Equal(CodecConstants.FieldLengthVersion, version);
        Assert.Equal(1u, value);
    }

    // ═══════════════════════════════════════════════════
    //  Error path tests
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Truncated version byte (empty file) via BinaryReader throws")]
    public void BinaryReader_EmptyFile_Throws()
    {
        var path = TempFile("br_trunc_version.dat");
        File.WriteAllBytes(path, []);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            Assert.ThrowsAny<Exception>(() =>
                CodecFileHeader.Read(reader, CodecFormats.Postings));
        }
    }

    [Fact(DisplayName = "Truncated version byte (empty file) via IndexInput throws")]
    public void IndexInput_EmptyFile_Throws()
    {
        var path = TempFile("ii_trunc_version.dat");
        File.WriteAllBytes(path, []);

        using var input = new IndexInput(path);
        Assert.ThrowsAny<Exception>(() =>
            CodecFileHeader.Read(input, CodecFormats.Postings));
    }

    [Fact(DisplayName = "Truncated VarInt via BinaryReader (ReadVersion) throws")]
    public void BinaryReader_TruncatedVarInt_Throws()
    {
        var path = TempFile("br_trunc_varint.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(1);     // version
            fs.WriteByte(0x80);  // VarInt continuation with no termination
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            Assert.ThrowsAny<Exception>(() =>
                CodecFileHeader.ReadVersion(reader, CodecFormats.Postings));
        }
    }

    [Fact(DisplayName = "Truncated VarInt via IndexInput (Read) throws")]
    public void IndexInput_TruncatedVarInt_Throws()
    {
        var path = TempFile("ii_trunc_varint.dat");
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1);     // version
            output.WriteByte(0x80);  // truncated VarInt
            output.Flush();
        }

        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() =>
            CodecFileHeader.Read(input, CodecFormats.Postings));
    }

    [Fact(DisplayName = "Malformed VarInt >10 continuation bytes via BinaryReader throws")]
    public void BinaryReader_MalformedVarInt_Exceeds10Bytes_Throws()
    {
        var path = TempFile("br_malformed_varint.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(1); // version
            for (int i = 0; i < 11; i++)
                fs.WriteByte(0x80); // 11 continuation bytes
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            Assert.Throws<InvalidDataException>(() =>
                CodecFileHeader.ReadVersion(reader, CodecFormats.Postings));
        }
    }

    [Fact(DisplayName = "Malformed VarInt >10 continuation bytes via IndexInput throws")]
    public void IndexInput_MalformedVarInt_Exceeds10Bytes_Throws()
    {
        var path = TempFile("ii_malformed_varint.dat");
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1);
            for (int i = 0; i < 11; i++)
                output.WriteByte(0x80);
            output.Flush();
        }

        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() =>
            CodecFileHeader.ReadVersion(input, CodecFormats.Postings));
    }

    [Fact(DisplayName = "Unknown version (forward compat) succeeds and returns raw body")]
    public void BinaryReader_UnknownVersion_Succeeds()
    {
        var path = TempFile("br_unknown_version.dat");
        byte[] body = [0xAA, 0xBB];
        byte unknownVersion = 10;

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(unknownVersion);
            WriteZigZagVarInt64(fs, body.Length);
            fs.Write(body);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            var result = CodecFileHeader.Read(reader, CodecFormats.Postings);

            Assert.Equal(unknownVersion, result.Version);
            Assert.Equal(body, result.Body);
        }
    }

    [Fact(DisplayName = "Unknown version via IndexInput succeeds and returns raw body")]
    public void IndexInput_UnknownVersion_Succeeds()
    {
        var path = TempFile("ii_unknown_version.dat");
        byte[] body = [0xCC, 0xDD];
        byte unknownVersion = 5;

        using (var output = new IndexOutput(path))
        {
            output.WriteByte(unknownVersion);
            WriteZigZagVarInt64(output, body.Length);
            output.WriteBytes(body);
            output.Flush();
        }

        using var input = new IndexInput(path);
        var result = CodecFileHeader.Read(input, CodecFormats.Postings);

        Assert.Equal(unknownVersion, result.Version);
        Assert.Equal(body, result.Body);
    }
    // ═══════════════════════════════════════════════════
    //  VarInt64 body-length edge cases
    // ═══════════════════════════════════════════════════

    private static void WriteBinaryHeader(BinaryWriter w, byte version, long bodyLen)
    {
        w.Write(version);
        ulong v = (ulong)bodyLen;
        while (v >= 0x80)
        {
            w.Write((byte)(v | 0x80));
            v >>= 7;
        }
        w.Write((byte)v);
    }

    [Fact(DisplayName = "Body-length VarInt correct for 0 (1 byte)")]
    public void BodyLength_VarInt_0_Is1Byte()
    {
        var path = TempFile("varlen_0.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            WriteBinaryHeader(w, CodecConstants.PostingsVersion, 0);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(r, CodecFormats.Postings);
            Assert.Equal(CodecConstants.PostingsVersion, version);
            Assert.Equal(2, fs.Position); // 1 version + 1 VarInt
        }
    }

    [Fact(DisplayName = "Body-length VarInt correct for 127 (1 byte)")]
    public void BodyLength_VarInt_127_Is1Byte()
    {
        var path = TempFile("varlen_127.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            WriteBinaryHeader(w, CodecConstants.PostingsVersion, 127);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(r, CodecFormats.Postings);
            Assert.Equal(CodecConstants.PostingsVersion, version);
            Assert.Equal(2, fs.Position);
        }
    }

    [Fact(DisplayName = "Body-length VarInt correct for 128 (2 bytes)")]
    public void BodyLength_VarInt_128_Is2Bytes()
    {
        var path = TempFile("varlen_128.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            WriteBinaryHeader(w, CodecConstants.PostingsVersion, 128);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(r, CodecFormats.Postings);
            Assert.Equal(CodecConstants.PostingsVersion, version);
            Assert.Equal(3, fs.Position); // 1 version + 2 VarInt
        }
    }

    [Fact(DisplayName = "Body-length VarInt correct for 16383 (2 bytes)")]
    public void BodyLength_VarInt_16383_Is2Bytes()
    {
        var path = TempFile("varlen_16383.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            WriteBinaryHeader(w, CodecConstants.PostingsVersion, 16383);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(r, CodecFormats.Postings);
            Assert.Equal(CodecConstants.PostingsVersion, version);
            Assert.Equal(3, fs.Position);
        }
    }

    [Fact(DisplayName = "Body-length VarInt correct for 16384 (3 bytes)")]
    public void BodyLength_VarInt_16384_Is3Bytes()
    {
        var path = TempFile("varlen_16384.dat");
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            WriteBinaryHeader(w, CodecConstants.PostingsVersion, 16384);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var r = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            byte version = CodecFileHeader.ReadVersion(r, CodecFormats.Postings);
            Assert.Equal(CodecConstants.PostingsVersion, version);
            Assert.Equal(4, fs.Position);
        }
    }

    // ═══════════════════════════════════════════════════
    //  All CodecKit-envelope formats produce correct version byte
    // ═══════════════════════════════════════════════════

    private static readonly (ICodec<byte[]> Format, byte ExpectedVersion, string Name)[] AllFormats =
    [
        (CodecFormats.Norms, CodecConstants.NormsVersion, "Norms"),
        (CodecFormats.FieldLengths, CodecConstants.FieldLengthVersion, "FieldLengths"),
        (CodecFormats.NumericDocValues, CodecConstants.NumericDocValuesVersion, "NumericDocValues"),
        (CodecFormats.SortedDocValues, CodecConstants.SortedDocValuesVersion, "SortedDocValues"),
        (CodecFormats.BinaryDocValues, CodecConstants.BinaryDocValuesVersion, "BinaryDocValues"),
        (CodecFormats.SortedSetDocValues, CodecConstants.SortedSetDocValuesVersion, "SortedSetDocValues"),
        (CodecFormats.SortedNumericDocValues, CodecConstants.SortedNumericDocValuesVersion, "SortedNumericDocValues"),
        (CodecFormats.Postings, CodecConstants.PostingsVersion, "Postings"),
        (CodecFormats.TermVectors, CodecConstants.TermVectorsVersion, "TermVectors"),
        (CodecFormats.TermDictionary, CodecConstants.TermDictionaryVersion, "TermDictionary"),
        (CodecFormats.Hnsw, CodecConstants.HnswVersion, "Hnsw"),
        (CodecFormats.Vectors, CodecConstants.VectorVersion, "Vectors"),
        (CodecFormats.QuantisedVectors, CodecConstants.QuantisedVectorVersion, "QuantisedVectors"),
        (CodecFormats.Bkd, CodecConstants.BKDVersion, "Bkd"),
        (CodecFormats.RoaringBitmap, CodecConstants.RoaringBitmapVersion, "RoaringBitmap"),
    ];

    [Fact(DisplayName = "All CodecKit-envelope formats produce correct version byte in header")]
    public void AllEnvelopeFormats_ProduceCorrectVersion()
    {
        foreach (var (format, expectedVersion, name) in AllFormats)
        {
            var path = TempFile($"fmt_{name}.dat");
            byte[] body = [0x42];

            using (var output = new IndexOutput(path))
            {
                CodecFileHeader.Write(output, format, body);
                output.Flush();
            }

            using var input = new IndexInput(path);
            // Verify first byte is the version
            byte versionByte = input.ReadByte();
            Assert.Equal(expectedVersion, versionByte);
        }
    }

    [Fact(DisplayName = "Each format's version constant matches CodecConstants")]
    public void EachFormat_VersionMatchesReadVersion()
    {
        foreach (var (format, expectedVersion, name) in AllFormats)
        {
            var path = TempFile($"fmt_readv_{name}.dat");
            byte[] body = [0x42];

            using (var output = new IndexOutput(path))
            {
                CodecFileHeader.Write(output, format, body);
                output.Flush();
            }

            using var input = new IndexInput(path);
            byte version = CodecFileHeader.ReadVersion(input, format);
            Assert.Equal(expectedVersion, version);
        }
    }

    /// <summary>Writes a ZigZag-encoded VarInt64 to a stream (matches Codec.VarInt64 wire format).</summary>
    private static void WriteZigZagVarInt64(Stream stream, long value)
    {
        ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
        while (zigzag >= 0x80)
        {
            stream.WriteByte((byte)(zigzag | 0x80));
            zigzag >>= 7;
        }
        stream.WriteByte((byte)zigzag);
    }

    /// <summary>Writes a ZigZag-encoded VarInt64 to an IndexOutput.</summary>
    private static void WriteZigZagVarInt64(IndexOutput output, long value)
    {
        ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
        while (zigzag >= 0x80)
        {
            output.WriteByte((byte)(zigzag | 0x80));
            zigzag >>= 7;
        }
        output.WriteByte((byte)zigzag);
    }
}
