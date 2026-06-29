using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Recovery;

[Trait("Category", "CodecKit")]
public sealed class FrameScanResultTests
{
    [Fact(DisplayName = "FrameScanResult: Success result has correct Offset, Success=true, Value set, Failure=null")]
    public void SuccessResult_HasCorrectProperties()
    {
        var source = new ReadOnlySequence<byte>([0x2A, 0x00, 0x00, 0x00]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(42, results[0].Value);
        Assert.Null(results[0].Failure);
    }

    [Fact(DisplayName = "FrameScanResult: Failure result has correct Offset, Success=false, Value=default, Failure set")]
    public void FailureResult_HasCorrectProperties()
    {
        var source = new ReadOnlySequence<byte>([0xFF]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(default(int), results[0].Value);
        Assert.NotNull(results[0].Failure);
        Assert.Equal(CodecErrorCode.Truncated, results[0].Failure.Code);
    }

    [Fact(DisplayName = "FrameScanResult: All frames have Success=true when data is valid")]
    public void AllFramesSuccess_WhenDataValid()
    {
        byte[] data = new byte[12];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), 10);
        BitConverter.TryWriteBytes(data.AsSpan(4, 4), 20);
        BitConverter.TryWriteBytes(data.AsSpan(8, 4), 30);

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(10, results[0].Value);
        Assert.Equal(20, results[1].Value);
        Assert.Equal(30, results[2].Value);
    }

    [Fact(DisplayName = "FrameScanResult: Corrupt frame has Success=false")]
    public void CorruptFrame_HasSuccessFalse()
    {
        var source = new ReadOnlySequence<byte>([0x01, 0x00, 0x00, 0x00, 0xFF]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.True(results.Count >= 2);
        Assert.True(results[0].Success);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(1, results[0].Value);
        Assert.False(results[1].Success);
        Assert.Equal(4, results[1].Offset);
    }

    [Fact(DisplayName = "FrameScanResult: Repeated Int32LE values all decode as success")]
    public void RepeatedInt32LE_AllSuccess()
    {
        byte[] data = new byte[40];
        for (int i = 0; i < 10; i++)
            BitConverter.TryWriteBytes(data.AsSpan(i * 4, 4), i * 100);

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Equal(10, results.Count);
        for (int i = 0; i < 10; i++)
        {
            Assert.True(results[i].Success);
            Assert.Equal(i * 4, results[i].Offset);
            Assert.Equal(i * 100, results[i].Value);
        }
    }

    [Fact(DisplayName = "FrameScanResult: Garbage data produces failures at each position")]
    public void GarbageData_AllFailures()
    {
        var source = new ReadOnlySequence<byte>([0xFF, 0xFE, 0xFD]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.False(r.Success));
        Assert.Equal(0, results[0].Offset);
    }

    [Fact(DisplayName = "FrameScanResult: Mixed valid/invalid data has mixed success/failure")]
    public void MixedData_MixedResults()
    {
        byte[] data = new byte[11];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), 100);
        data[4] = 0xFF;
        BitConverter.TryWriteBytes(data.AsSpan(5, 4), 200);
        data[9] = 0xFE;
        data[10] = 0xFD;

        var source = new ReadOnlySequence<byte>(data);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        // Verify at least 2 results exist with mixed success/failure
        Assert.True(results.Count >= 2);

        // First frame at offset 0 should be success (valid Int32LE = 100)
        Assert.True(results[0].Success);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(100, results[0].Value);

        // Offsets must be strictly increasing
        for (int i = 1; i < results.Count; i++)
            Assert.True(results[i].Offset > results[i - 1].Offset,
                $"Expected offset[{i}] > offset[{i - 1}], got {results[i].Offset} <= {results[i - 1].Offset}");

        // Some results should succeed, some should fail
        Assert.Contains(results, r => r.Success);
        Assert.Contains(results, r => !r.Success);
    }

    [Fact(DisplayName = "FrameScanResult: Empty source returns empty results")]
    public void EmptySource_ReturnsEmpty()
    {
        var source = ReadOnlySequence<byte>.Empty;
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Empty(results);
    }

    [Fact(DisplayName = "FrameScanResult: Single valid frame offset is preserved")]
    public void SingleFrame_OffsetPreserved()
    {
        var source = new ReadOnlySequence<byte>([0x05, 0x00, 0x00, 0x00]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal(0, results[0].Offset);
        Assert.Equal(5, results[0].Value);
    }

    [Fact(DisplayName = "FrameScanResult: Failure carries CodecErrorCode details")]
    public void FailureResult_CarriesErrorCodeDetails()
    {
        var source = new ReadOnlySequence<byte>([0x01, 0x02]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.NotEmpty(results);
        Assert.False(results[0].Success);
        Assert.NotNull(results[0].Failure);
        Assert.Equal(CodecErrorCode.Truncated, results[0].Failure.Code);
        Assert.False(string.IsNullOrEmpty(results[0].Failure.Message));
    }

    [Fact(DisplayName = "FrameScanResult: Success value is correct type")]
    public void SuccessResult_ValueType()
    {
        var source = new ReadOnlySequence<byte>([0x0A, 0x00, 0x00, 0x00]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Single(results);
        Assert.IsType<int>(results[0].Value);
        Assert.Equal(10, results[0].Value);
    }

    [Fact(DisplayName = "FrameScanResult: Failure value is default(T)")]
    public void FailureResult_ValueIsDefault()
    {
        var source = new ReadOnlySequence<byte>([0xFF]);
        IReadOnlyList<FrameScanResult<int>> results = CodecRecovery.ScanFrames(source, Codec.Int32LE);

        Assert.Single(results);
        Assert.False(results[0].Success);
        Assert.Equal(default(int), results[0].Value);
    }
}
