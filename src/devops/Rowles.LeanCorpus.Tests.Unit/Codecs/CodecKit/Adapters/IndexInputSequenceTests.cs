using System.Buffers;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Adapters;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Adapters;

[Trait("Category", "CodecKit")]
public sealed class IndexInputSequenceTests : IDisposable
{
    private readonly string _dir;
    private readonly MMapDirectory _directory;

    public IndexInputSequenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_iseq_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _directory = new MMapDirectory(_dir);
    }

    public void Dispose()
    {
        _directory.Dispose();
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    private IndexInput WriteAndOpen(string name, byte[] data)
    {
        using (var output = _directory.CreateOutput(name))
        {
            output.WriteBytes(data);
        }
        return _directory.OpenInput(name);
    }

    // ═══════════════════════════════════════════════════
    //  Constructor
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "IndexInputSequence: Constructor creates a valid ReadOnlySequence from IndexInput remaining bytes")]
    public void Constructor_CreatesSequence()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        using var input = WriteAndOpen("test_seq.bin", data);
        var seq = new IndexInputSequence(input);

        Assert.True(seq.Sequence.Length > 0);
        Assert.Equal(data.Length, (int)seq.Sequence.Length);
    }

    [Fact(DisplayName = "IndexInputSequence: Sequence.Length equals remaining bytes in the file")]
    public void Sequence_LengthEqualsRemainingBytes()
    {
        byte[] data = new byte[128];
        Random.Shared.NextBytes(data);
        using var input = WriteAndOpen("test_len.bin", data);
        var seq = new IndexInputSequence(input);

        Assert.Equal(data.Length, (int)seq.Sequence.Length);
    }

    [Fact(DisplayName = "IndexInputSequence: Sequence contains the correct bytes")]
    public void Sequence_ContainsCorrectBytes()
    {
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE];
        using var input = WriteAndOpen("test_bytes.bin", data);
        var seq = new IndexInputSequence(input);

        var result = new byte[seq.Sequence.Length];
        seq.Sequence.CopyTo(result);
        Assert.Equal(data, result);
    }

    // ═══════════════════════════════════════════════════
    //  Properties
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "IndexInputSequence: Input property returns the original IndexInput")]
    public void Input_ReturnsOriginal()
    {
        byte[] data = [0x01, 0x02, 0x03];
        using var input = WriteAndOpen("test_input.bin", data);
        var seq = new IndexInputSequence(input);

        Assert.Same(input, seq.Input);
    }

    [Fact(DisplayName = "IndexInputSequence: StartPosition is the position at construction time")]
    public void StartPosition_IsConstructionPosition()
    {
        byte[] data = new byte[64];
        Random.Shared.NextBytes(data);
        using var input = WriteAndOpen("test_startpos.bin", data);

        // Seek to a non-zero position
        input.Seek(10);
        Assert.Equal(10, input.Position);

        var seq = new IndexInputSequence(input);
        Assert.Equal(10, seq.StartPosition);
    }

    [Fact(DisplayName = "IndexInputSequence: StartPosition is zero when constructed at start of file")]
    public void StartPosition_AtFileStart_IsZero()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        using var input = WriteAndOpen("test_start0.bin", data);
        var seq = new IndexInputSequence(input);

        Assert.Equal(0, seq.StartPosition);
    }

    // ═══════════════════════════════════════════════════
    //  Constructor — position restoration
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "IndexInputSequence: Constructor leaves IndexInput at its original position (seeks back)")]
    public void Constructor_RestoresOriginalPosition()
    {
        byte[] data = new byte[100];
        Random.Shared.NextBytes(data);
        using var input = WriteAndOpen("test_restore.bin", data);

        long originalPosition = input.Position; // 0
        Assert.Equal(0, originalPosition);

        var seq = new IndexInputSequence(input);

        // Position should be restored to original
        Assert.Equal(originalPosition, input.Position);
    }

    [Fact(DisplayName = "IndexInputSequence: Constructor restores non-zero start position")]
    public void Constructor_RestoresNonZeroPosition()
    {
        byte[] data = new byte[100];
        Random.Shared.NextBytes(data);
        using var input = WriteAndOpen("test_restorenz.bin", data);

        input.Seek(40);
        Assert.Equal(40, input.Position);

        var seq = new IndexInputSequence(input);

        Assert.Equal(40, input.Position);
    }

    // ═══════════════════════════════════════════════════
    //  SyncPositionBack
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "IndexInputSequence: SyncPositionBack advances IndexInput position after SequenceReader consumption")]
    public void SyncPositionBack_AdvancesInputPosition()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        using var input = WriteAndOpen("test_sync.bin", data);
        var seq = new IndexInputSequence(input);

        // Read first 3 bytes via SequenceReader
        var reader = new SequenceReader<byte>(seq.Sequence);
        reader.Advance(3);
        Assert.Equal(3, reader.Consumed);

        // IndexInput position should still be at start (0)
        Assert.Equal(0, input.Position);

        seq.SyncPositionBack(ref reader);
        Assert.Equal(3, input.Position);
    }

    [Fact(DisplayName = "IndexInputSequence: SyncPositionBack after consuming all bytes sets position to end")]
    public void SyncPositionBack_AfterAllBytes_PositionAtEnd()
    {
        byte[] data = [0x0A, 0x0B, 0x0C, 0x0D];
        using var input = WriteAndOpen("test_syncall.bin", data);
        var seq = new IndexInputSequence(input);

        var reader = new SequenceReader<byte>(seq.Sequence);
        reader.Advance(data.Length);
        Assert.Equal(data.Length, reader.Consumed);

        seq.SyncPositionBack(ref reader);
        Assert.Equal(data.Length, input.Position);
        Assert.Equal(input.Length, input.Position);
    }

    [Fact(DisplayName = "IndexInputSequence: SyncPositionBack with zero consumption leaves position unchanged")]
    public void SyncPositionBack_NoConsumption_PositionUnchanged()
    {
        byte[] data = [0x01, 0x02, 0x03];
        using var input = WriteAndOpen("test_sync0.bin", data);
        var seq = new IndexInputSequence(input);

        var reader = new SequenceReader<byte>(seq.Sequence);
        Assert.Equal(0, reader.Consumed);

        seq.SyncPositionBack(ref reader);
        Assert.Equal(0, input.Position);
    }

    [Fact(DisplayName = "IndexInputSequence: SyncPositionBack with non-zero start position advances correctly")]
    public void SyncPositionBack_NonZeroStart()
    {
        byte[] data = new byte[64];
        Random.Shared.NextBytes(data);
        using var input = WriteAndOpen("test_syncnz.bin", data);

        input.Seek(10);
        var seq = new IndexInputSequence(input);

        // Read 5 bytes from the sequence
        var reader = new SequenceReader<byte>(seq.Sequence);
        reader.Advance(5);

        seq.SyncPositionBack(ref reader);
        Assert.Equal(15, input.Position); // start 10 + consumed 5
    }

    // ═══════════════════════════════════════════════════
    //  Empty file
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "IndexInputSequence: Works with empty IndexInput (no remaining bytes)")]
    public void EmptyInput_Works()
    {
        using var input = WriteAndOpen("test_empty.bin", []);
        var seq = new IndexInputSequence(input);

        Assert.Equal(0, (int)seq.Sequence.Length);
        Assert.Equal(0, seq.StartPosition);

        var reader = new SequenceReader<byte>(seq.Sequence);
        Assert.Equal(0, reader.Remaining);
    }

    // ═══════════════════════════════════════════════════
    //  Decode round-trip through IndexInputSequence
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "IndexInputSequence: Can decode an Int32 via SequenceReader and sync position")]
    public void DecodeInt32_AndSyncPosition()
    {
        byte[] data = new byte[8];
        BitConverter.GetBytes(42).CopyTo(data, 0);
        BitConverter.GetBytes(99).CopyTo(data, 4);

        using var input = WriteAndOpen("test_decode.bin", data);
        var seq = new IndexInputSequence(input);

        var reader = new SequenceReader<byte>(seq.Sequence);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        int first = Codec.Int32LE.Decode(ref reader, ctx);
        Assert.Equal(42, first);

        seq.SyncPositionBack(ref reader);
        Assert.Equal(4, input.Position);

        // Now read the second int32 directly from IndexInput
        int second = input.ReadInt32();
        Assert.Equal(99, second);
    }

    [Fact(DisplayName = "IndexInputSequence: Full round-trip encode then decode via IndexInputSequence")]
    public void RoundTrip_EncodeThenDecodeViaSequence()
    {
        // Encode an Int32 value and decode via IndexInputSequence
        int value = 42;
        byte[] encoded = Codec.EncodeToArray(Codec.Int32LE, value);

        using var input = WriteAndOpen("test_rt.bin", encoded);
        var seq = new IndexInputSequence(input);

        var reader = new SequenceReader<byte>(seq.Sequence);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        int decoded = Codec.Int32LE.Decode(ref reader, ctx);
        Assert.Equal(value, decoded);
    }
}
