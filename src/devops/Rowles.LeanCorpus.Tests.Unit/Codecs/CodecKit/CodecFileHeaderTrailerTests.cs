using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecFileHeaderTrailerTests : IDisposable
{
    private readonly string _tempDirectory;

    public CodecFileHeaderTrailerTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LeanCorpus_TrailerTests",
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
    //  Round-trip tests
    // ═══════════════════════════════════════════════════

    [Theory(DisplayName = "Trailer round-trip via BeginStreamingWrite + ReadBody")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1_048_576)]
    public void Trailer_RoundTrip_WritesAndReadsCorrectly(int bodySize)
    {
        byte version = 42;
        var original = new byte[bodySize];
        for (int i = 0; i < bodySize; i++) original[i] = (byte)(i & 0xFF);

        var path = TempFile($"roundtrip_{bodySize}.dat");

        // Write via trailer
        using (var output = new IndexOutput(path, durable: false))
        using (var scope = CodecFileHeader.BeginStreamingWrite(output, version))
        {
            if (bodySize > 0)
                scope.Output.WriteBytes(original);
        }

        // Read via ReadBody
        using var input = new IndexInput(path);
        var result = CodecFileHeader.ReadBody(input);

        Assert.Equal(version, result.Version);
        Assert.Equal(original, result.Body);
    }

    // ═══════════════════════════════════════════════════
    //  Envelope fallback tests
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ReadBody reads old envelope format correctly")]
    public void Trailer_EnvelopeFallback_ReadsOldFormatCorrectly()
    {
        var original = new byte[] { 1, 2, 3, 4, 5 };
        var path = TempFile("envelope_fallback.dat");

        // Write via old envelope path
        using (var output = new IndexOutput(path, durable: false))
            CodecFileHeader.Write(output, CodecFormats.BinaryDocValues, original);

        // Read via ReadBody — should detect envelope format
        using var input = new IndexInput(path);
        var result = CodecFileHeader.ReadBody(input);

        Assert.Equal(CodecConstants.BinaryDocValuesVersion, result.Version);
        Assert.Equal(original, result.Body);
    }

    [Fact(DisplayName = "ReadBody correctly detects trailer then envelope in same reader")]
    public void Trailer_DetectsTrailerThenEnvelope_InSameReader()
    {
        var original = new byte[] { 9, 8, 7 };

        // Write a trailer file
        var trailerPath = TempFile("detect_trailer.dat");
        using (var output = new IndexOutput(trailerPath, durable: false))
        using (var scope = CodecFileHeader.BeginStreamingWrite(output, 77))
        {
            scope.Output.WriteBytes(original);
        }

        // Write an envelope file
        var envelopePath = TempFile("detect_envelope.dat");
        using (var output = new IndexOutput(envelopePath, durable: false))
            CodecFileHeader.Write(output, CodecFormats.BinaryDocValues, original);

        // Read both through the same code path
        using (var input = new IndexInput(trailerPath))
        {
            var trailerResult = CodecFileHeader.ReadBody(input);
            Assert.Equal(77, trailerResult.Version);
            Assert.Equal(original, trailerResult.Body);
        }

        using (var input = new IndexInput(envelopePath))
        {
            var envelopeResult = CodecFileHeader.ReadBody(input);
            Assert.Equal(CodecConstants.BinaryDocValuesVersion, envelopeResult.Version);
            Assert.Equal(original, envelopeResult.Body);
        }
    }

    // ═══════════════════════════════════════════════════
    //  Error / corruption tests
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ReadBody throws on truncated file")]
    public void Trailer_RejectsTruncatedFile()
    {
        var path = TempFile("truncated.dat");

        // Write a valid trailer file, then truncate to just the version byte.
        var original = new byte[] { 10, 20, 30, 40 };
        using (var output = new IndexOutput(path, durable: false))
        using (var scope = CodecFileHeader.BeginStreamingWrite(output, 1))
        {
            scope.Output.WriteBytes(original);
        }

        // Truncate to 1 byte: version only, no body, no trailer.
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            fs.SetLength(1);

        // ReadBody should fail: trailer check fails (file too short),
        // envelope fallback fails because ReadVarInt64 can't read past EOF.
        using var input = new IndexInput(path);
        Assert.ThrowsAny<Exception>(() => CodecFileHeader.ReadBody(input));
    }

    [Fact(DisplayName = "ReadBody rejects corrupt trailer length")]
    public void Trailer_RejectsCorruptTrailerLength()
    {
        var original = new byte[] { 1, 2, 3 };
        var path = TempFile("corrupt_trailer.dat");

        using (var output = new IndexOutput(path, durable: false))
        using (var scope = CodecFileHeader.BeginStreamingWrite(output, 99))
        {
            scope.Output.WriteBytes(original);
        }

        // Corrupt the last 8 bytes to an inconsistent value
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            fs.Seek(-8, SeekOrigin.End);
            fs.Write(BitConverter.GetBytes(999_999L));
        }

        // Trailer check fails (inconsistent), falls back to envelope
        // Envelope parsing fails because bodyLen is garbage
        using var input = new IndexInput(path);
        Assert.Throws<InvalidDataException>(() => CodecFileHeader.ReadBody(input));
    }

    [Fact(DisplayName = "ReadBody handles 9-byte empty-body trailer file")]
    public void Trailer_ReadBody_EmptyFile()
    {
        var path = TempFile("empty_body.dat");

        // 9 bytes: version(1) + body(0) + trailer(8)
        using (var output = new IndexOutput(path, durable: false))
        using (var scope = CodecFileHeader.BeginStreamingWrite(output, 7))
        {
            // no body written
        }

        using var input = new IndexInput(path);
        var result = CodecFileHeader.ReadBody(input);

        Assert.Equal(7, result.Version);
        Assert.Empty(result.Body);
        Assert.Equal(9L, input.Length);
    }

    // ═══════════════════════════════════════════════════
    //  ReadVersionAndSkipHeader positioning tests
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ReadVersionAndSkipHeader positions at body start for trailer format")]
    public void ReadVersionAndSkipHeader_PositionsAtBodyStart_Trailer()
    {
        var body = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var path = TempFile("skip_trailer.dat");

        using (var output = new IndexOutput(path, durable: false))
        using (var scope = CodecFileHeader.BeginStreamingWrite(output, 55))
        {
            scope.Output.WriteBytes(body);
        }

        using var input = new IndexInput(path);
        byte version = CodecFileHeader.ReadVersionAndSkipHeader(input);

        Assert.Equal(55, version);
        Assert.Equal(1L, input.Position); // body starts immediately after version byte
        Assert.Equal(body[0], input.ReadByte());
    }

    [Fact(DisplayName = "ReadVersionAndSkipHeader positions at body start for envelope format")]
    public void ReadVersionAndSkipHeader_PositionsAtBodyStart_Envelope()
    {
        var body = new byte[] { 0x11, 0x22, 0x33 };
        var path = TempFile("skip_envelope.dat");

        using (var output = new IndexOutput(path, durable: false))
            CodecFileHeader.Write(output, CodecFormats.BinaryDocValues, body);

        using var input = new IndexInput(path);
        byte version = CodecFileHeader.ReadVersionAndSkipHeader(input);

        Assert.Equal(CodecConstants.BinaryDocValuesVersion, version);
        // Body should start after version(1) + VarInt64 bodyLen
        Assert.True(input.Position > 1L);
        Assert.Equal(body[0], input.ReadByte());
    }

    // BinaryReader ReadVersionAndSkipHeader omitted for Phase A —
    // the BinaryReader overload is envelope-only until Phase C.}
}
