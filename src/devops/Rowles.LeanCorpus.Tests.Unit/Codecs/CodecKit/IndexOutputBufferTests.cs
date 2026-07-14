using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Adapters;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class IndexOutputBufferTests : IDisposable
{
    private readonly string _tempDirectory;

    public IndexOutputBufferTests()
    {
        _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "LeanCorpus_IndexOutputBufferTests",
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

    [Fact(DisplayName = "Write via GetSpan + Advance flushes to IndexOutput")]
    public void Write_GetSpan_Advance_FlushesToOutput()
    {
        var filePath = Path.Combine(_tempDirectory, "buf_write.dat");
        using (var output = new IndexOutput(filePath))
        {
            var buf = new IndexOutputBuffer(output);
            try
            {
                var span = buf.GetSpan(4);
                span[0] = 0x01;
                span[1] = 0x02;
                span[2] = 0x03;
                span[3] = 0x04;
                buf.Advance(4);
                output.Flush();
            }
            finally
            {
                buf.Dispose();
            }
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        Assert.Equal(4, fileBytes.Length);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, fileBytes);
    }

    [Fact(DisplayName = "Write via GetMemory + Advance flushes to IndexOutput")]
    public void Write_GetMemory_Advance_FlushesToOutput()
    {
        var filePath = Path.Combine(_tempDirectory, "buf_memory.dat");
        using (var output = new IndexOutput(filePath))
        {
            var buf = new IndexOutputBuffer(output);
            try
            {
                var mem = buf.GetMemory(2);
                mem.Span[0] = 0xAA;
                mem.Span[1] = 0xBB;
                buf.Advance(2);
                output.Flush();
            }
            finally
            {
                buf.Dispose();
            }
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        Assert.Equal(2, fileBytes.Length);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, fileBytes);
    }

    [Fact(DisplayName = "Multiple Advance calls accumulate correctly")]
    public void MultipleAdvances_AccumulateCorrectly()
    {
        var filePath = Path.Combine(_tempDirectory, "buf_multi.dat");
        using (var output = new IndexOutput(filePath))
        {
            var buf = new IndexOutputBuffer(output);
            try
            {
                for (byte i = 0; i < 5; i++)
                {
                    var span = buf.GetSpan(1);
                    span[0] = i;
                    buf.Advance(1);
                }
                output.Flush();
            }
            finally
            {
                buf.Dispose();
            }
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        Assert.Equal(5, fileBytes.Length);
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, fileBytes);
    }

    [Fact(DisplayName = "Direct Write writes to IndexOutput")]
    public void DirectWrite_WritesToOutput()
    {
        var filePath = Path.Combine(_tempDirectory, "buf_direct.dat");
        byte[] data = [0xDE, 0xAD, 0xBE, 0xEF];
        using (var output = new IndexOutput(filePath))
        {
            var buf = new IndexOutputBuffer(output);
            try
            {
                buf.Write(data);
                output.Flush();
            }
            finally
            {
                buf.Dispose();
            }
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        Assert.Equal(data, fileBytes);
    }

    [Fact(DisplayName = "Large write spans chunks correctly")]
    public void LargeWrite_SpansChunks()
    {
        var filePath = Path.Combine(_tempDirectory, "buf_large.dat");
        byte[] data = new byte[10_000];
        Random.Shared.NextBytes(data);
        using (var output = new IndexOutput(filePath))
        {
            var buf = new IndexOutputBuffer(output);
            try
            {
                // Write in chunks via GetSpan/Advance
                int offset = 0;
                while (offset < data.Length)
                {
                    int chunk = Math.Min(1000, data.Length - offset);
                    var span = buf.GetSpan(chunk);
                    data.AsSpan(offset, chunk).CopyTo(span);
                    buf.Advance(chunk);
                    offset += chunk;
                }
                output.Flush();
            }
            finally
            {
                buf.Dispose();
            }
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        Assert.Equal(data, fileBytes);
    }

    [Fact(DisplayName = "Advance(0) does not discard pending data")]
    public void AdvanceZero_DoesNotDiscardPendingData()
    {
        var filePath = Path.Combine(_tempDirectory, "buf_zero.dat");
        using (var output = new IndexOutput(filePath))
        {
            var buf = new IndexOutputBuffer(output);
            try
            {
                var span = buf.GetSpan(4);
                span[0] = 0x11;
                span[1] = 0x22;
                span[2] = 0x33;
                span[3] = 0x44;
                buf.Advance(0);

                // Advance(0) must not flush or reset — data is still pending.
                // The next Advance with the real count must flush it.
                buf.Advance(4);
                output.Flush();
            }
            finally
            {
                buf.Dispose();
            }
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        Assert.Equal(4, fileBytes.Length);
        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, fileBytes);
    }
}
