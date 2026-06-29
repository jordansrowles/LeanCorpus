using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class ScratchBufferTests
{
    [Fact(DisplayName = "RentScratchBuffer writes data and tracks length")]
    public void ScratchBuffer_WritesDataAndTracksLength()
    {
        var context = new CodecContext(new CodecOptions(), CodecRegistry.Default);
        var scratch = context.RentScratchBuffer(256);
        try
        {
            Assert.Equal(0, scratch.Length);

            var span = scratch.GetSpan(4);
            span[0] = 0x01;
            span[1] = 0x02;
            span[2] = 0x03;
            span[3] = 0x04;
            scratch.Advance(4);

            Assert.Equal(4, scratch.Length);

            var written = scratch.Written;
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04 }, written.ToArray());
        }
        finally
        {
            context.ReturnScratchBuffer(scratch);
        }
    }

    [Fact(DisplayName = "RentScratchBuffer return disposes and resets")]
    public void ScratchBuffer_ReturnDisposes()
    {
        var context = new CodecContext(new CodecOptions(), CodecRegistry.Default);
        var scratch = context.RentScratchBuffer(256);
        scratch.GetSpan(8);
        scratch.Advance(8);
        Assert.Equal(8, scratch.Length);

        context.ReturnScratchBuffer(scratch);
        // After return (dispose), further access is not safe; the buffer is returned to pool.
        // We can verify a second rent returns a fresh buffer.
        var scratch2 = context.RentScratchBuffer(256);
        try
        {
            Assert.Equal(0, scratch2.Length);
        }
        finally
        {
            context.ReturnScratchBuffer(scratch2);
        }
    }

    [Fact(DisplayName = "EnterScope sets InScope and tracks RemainingInScope")]
    public void EnterScope_TracksRemaining()
    {
        var context = new CodecContext(new CodecOptions(), CodecRegistry.Default);

        Assert.False(context.InScope);
        Assert.Equal(-1, context.RemainingInScope);

        using (context.EnterScope(100))
        {
            Assert.True(context.InScope);
            Assert.Equal(100, context.RemainingInScope);

            context.ConsumeScope(40);
            Assert.Equal(60, context.RemainingInScope);

            context.ConsumeScope(60);
            Assert.Equal(0, context.RemainingInScope);
        }

        Assert.False(context.InScope);
        Assert.Equal(-1, context.RemainingInScope);
    }

    [Fact(DisplayName = "EnterScope guard exits scope on dispose")]
    public void EnterScope_GuardExitsScope()
    {
        var context = new CodecContext(new CodecOptions(), CodecRegistry.Default);

        var guard = context.EnterScope(50);
        Assert.True(context.InScope);
        Assert.Equal(50, context.RemainingInScope);

        guard.Dispose();
        Assert.False(context.InScope);
        Assert.Equal(-1, context.RemainingInScope);
    }

    [Fact(DisplayName = "PushDepth increments and decrements depth")]
    public void PushDepth_IncrementsAndDecrements()
    {
        var context = new CodecContext(new CodecOptions { MaxNestingDepth = 10 }, CodecRegistry.Default);

        Assert.Equal(0, context.Depth);

        using (context.PushDepth())
        {
            Assert.Equal(1, context.Depth);

            using (context.PushDepth())
            {
                Assert.Equal(2, context.Depth);
            }

            Assert.Equal(1, context.Depth);
        }

        Assert.Equal(0, context.Depth);
    }
}
