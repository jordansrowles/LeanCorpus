using System.Buffers;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecFileFrameTests : IDisposable
{
    private const string FormatId = "test.frame";
    private readonly string _tempDirectory;

    public CodecFileFrameTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LeanCorpus_CodecFileFrameTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDirectory))
            return;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        try { Directory.Delete(_tempDirectory, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact(DisplayName = "Frame v1 empty body has exact golden bytes")]
    public void GoldenBytes_EmptyBody()
    {
        string path = WriteFrame("empty.lccf", [], CodecFileChecksumAlgorithm.XxHash64);

        Assert.Equal(Hex(
            "4c434346 01 0a 07000000 00000000 03 00 746573742e6672616d65 " +
            "0000000000000000 99e9d85137db46ef"), File.ReadAllBytes(path));
    }

    [Fact(DisplayName = "Frame v1 one-byte body has exact golden bytes")]
    public void GoldenBytes_OneByteBody()
    {
        string path = WriteFrame("one-byte.lccf", [0x2a], CodecFileChecksumAlgorithm.XxHash64);

        Assert.Equal(Hex(
            "4c434346 01 0a 07000000 00000000 03 00 746573742e6672616d65 2a " +
            "0100000000000000 e43ab0becede9e0a"), File.ReadAllBytes(path));
    }

    [Theory(DisplayName = "Frame v1 checksum modes have exact golden bytes")]
    [InlineData(CodecFileChecksumAlgorithm.None, "0000000000000000")]
    [InlineData(CodecFileChecksumAlgorithm.Crc32, "c241243500000000")]
    [InlineData(CodecFileChecksumAlgorithm.XxHash32, "ff53d13200000000")]
    [InlineData(CodecFileChecksumAlgorithm.XxHash64, "990977adf52cbc44")]
    public void GoldenBytes_AllChecksumModes(
        CodecFileChecksumAlgorithm algorithm,
        string expectedChecksum)
    {
        string path = WriteFrame($"checksum-{algorithm}.lccf", "abc"u8.ToArray(), algorithm);

        byte[] expected = Hex(
            $"4c434346 01 0a 07000000 00000000 {(byte)algorithm:x2} 00 " +
            $"746573742e6672616d65 616263 0300000000000000 {expectedChecksum}");
        Assert.Equal(expected, File.ReadAllBytes(path));

        using var input = new IndexInput(path);
        using CodecReadSession session = CodecFileReader.Open(input);
        Assert.Equal("abc"u8.ToArray(), session.ReadBody());
    }

    [Fact(DisplayName = "Frame v1 accepts and round-trips a 64-byte format ID")]
    public void MaximumFormatId_RoundTrips()
    {
        string formatId = "a." + new string('b', 62);
        string path = WriteFrame("maximum-format-id.lccf", [0x01], formatId: formatId);

        using var input = new IndexInput(path);
        using CodecReadSession session = CodecFileReader.Open(input, expectedFormatId: formatId);

        Assert.Equal(64, session.Metadata.FormatId.Length);
        Assert.Equal(80, session.Metadata.BodyStart);
        Assert.Equal([0x01], session.ReadBody());
    }

    [Theory(DisplayName = "Frame writer rejects invalid format IDs")]
    [InlineData("frame")]
    [InlineData("Test.frame")]
    [InlineData("test..frame")]
    [InlineData("test._frame")]
    [InlineData("test.frame_")]
    [InlineData("test.frame:body")]
    public void Begin_InvalidFormatId_Throws(string formatId)
    {
        string path = TempFile("invalid-format-id.lccf");
        using var output = new IndexOutput(path, durable: false);

        Assert.Throws<ArgumentException>(() => CodecFileWriter.Begin(output, formatId, 1));
    }

    [Fact(DisplayName = "Frame writer rejects a format ID longer than 64 bytes")]
    public void Begin_ExcessiveFormatId_Throws()
    {
        string path = TempFile("long-format-id.lccf");
        using var output = new IndexOutput(path, durable: false);

        Assert.Throws<ArgumentException>(() =>
            CodecFileWriter.Begin(output, "a." + new string('b', 63), 1));
    }

    [Fact(DisplayName = "Complete finalises metadata and closes the body output")]
    public void Complete_FinalisesSessionExplicitly()
    {
        string path = TempFile("explicit-complete.lccf");
        using var output = new IndexOutput(path, durable: false);
        using CodecWriteSession session = CodecFileWriter.Begin(output, FormatId, 7);

        session.Output.WriteBytes([0x10, 0x20, 0x30]);
        session.Complete();

        Assert.Equal(3, session.Metadata.BodyLength);
        Assert.NotEqual(0ul, session.Metadata.StoredChecksum);
        Assert.Throws<InvalidOperationException>(() => session.Output.WriteByte(0x40));
        Assert.Throws<InvalidOperationException>(() => session.Complete());
    }

    [Fact(DisplayName = "Disposal without Complete never writes a footer")]
    public void Dispose_WithoutComplete_DoesNotWriteFooter()
    {
        string path = TempFile("incomplete.lccf");
        using (var output = new IndexOutput(path, durable: false))
        {
            CodecWriteSession session = CodecFileWriter.Begin(output, FormatId, 7);
            session.Output.WriteBytes([0xaa, 0xbb]);
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.Complete());
            Assert.Throws<ObjectDisposedException>(() => session.Output.WriteByte(0xcc));
        }

        Assert.Equal(Hex(
            "4c434346 01 0a 07000000 00000000 03 00 746573742e6672616d65 aabb"),
            File.ReadAllBytes(path));

        using var input = new IndexInput(path);
        AssertError(CodecFileErrorCode.TruncatedHeader, () => CodecFileReader.Open(input));
    }

    [Fact(DisplayName = "Incremental checksum is independent of write chunk boundaries")]
    public void IncrementalChecksum_ChunkBoundariesMatchSingleWrite()
    {
        byte[] body = Enumerable.Range(0, 257).Select(value => (byte)value).ToArray();

        foreach (CodecFileChecksumAlgorithm algorithm in Enum.GetValues<CodecFileChecksumAlgorithm>())
        {
            string oneWritePath = WriteFrame($"single-{algorithm}.lccf", body, algorithm);
            string chunkedPath = TempFile($"chunked-{algorithm}.lccf");
            using (var output = new IndexOutput(chunkedPath, durable: false))
            using (CodecWriteSession session = CodecFileWriter.Begin(output, FormatId, 7, checksumAlgorithm: algorithm))
            {
                int[] chunkSizes = [1, 15, 16, 17, 31, 32, 33, 64, 48];
                int offset = 0;
                foreach (int chunkSize in chunkSizes)
                {
                    session.Output.WriteBytes(body.AsSpan(offset, chunkSize));
                    offset += chunkSize;
                }
                Assert.Equal(body.Length, offset);
                session.Complete();
            }

            Assert.Equal(File.ReadAllBytes(oneWritePath), File.ReadAllBytes(chunkedPath));
        }
    }

    [Fact(DisplayName = "IBufferWriter writes participate in the incremental checksum")]
    public void BufferWriter_Advance_ParticipatesInChecksum()
    {
        byte[] body = Enumerable.Range(0, 96).Select(value => (byte)(value * 3)).ToArray();
        string expectedPath = WriteFrame("buffer-expected.lccf", body);
        string actualPath = TempFile("buffer-actual.lccf");

        using (var output = new IndexOutput(actualPath, durable: false))
        using (CodecWriteSession session = CodecFileWriter.Begin(output, FormatId, 7))
        {
            IBufferWriter<byte> writer = session.Output;
            body.CopyTo(writer.GetSpan(body.Length));
            writer.Advance(body.Length);
            session.Complete();
        }

        Assert.Equal(File.ReadAllBytes(expectedPath), File.ReadAllBytes(actualPath));
    }

    [Fact(DisplayName = "Open rejects invalid frame magic")]
    public void Open_InvalidMagic_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("invalid-magic.lccf", bytes => bytes[0] ^= 0xff);

        AssertOpenError(path, CodecFileErrorCode.InvalidMagic);
    }

    [Fact(DisplayName = "Open rejects unsupported frame version")]
    public void Open_UnsupportedFrameVersion_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("future-frame.lccf", bytes => bytes[4] = 2);

        AssertOpenError(path, CodecFileErrorCode.UnsupportedFrameVersion);
    }

    [Fact(DisplayName = "Open rejects a truncated fixed header")]
    public void Open_TruncatedHeader_ThrowsStructuredError()
    {
        string path = TempFile("truncated-header.lccf");
        File.WriteAllBytes(path, Hex("4c434346 01 0a 0700"));

        AssertOpenError(path, CodecFileErrorCode.TruncatedHeader);
    }

    [Fact(DisplayName = "Open rejects a truncated format ID")]
    public void Open_TruncatedFormatIdentifier_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("truncated-format-id.lccf", bytes => bytes[5] = 64);

        AssertOpenError(path, CodecFileErrorCode.TruncatedHeader);
    }

    [Theory(DisplayName = "Open rejects invalid format ID lengths")]
    [InlineData(0)]
    [InlineData(65)]
    public void Open_InvalidFormatIdentifierLength_ThrowsStructuredError(byte identifierLength)
    {
        string path = CorruptValidFrame(
            $"invalid-format-length-{identifierLength}.lccf",
            bytes => bytes[5] = identifierLength);

        AssertOpenError(path, CodecFileErrorCode.InvalidFormatIdentifier);
    }

    [Fact(DisplayName = "Open rejects invalid format ID syntax")]
    public void Open_InvalidFormatIdentifierSyntax_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("invalid-format-syntax.lccf", bytes => bytes[16] = (byte)'T');

        AssertOpenError(path, CodecFileErrorCode.InvalidFormatIdentifier);
    }

    [Fact(DisplayName = "Open rejects non-positive format version")]
    public void Open_InvalidFormatVersion_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("invalid-format-version.lccf", bytes => Array.Clear(bytes, 6, 4));

        AssertOpenError(path, CodecFileErrorCode.UnsupportedFormatVersion);
    }

    [Fact(DisplayName = "Open rejects unsupported format version")]
    public void Open_UnsupportedFormatVersion_ThrowsStructuredError()
    {
        string path = WriteFrame("unsupported-format-version.lccf", [0x01]);
        using var input = new IndexInput(path);

        AssertError(
            CodecFileErrorCode.UnsupportedFormatVersion,
            () => CodecFileReader.Open(input, supportedFormatVersions: new HashSet<int> { 6 }));
    }

    [Fact(DisplayName = "Open rejects unsupported flags")]
    public void Open_InvalidFlags_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("invalid-flags.lccf", bytes => bytes[10] = 1);

        AssertOpenError(path, CodecFileErrorCode.InvalidFlags);
    }

    [Fact(DisplayName = "Open rejects a non-zero reserved header byte")]
    public void Open_InvalidReservedByte_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("invalid-reserved.lccf", bytes => bytes[15] = 1);

        AssertOpenError(path, CodecFileErrorCode.InvalidFlags);
    }

    [Fact(DisplayName = "Open rejects an unknown checksum algorithm")]
    public void Open_UnknownChecksumAlgorithm_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("invalid-checksum-algorithm.lccf", bytes => bytes[14] = 4);

        AssertOpenError(path, CodecFileErrorCode.UnsupportedChecksumAlgorithm);
    }

    [Fact(DisplayName = "Open rejects a truncated footer")]
    public void Open_TruncatedFooter_ThrowsStructuredError()
    {
        string completePath = WriteFrame("complete-before-truncation.lccf", [0x01]);
        byte[] bytes = File.ReadAllBytes(completePath);
        string path = TempFile("truncated-footer.lccf");
        File.WriteAllBytes(path, bytes[..(16 + FormatId.Length + 15)]);

        AssertOpenError(path, CodecFileErrorCode.TruncatedBody);
    }

    [Theory(DisplayName = "Open rejects impossible body lengths")]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(long.MaxValue)]
    public void Open_InvalidBodyLength_ThrowsStructuredError(long bodyLength)
    {
        string path = WriteFrame("invalid-body-length.lccf", [0x01, 0x02]);
        byte[] bytes = File.ReadAllBytes(path);
        BitConverter.GetBytes(bodyLength).CopyTo(bytes, bytes.Length - 16);
        File.WriteAllBytes(path, bytes);

        AssertOpenError(path, CodecFileErrorCode.InvalidBodyLength);
    }

    [Fact(DisplayName = "Open rejects trailing garbage after a complete frame")]
    public void Open_TrailingGarbage_ThrowsStructuredError()
    {
        string completePath = WriteFrame("complete-before-garbage.lccf", [0x01, 0x02]);
        string path = TempFile("trailing-garbage.lccf");
        File.WriteAllBytes(path, [.. File.ReadAllBytes(completePath), 0xff]);

        AssertOpenError(path, CodecFileErrorCode.InvalidBodyLength);
    }

    [Theory(DisplayName = "Open rejects reserved high checksum bits for 32-bit modes")]
    [InlineData(CodecFileChecksumAlgorithm.None)]
    [InlineData(CodecFileChecksumAlgorithm.Crc32)]
    [InlineData(CodecFileChecksumAlgorithm.XxHash32)]
    public void Open_InvalidChecksumFooterRepresentation_ThrowsStructuredError(
        CodecFileChecksumAlgorithm algorithm)
    {
        string path = WriteFrame("invalid-checksum-footer.lccf", [0x01], algorithm);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] = 1;
        File.WriteAllBytes(path, bytes);

        AssertOpenError(path, CodecFileErrorCode.ChecksumMismatch);
    }

    [Fact(DisplayName = "Checksum validation rejects a body bit flip")]
    public void ValidateChecksum_CorruptBody_ThrowsStructuredError()
    {
        string path = CorruptValidFrame(
            "corrupt-body.lccf",
            bytes => bytes[16 + FormatId.Length + 1] ^= 0x40,
            [0x01, 0x02, 0x03]);

        using var input = new IndexInput(path);
        using CodecReadSession session = CodecFileReader.Open(input);
        AssertError(CodecFileErrorCode.ChecksumMismatch, session.ValidateChecksum);
    }

    [Fact(DisplayName = "Checksum validation rejects a corrupt checksum field")]
    public void ValidateChecksum_CorruptChecksum_ThrowsStructuredError()
    {
        string path = CorruptValidFrame("corrupt-checksum.lccf", bytes => bytes[^8] ^= 0x01);

        using var input = new IndexInput(path);
        using CodecReadSession session = CodecFileReader.Open(input);
        AssertError(CodecFileErrorCode.ChecksumMismatch, session.ValidateChecksum);
    }

    [Fact(DisplayName = "Open rejects an unexpected format ID")]
    public void Open_FormatMismatch_ThrowsStructuredError()
    {
        string path = WriteFrame("format-mismatch.lccf", [0x01]);
        using var input = new IndexInput(path);

        CodecFileException exception = AssertError(
            CodecFileErrorCode.FormatMismatch,
            () => CodecFileReader.Open(input, expectedFormatId: "other.frame"));
        Assert.Equal(FormatId, exception.FormatId);
    }

    [Fact(DisplayName = "Open enforces the codec-file size limit before parsing")]
    public void Open_MaxCodecFileBytes_ThrowsStructuredError()
    {
        string path = WriteFrame("file-limit.lccf", [0x01]);
        using var input = new IndexInput(path);
        var options = new CodecOptions { MaxCodecFileBytes = input.Length - 1 };

        AssertError(CodecFileErrorCode.LimitExceeded, () => CodecFileReader.Open(input, options));
        Assert.Equal(0, input.Position);
    }

    [Fact(DisplayName = "ReadBody enforces its materialisation limit")]
    public void ReadBody_MaxMaterialisedBodyBytes_ThrowsStructuredError()
    {
        string path = WriteFrame("body-limit.lccf", [0x01, 0x02]);
        using var input = new IndexInput(path);
        var options = new CodecOptions { MaxMaterialisedBodyBytes = 1 };
        using CodecReadSession session = CodecFileReader.Open(input, options);

        AssertError(CodecFileErrorCode.LimitExceeded, () => session.ReadBody());
        Assert.Equal(session.Metadata.BodyStart, input.Position);
    }

    [Fact(DisplayName = "Open does not scan or validate the body checksum")]
    public void Open_DoesNotScanBodyChecksum()
    {
        string path = CorruptValidFrame(
            "fast-open-corrupt-body.lccf",
            bytes => bytes[16 + FormatId.Length] ^= 0xff,
            [0x01, 0x02, 0x03]);

        using var input = new IndexInput(path);
        using CodecReadSession session = CodecFileReader.Open(input);

        Assert.Equal(3, session.Metadata.BodyLength);
        Assert.Equal(session.Metadata.BodyStart, input.Position);
        AssertError(CodecFileErrorCode.ChecksumMismatch, session.ValidateChecksum);
    }

    private string WriteFrame(
        string name,
        byte[] body,
        CodecFileChecksumAlgorithm algorithm = CodecFileChecksumAlgorithm.XxHash64,
        string formatId = FormatId)
    {
        string path = TempFile(name);
        using var output = new IndexOutput(path, durable: false);
        using CodecWriteSession session = CodecFileWriter.Begin(
            output,
            formatId,
            formatVersion: 7,
            checksumAlgorithm: algorithm);
        session.Output.WriteBytes(body);
        session.Complete();
        return path;
    }

    private string CorruptValidFrame(
        string name,
        Action<byte[]> corrupt,
        byte[]? body = null)
    {
        string path = WriteFrame(name, body ?? [0x01]);
        byte[] bytes = File.ReadAllBytes(path);
        corrupt(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private void AssertOpenError(string path, CodecFileErrorCode expected)
    {
        using var input = new IndexInput(path);
        AssertError(expected, () => CodecFileReader.Open(input));
    }

    private static CodecFileException AssertError(
        CodecFileErrorCode expected,
        Action action)
    {
        CodecFileException exception = Assert.Throws<CodecFileException>(action);
        Assert.Equal(expected, exception.ErrorCode);
        return exception;
    }

    private static byte[] Hex(string value)
        => Convert.FromHexString(value.Replace(" ", string.Empty, StringComparison.Ordinal));

    private string TempFile(string name) => Path.Combine(_tempDirectory, name);
}
