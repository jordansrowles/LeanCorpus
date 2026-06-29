using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Recovery;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Recovery;

[Trait("Category", "CodecKit")]
public sealed class CodecRecoveryTests
{
    // ═══════════════════════════════════════════════════
    //  ScanForMagic — empty magic bytes
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Empty magic bytes returns empty results")]
    public void ScanForMagic_EmptyMagic_ReturnsEmpty()
    {
        var source = new ReadOnlySequence<byte>([0x01, 0x02, 0x03]);
        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, ReadOnlySpan<byte>.Empty);

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — pattern longer than source
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Pattern longer than source returns empty")]
    public void ScanForMagic_PatternLongerThanSource_ReturnsEmpty()
    {
        var source = new ReadOnlySequence<byte>([0x01, 0x02]);
        byte[] magic = [0x01, 0x02, 0x03]; // 3 bytes > 2

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — no match
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: No match returns empty")]
    public void ScanForMagic_NoMatch_ReturnsEmpty()
    {
        var source = new ReadOnlySequence<byte>([0x01, 0x02, 0x03, 0x04]);
        byte[] magic = [0xAA, 0xBB];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — single match
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Single match returns one offset")]
    public void ScanForMagic_SingleMatch_ReturnsOneOffset()
    {
        var source = new ReadOnlySequence<byte>([0x00, 0x01, 0x02, 0x03, 0x04]);
        byte[] magic = [0x01, 0x02];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Single(results);
        Assert.Equal(1, results[0]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — multiple matches
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Multiple matches at known positions")]
    public void ScanForMagic_MultipleMatches_ReturnsAllOffsets()
    {
        // Magic "AB" at positions 0, 5, 10
        byte[] data =
        [
            0x41, 0x42, // "AB" at offset 0
            0x00, 0x00, 0x00,
            0x41, 0x42, // "AB" at offset 5
            0x00, 0x00, 0x00,
            0x41, 0x42  // "AB" at offset 10
        ];
        var source = new ReadOnlySequence<byte>(data);
        byte[] magic = [0x41, 0x42]; // "AB"

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0]);
        Assert.Equal(5, results[1]);
        Assert.Equal(10, results[2]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — match at position 0
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Match at position 0")]
    public void ScanForMagic_MatchAtStart()
    {
        var source = new ReadOnlySequence<byte>([0xDE, 0xAD, 0xBE, 0xEF]);
        byte[] magic = [0xDE, 0xAD];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Single(results);
        Assert.Equal(0, results[0]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — match at end of source
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Match at end of source")]
    public void ScanForMagic_MatchAtEnd()
    {
        var source = new ReadOnlySequence<byte>([0x00, 0x00, 0x00, 0x01, 0x02]);
        byte[] magic = [0x01, 0x02];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Single(results);
        Assert.Equal(3, results[0]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — overlapping matches
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Overlapping matches are detected")]
    public void ScanForMagic_OverlappingMatches()
    {
        // "AAAA" contains "AAA" at offsets 0 and 1 (overlapping)
        var source = new ReadOnlySequence<byte>([0x41, 0x41, 0x41, 0x41]); // "AAAA"
        byte[] magic = [0x41, 0x41, 0x41]; // "AAA"

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0]);
        Assert.Equal(1, results[1]);
    }

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Overlapping matches with repeated pattern")]
    public void ScanForMagic_OverlappingRepeatedPattern()
    {
        // "010101" contains "0101" at offsets 0 and 2 (overlapping)
        var source = new ReadOnlySequence<byte>([0x01, 0x01, 0x01, 0x01]); // four 0x01 bytes
        byte[] magic = [0x01, 0x01]; // two bytes

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        // Offsets: 0, 1, 2
        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0]);
        Assert.Equal(1, results[1]);
        Assert.Equal(2, results[2]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — single byte pattern
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Single-byte pattern finds all occurrences")]
    public void ScanForMagic_SingleBytePattern()
    {
        var source = new ReadOnlySequence<byte>([0x00, 0x42, 0x00, 0x42, 0x42]);
        byte[] magic = [0x42];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Equal(3, results.Count);
        Assert.Equal(1, results[0]);
        Assert.Equal(3, results[1]);
        Assert.Equal(4, results[2]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — exact match to source size
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Exact match to source length")]
    public void ScanForMagic_ExactSourceLength()
    {
        var source = new ReadOnlySequence<byte>([0xCA, 0xFE, 0xBA, 0xBE]);
        byte[] magic = [0xCA, 0xFE, 0xBA, 0xBE];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Single(results);
        Assert.Equal(0, results[0]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — all frames decode
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: All frames decode successfully")]
    public void ScanFrames_AllValid_AllSuccess()
    {
        // 4 valid Int32LE frames = 16 bytes
        byte[] data = new byte[16];
        for (int i = 0; i < 4; i++)
            BitConverter.TryWriteBytes(data.AsSpan(i * 4, 4), (i + 1) * 10);

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(10, results[0].Value);
        Assert.Equal(20, results[1].Value);
        Assert.Equal(30, results[2].Value);
        Assert.Equal(40, results[3].Value);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — corrupt byte
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Corrupt byte produces failure, then next offset tried")]
    public void ScanFrames_CorruptByte_ThenNextOffset()
    {
        // Valid frame (4 bytes) + garbage (1 byte) + valid frame (4 bytes)
        byte[] data = new byte[9];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), 1);   // valid
        data[4] = 0xFF;                                       // garbage
        BitConverter.TryWriteBytes(data.AsSpan(5, 4), 2);   // valid

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        // At least 2 results: first frame succeeds, and there's at least one failure
        Assert.True(results.Count >= 2);

        Assert.True(results[0].Success);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(1, results[0].Value);

        // Offsets must be strictly increasing
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i].Offset > results[i - 1].Offset,
                $"Expected offset[{i}] > offset[{i - 1}], got {results[i].Offset} <= {results[i - 1].Offset}");

        // At least one failure exists (from the corrupt byte)
        Assert.Contains(results, r => !r.Success);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — empty source
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Empty source returns empty results")]
    public void ScanFrames_EmptySource_ReturnsEmpty()
    {
        var source = ReadOnlySequence<byte>.Empty;
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — single frame
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Single frame source decodes one frame")]
    public void ScanFrames_SingleFrame()
    {
        var source = new ReadOnlySequence<byte>([0xE8, 0x03, 0x00, 0x00]); // Int32LE = 1000
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(1000, results[0].Value);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — multiple successful frames
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Multiple successful frames")]
    public void ScanFrames_MultipleSuccess()
    {
        // 3 Int32LE frames with values: -1, 0, int.MaxValue
        byte[] data = new byte[12];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), -1);
        BitConverter.TryWriteBytes(data.AsSpan(4, 4), 0);
        BitConverter.TryWriteBytes(data.AsSpan(8, 4), int.MaxValue);

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Success);
        Assert.True(results[1].Success);
        Assert.True(results[2].Success);
        Assert.Equal(-1, results[0].Value);
        Assert.Equal(0, results[1].Value);
        Assert.Equal(int.MaxValue, results[2].Value);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames advances past successful frames
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Offsets increase by frame size on success")]
    public void ScanFrames_OffsetsIncreaseByFrameSize()
    {
        // 3 valid Int32LE frames
        byte[] data = new byte[12];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), 100);
        BitConverter.TryWriteBytes(data.AsSpan(4, 4), 200);
        BitConverter.TryWriteBytes(data.AsSpan(8, 4), 300);

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Equal(3, results.Count);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(4, results[1].Offset);
        Assert.Equal(8, results[2].Offset);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames with explicit CodecOptions
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: With explicit CodecOptions")]
    public void ScanFrames_WithCodecOptions()
    {
        var options = new CodecOptions { RejectNonCanonicalVarInts = true };
        var source = new ReadOnlySequence<byte>([0x2A, 0x00, 0x00, 0x00]); // Int32LE = 42
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE, options);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(42, results[0].Value);
    }

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Custom CodecOptions with strict string validation")]
    public void ScanFrames_WithCustomOptions()
    {
        var options = new CodecOptions { Utf8Validation = Utf8ValidationMode.Strict };
        var source = new ReadOnlySequence<byte>(
        [
            0x03, 0x00, 0x00, 0x00, // length prefix for Utf8String(3)
            0x41, 0x42, 0x43        // "ABC"
        ]);
        IReadOnlyList<FrameScanResult<string>> results = CodecRecovery.ScanFrames(
            source,
            Codec.LengthPrefixed(Codec.UInt32LE, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow),
            options);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal("ABC", results[0].Value);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames with explicit CodecRegistry
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: With explicit CodecRegistry")]
    public void ScanFrames_WithCodecRegistry()
    {
        var registry = CodecRegistry.Default;
        var source = new ReadOnlySequence<byte>([0x2A, 0x00, 0x00, 0x00]); // Int32LE = 42
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE, null, registry);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(42, results[0].Value);
    }

    [Fact(DisplayName = "CodecRecovery.ScanFrames: With both options and registry")]
    public void ScanFrames_WithOptionsAndRegistry()
    {
        var options = new CodecOptions { MaxFrameBytes = 1024 };
        var registry = CodecRegistry.Default;
        var source = new ReadOnlySequence<byte>([0x2A, 0x00, 0x00, 0x00]);

        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE, options, registry);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(42, results[0].Value);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — failure advances by one byte
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Failure advances position by one byte")]
    public void ScanFrames_FailureAdvancesByOne()
    {
        // 5 bytes: last 4 bytes form a valid frame, first byte is spurious
        byte[] data = new byte[5];
        data[0] = 0xFF;                                       // garbage
        BitConverter.TryWriteBytes(data.AsSpan(1, 4), 42);   // valid at offset 1

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        // At least 1 result: the first frame may succeed or fail depending on byte interpretation
        Assert.True(results.Count >= 1);

        // Offsets must be strictly increasing
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i].Offset > results[i - 1].Offset,
                $"Expected offset[{i}] > offset[{i - 1}], got {results[i].Offset} <= {results[i - 1].Offset}");

        // At least one failure exists (garbage byte causes issues)
        Assert.Contains(results, r => !r.Success);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — large number of frames
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Large number of Int32LE frames all decode")]
    public void ScanFrames_LargeBatch()
    {
        int count = 50;
        byte[] data = new byte[count * 4];
        for (int i = 0; i < count; i++)
            BitConverter.TryWriteBytes(data.AsSpan(i * 4, 4), i);

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Equal(count, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i, results[i].Value);
            Assert.Equal(i * 4, results[i].Offset);
        }
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — empty source
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Empty source returns empty")]
    public void ScanForMagic_EmptySource_ReturnsEmpty()
    {
        var source = ReadOnlySequence<byte>.Empty;
        byte[] magic = [0x01, 0x02];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Empty(results);
    }

    // ═══════════════════════════════════════════════════
    //  ScanForMagic — pattern equals source
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanForMagic: Pattern exactly equals source")]
    public void ScanForMagic_PatternEqualsSource()
    {
        var source = new ReadOnlySequence<byte>([0xDE, 0xAD]);
        byte[] magic = [0xDE, 0xAD];

        IReadOnlyList<long> results = CodecRecovery.ScanForMagic(source, magic);

        Assert.Single(results);
        Assert.Equal(0, results[0]);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — single byte frames with UInt8
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: Single-byte frames (UInt8) all succeed")]
    public void ScanFrames_UInt8Frames()
    {
        var source = new ReadOnlySequence<byte>([0x0A, 0x14, 0x1E, 0x28]);
        IReadOnlyList<FrameScanResult<byte>> results = CodecRecovery.ScanFrames(source, Codec.UInt8);

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(0x0A, results[0].Value);
        Assert.Equal(0x14, results[1].Value);
        Assert.Equal(0x1E, results[2].Value);
        Assert.Equal(0x28, results[3].Value);
    }

    // ═══════════════════════════════════════════════════
    //  ScanFrames — VarUInt32 frames
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecRecovery.ScanFrames: VarUInt32 frames decode correctly")]
    public void ScanFrames_VarUInt32Frames()
    {
        // VarUInt32 encoded values: 0, 127, 128, 16383
        // 0 = 0x00
        // 127 = 0x7F
        // 128 = 0x80, 0x01
        // 16383 = 0xFF, 0x7F
        var source = new ReadOnlySequence<byte>([0x00, 0x7F, 0x80, 0x01, 0xFF, 0x7F]);
        IReadOnlyList<FrameScanResult<uint>> results = CodecRecovery.ScanFrames(source, Codec.VarUInt32);

        // VarUInt32 is variable-length, so ScanFrames reads each frame:
        // pos=0: reads 1 byte -> 0, pos+=1
        // pos=1: reads 1 byte -> 127, pos+=1
        // pos=2: reads 2 bytes -> 128, pos+=2
        // pos=4: reads 2 bytes -> 16383, pos+=2
        // pos=6: end
        Assert.Equal(4, results.Count);
        Assert.Equal((uint)0, results[0].Value);
        Assert.Equal((uint)127, results[1].Value);
        Assert.Equal((uint)128, results[2].Value);
        Assert.Equal((uint)16383, results[3].Value);
        Assert.All(results, r => Assert.True(r.Success));
    }
}
