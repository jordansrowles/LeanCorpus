using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Tests.Unit.Index;

[Trait("Category", "UnitTest")]
public sealed class PostingAccumulatorIndexOptionsTests
{
    [Fact(DisplayName = "DocsOnly: HasFreqs and HasPositions stay false")]
    public void DocsOnly_FlagsStayFalse()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 0, FieldIndexOptions.DocsOnly);

        Assert.False(acc.HasFreqs);
        Assert.False(acc.HasPositions);
        Assert.False(acc.HasPayloads);
    }

    [Fact(DisplayName = "DocsOnly: Accumulates doc IDs as deltas")]
    public void DocsOnly_AccumulatesDocIds()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 0, FieldIndexOptions.DocsOnly);
        acc.Add(10, 0, FieldIndexOptions.DocsOnly);
        acc.Add(20, 0, FieldIndexOptions.DocsOnly);

        var ids = acc.DocIds;
        Assert.Equal(3, acc.Count);
        Assert.Equal(5, ids[0]);
        Assert.Equal(10, ids[1]);
        Assert.Equal(20, ids[2]);
    }

    [Fact(DisplayName = "DocsOnly: Same doc ID does not produce duplicate")]
    public void DocsOnly_DeduplicatesSameDoc()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 0, FieldIndexOptions.DocsOnly);
        acc.Add(5, 0, FieldIndexOptions.DocsOnly);

        Assert.Equal(1, acc.Count);
        Assert.Equal(5, acc.DocIds[0]);
    }

    [Fact(DisplayName = "DocsOnly: GetFreq returns 0 for all entries")]
    public void DocsOnly_FreqIsZero()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 0, FieldIndexOptions.DocsOnly);
        acc.Add(10, 0, FieldIndexOptions.DocsOnly);

        Assert.Equal(0, acc.GetFreq(0));
        Assert.Equal(0, acc.GetFreq(1));
    }

    [Fact(DisplayName = "DocsOnly: GetPositions returns empty span")]
    public void DocsOnly_PositionsAreEmpty()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 0, FieldIndexOptions.DocsOnly);

        var positions = acc.GetPositions(0);
        Assert.True(positions.IsEmpty);
    }

    [Fact(DisplayName = "DocsAndFreqs: HasFreqs is true, HasPositions stays false")]
    public void DocsAndFreqs_FlagsCorrect()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 0, FieldIndexOptions.DocsAndFreqs);

        Assert.True(acc.HasFreqs);
        Assert.False(acc.HasPositions);
        Assert.False(acc.HasPayloads);
    }

    [Fact(DisplayName = "DocsAndFreqs: Accumulates term frequencies")]
    public void DocsAndFreqs_AccumulatesFreqs()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 0, FieldIndexOptions.DocsAndFreqs);
        acc.Add(5, 0, FieldIndexOptions.DocsAndFreqs);
        acc.Add(5, 0, FieldIndexOptions.DocsAndFreqs);
        acc.Add(10, 0, FieldIndexOptions.DocsAndFreqs);

        Assert.Equal(2, acc.Count);
        Assert.Equal(3, acc.GetFreq(0)); // doc 5 appears 3 times
        Assert.Equal(1, acc.GetFreq(1)); // doc 10 appears 1 time
    }

    [Fact(DisplayName = "DocsAndFreqs: Positions are empty")]
    public void DocsAndFreqs_PositionsAreEmpty()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 42, FieldIndexOptions.DocsAndFreqs); // position 42 is ignored

        Assert.Equal(1, acc.Count);
        Assert.True(acc.GetPositions(0).IsEmpty);
    }

    [Fact(DisplayName = "DocsAndFreqsAndPositions: Both flags are true")]
    public void DocsAndFreqsAndPositions_FlagsCorrect()
    {
        var acc = new PostingAccumulator();
        acc.Add(0, 0, FieldIndexOptions.DocsAndFreqsAndPositions);

        Assert.True(acc.HasFreqs);
        Assert.True(acc.HasPositions);
    }

    [Fact(DisplayName = "DocsAndFreqsAndPositions: Positions are stored")]
    public void DocsAndFreqsAndPositions_StoresPositions()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 10, FieldIndexOptions.DocsAndFreqsAndPositions);
        acc.Add(5, 20, FieldIndexOptions.DocsAndFreqsAndPositions); // same doc, position 20
        acc.Add(10, 5, FieldIndexOptions.DocsAndFreqsAndPositions);

        Assert.Equal(2, acc.Count);
        Assert.Equal(2, acc.GetFreq(0)); // doc 5 has 2 positions

        var positions = acc.GetPositions(0);
        Assert.Equal(2, positions.Length);
        Assert.Equal(10, positions[0]);
        Assert.Equal(20, positions[1]);
    }

    [Fact(DisplayName = "DocsAndFreqsAndPositionsAndOffsets: HasOffsets is true")]
    public void DocsAndFreqsAndPositionsAndOffsets_OffsetsFlag()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 10, FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets, startOffset: 0, endOffset: 5);

        Assert.True(acc.HasOffsets);
        Assert.True(acc.HasFreqs);
        Assert.True(acc.HasPositions);

        var (starts, ends) = acc.GetOffsets(0);
        Assert.NotNull(starts);
        Assert.NotNull(ends);
        Assert.Equal(0, starts![0]);
        Assert.Equal(5, ends![0]);
    }

    [Fact(DisplayName = "DocsAndFreqsAndPositionsAndOffsets: Positions still stored")]
    public void DocsAndFreqsAndPositionsAndOffsets_StoresPositions()
    {
        var acc = new PostingAccumulator();
        acc.Add(5, 10, FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets, startOffset: 0, endOffset: 5);

        var positions = acc.GetPositions(0);
        Assert.Equal(1, positions.Length);
        Assert.Equal(10, positions[0]);
    }

    [Fact(DisplayName = "AddWithPayload: DocsOnly discards payload")]
    public void AddWithPayload_DocsOnly_DiscardsPayload()
    {
        var acc = new PostingAccumulator();
        acc.AddWithPayload(5, 10, new byte[] { 1, 2, 3 }, FieldIndexOptions.DocsOnly);

        Assert.False(acc.HasPayloads);
        Assert.False(acc.HasFreqs);
        Assert.Equal(1, acc.Count);
        Assert.Equal(5, acc.DocIds[0]);
    }

    [Fact(DisplayName = "AddWithPayload: DocsAndFreqsAndPositions stores payload")]
    public void AddWithPayload_StoresPayload()
    {
        var acc = new PostingAccumulator();
        var payload = new byte[] { 1, 2, 3 };
        acc.AddWithPayload(5, 10, payload, FieldIndexOptions.DocsAndFreqsAndPositions);

        Assert.True(acc.HasPayloads);
        Assert.Equal(payload, acc.GetPayload(0, 0));
    }

    [Fact(DisplayName = "AddWithPayload: DocsAndFreqs discards payload (no positions to attach)")]
    public void AddWithPayload_DocsAndFreqs_DiscardsPayload()
    {
        var acc = new PostingAccumulator();
        acc.AddWithPayload(5, 10, new byte[] { 1, 2, 3 }, FieldIndexOptions.DocsAndFreqs);

        Assert.True(acc.HasFreqs);
        Assert.False(acc.HasPositions);
        Assert.False(acc.HasPayloads);
        Assert.Equal(1, acc.GetFreq(0));
    }

    [Fact(DisplayName = "PostingAccumulator has zero position buffer allocation for DocsOnly")]
    public void DocsOnly_NoPositionBufferAllocation()
    {
        // DocsOnly should never trigger EnsurePayloads or position buffer growth.
        var acc = new PostingAccumulator();

        // Add many docs with DocsOnly — should not allocate payload arrays.
        for (int i = 0; i < 100; i++)
            acc.Add(i, 0, FieldIndexOptions.DocsOnly);

        Assert.False(acc.HasPayloads);
        Assert.False(acc.HasFreqs);
        Assert.False(acc.HasPositions);
        Assert.Equal(100, acc.Count);
    }
}
