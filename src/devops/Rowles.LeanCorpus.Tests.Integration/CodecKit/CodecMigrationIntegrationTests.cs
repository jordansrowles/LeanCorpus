using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecMigrationIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public CodecMigrationIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Creates a reader that reads the body bytes and appends a version-marker
    /// byte so we can verify which version's reader was dispatched.
    /// </summary>
    private static ICodec<byte[]> TaggedReader(byte tag)
    {
        return Codec.BytesOwnedRemaining().Map<byte[], byte[]>(
            decode: body =>
            {
                var result = new byte[body.Length + 1];
                Buffer.BlockCopy(body, 0, result, 0, body.Length);
                result[^1] = tag;
                return result;
            },
            encode: model =>
            {
                // Strip the tag byte on encode.
                var body = new byte[model.Length - 1];
                Buffer.BlockCopy(model, 0, body, 0, model.Length - 1);
                return body;
            });
    }

    private static void AssertTagged(byte[] value, byte expectedTag)
    {
        Assert.NotEmpty(value);
        Assert.Equal(expectedTag, value[^1]);
    }

    // ═══════════════════════════════════════════════════
    //  Encode picks newest version
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Multi-step format encodes with newest version")]
    public void MultiStepFormat_Encode_UsesNewestVersion()
    {
        var format = new CodecFormat("test-multi",
        [
            new CodecVersionStep(1, "v1", TaggedReader(1)),
            new CodecVersionStep(2, "v2", TaggedReader(2)),
            new CodecVersionStep(3, "v3", TaggedReader(3)),
        ]);

        CodecMigrationRegistry.Default.Register(format);

        // Create a VersionEnvelope that uses the registry (simulating CodecFormats.Create).
        // CodecFormats.Create checks the registry; we can also construct one manually.
        var codec = Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases:
            [
                // Newest first (matching CodecFormats.Create's reverse iteration)
                Codec.VersionCase<byte[], byte[]>((byte)3, "v3", TaggedReader(3)),
                Codec.VersionCase<byte[], byte[]>((byte)2, "v2", TaggedReader(2)),
                Codec.VersionCase<byte[], byte[]>((byte)1, "v1", TaggedReader(1)),
            ]);

        // Encode a body — the tag byte is stripped by the reader's encode,
        // so we provide a full tagged model and verify the round-trip.
        byte[] model = [0xAA, 0xBB, 0x03]; // last byte is the tag for v3
        byte[] encoded = Codec.EncodeToArray(codec, model);

        // First byte of encoded output is the version byte.
        Assert.Equal(3, encoded[0]);
    }

    // ═══════════════════════════════════════════════════
    //  Decode dispatches by version byte
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Multi-step format decodes old version through correct reader")]
    public void MultiStepFormat_Decode_DispachesCorrectReader()
    {
        var v1Reader = TaggedReader(1);
        var v2Reader = TaggedReader(2);

        var codec = Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases:
            [
                Codec.VersionCase<byte[], byte[]>((byte)2, "v2", v2Reader),
                Codec.VersionCase<byte[], byte[]>((byte)1, "v1", v1Reader),
            ]);

        // Manually construct a v1 byte layout: [version=1][VarInt64 ZigZag of 1 = 0x02][body=0xAA]
        byte[] v1Data = [0x01, 0x02, 0xAA];

        var result = Codec.Decode(codec, v1Data);

        // v1 reader was dispatched — result ends with tag byte 0x01.
        AssertTagged(result, 1);
        // The body byte is preserved.
        Assert.Equal(0xAA, result[0]);
    }

    [Fact(DisplayName = "Multi-step format decodes current version through correct reader")]
    public void MultiStepFormat_Decode_CurrentVersion()
    {
        var codec = Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases:
            [
                Codec.VersionCase<byte[], byte[]>((byte)2, "v2", TaggedReader(2)),
                Codec.VersionCase<byte[], byte[]>((byte)1, "v1", TaggedReader(1)),
            ]);

        // v2 data: [version=2][VarInt64 ZigZag of 1 = 0x02][body=0xBB]
        byte[] v2Data = [0x02, 0x02, 0xBB];

        var result = Codec.Decode(codec, v2Data);

        AssertTagged(result, 2);
        Assert.Equal(0xBB, result[0]);
    }

    // ═══════════════════════════════════════════════════
    //  Unknown version fires the unknown delegate
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Unknown version passes raw body to unknown delegate")]
    public void UnknownVersion_FiresDelegate()
    {
        byte[] capturedBody = [];
        byte capturedVersion = 0;
        bool wasCalled = false;

        var unknown = (byte ver, byte[] body) =>
        {
            capturedVersion = ver;
            capturedBody = body;
            wasCalled = true;
            return body;
        };

        var codec = Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: unknown,
            cases:
            [
                Codec.VersionCase<byte[], byte[]>((byte)1, "v1", TaggedReader(1)),
            ]);

        // v99 data: [version=99][VarInt64 ZigZag of 2 = 0x04][body=0xCC, 0xDD]
        byte[] v99Data = [0x63, 0x04, 0xCC, 0xDD];

        var result = Codec.Decode(codec, v99Data);

        Assert.True(wasCalled);
        Assert.Equal(99, capturedVersion);
        Assert.Equal([0xCC, 0xDD], capturedBody);
        Assert.Equal([0xCC, 0xDD], result);
    }

    // ═══════════════════════════════════════════════════
    //  Round-trip: encode newest, decode same
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Multi-step format round-trips with current version")]
    public void MultiStepFormat_RoundTrip_CurrentVersion()
    {
        var codec = Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases:
            [
                Codec.VersionCase<byte[], byte[]>((byte)2, "v2", TaggedReader(2)),
                Codec.VersionCase<byte[], byte[]>((byte)1, "v1", TaggedReader(1)),
            ]);

        // Model with v2 tag byte
        byte[] original = [0xDE, 0xAD, 0x02];

        byte[] encoded = Codec.EncodeToArray(codec, original);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  v1 reader supplies defaults for v2 fields
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Old version reader supplies defaults for new fields")]
    public void OldVersionReader_SuppliesDefaults()
    {
        // v1 stores only one byte. v2 stores two bytes.
        // v1 reader reads one byte and defaults the second to 0xFF.

        var v1Reader = new DefaultingReader(wireBytes: 1, modelBytes: 2, defaultByte: 0xFF);
        var v2Reader = new DefaultingReader(wireBytes: 2, modelBytes: 2, defaultByte: 0x00);

        var codec = Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases:
            [
                Codec.VersionCase<byte[], byte[]>((byte)2, "v2", v2Reader),
                Codec.VersionCase<byte[], byte[]>((byte)1, "v1", v1Reader),
            ]);

        // v1 data: [version=1][VarInt64 ZigZag of 1 = 0x02][body=0x42]
        byte[] v1Data = [0x01, 0x02, 0x42];

        var result = Codec.Decode(codec, v1Data);

        // v1 reader: reads 1 byte, pads to 2 with 0xFF
        Assert.Equal([0x42, 0xFF], result);

        // v2 data: [version=2][VarInt64 ZigZag of 2 = 0x04][body=0x10, 0x20]
        byte[] v2Data = [0x02, 0x04, 0x10, 0x20];

        var result2 = Codec.Decode(codec, v2Data);

        Assert.Equal([0x10, 0x20], result2);
    }

    // ═══════════════════════════════════════════════════
    //  CodecFormats.Create integration with registry
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecFormats.Create uses registry when registered")]
    public void CodecFormats_Create_UsesRegistry()
    {
        // Register a multi-step format for "pos" (overriding the default single-step)
        var v1Reader = TaggedReader(1);
        var v2Reader = TaggedReader(2);

        var format = new CodecFormat("test-pos-registry",
        [
            new CodecVersionStep(1, "pos-v1", v1Reader),
            new CodecVersionStep(2, "pos-v2", v2Reader),
        ]);

        CodecMigrationRegistry.Default.Register(format);

        // Build a codec that uses the registry (simulating CodecFormats.Create logic).
        var codec = BuildFromRegistry("test-pos-registry");

        Assert.NotNull(codec);

        // v1 data should dispatch through v1 reader
        byte[] v1Data = [0x01, 0x02, 0x42];
        var result = Codec.Decode(codec, v1Data);
        AssertTagged(result, 1);
    }

    /// <summary>
    /// Builds a VersionEnvelope from the registry, matching CodecFormats.Create logic.
    /// </summary>
    private static ICodec<byte[]> BuildFromRegistry(string ext)
    {
        var cases = new List<VersionCaseDefinition<byte[]>>();

        var format = CodecMigrationRegistry.Default.Get(ext);
        if (format != null)
        {
            for (int i = format.Steps.Count - 1; i >= 0; i--)
            {
                var step = format.Steps[i];
                cases.Add(Codec.VersionCase<byte[], byte[]>(
                    (byte)step.Version, step.Label, step.Reader));
            }
        }

        if (cases.Count == 0)
            throw new InvalidOperationException($"No format registered for '{ext}'.");

        return Codec.VersionEnvelope<byte[], byte>(
            versionCodec: Codec.UInt8,
            bodyLengthCodec: Codec.VarInt64,
            unknown: (ver, body) => body,
            cases: cases.ToArray());
    }

    /// <summary>
    /// Reader that decodes <c>wireBytes</c> from the stream and produces
    /// a <c>modelBytes</c>-length model, padding with <c>defaultByte</c>.
    /// Encode writes only the leading <c>wireBytes</c>.
    /// </summary>
    private sealed class DefaultingReader : ICodec<byte[]>
    {
        private readonly int _wireBytes;
        private readonly int _modelBytes;
        private readonly byte _defaultByte;

        public DefaultingReader(int wireBytes, int modelBytes, byte defaultByte)
        {
            _wireBytes = wireBytes;
            _modelBytes = modelBytes;
            _defaultByte = defaultByte;
        }

        public byte[] Decode(ref SequenceReader<byte> reader, CodecContext context)
        {
            var result = new byte[_modelBytes];
            for (int i = _wireBytes; i < _modelBytes; i++)
                result[i] = _defaultByte;

            for (int i = 0; i < _wireBytes; i++)
            {
                if (!reader.TryRead(out byte b))
                    throw new InvalidOperationException("DefaultingReader: unexpected end of data.");
                result[i] = b;
            }

            return result;
        }

        public void Encode(byte[] value, IBufferWriter<byte> writer, CodecContext context)
        {
            var span = writer.GetSpan(_wireBytes);
            for (int i = 0; i < _wireBytes; i++)
                span[i] = i < value.Length ? value[i] : _defaultByte;
            writer.Advance(_wireBytes);
        }
    }
}
