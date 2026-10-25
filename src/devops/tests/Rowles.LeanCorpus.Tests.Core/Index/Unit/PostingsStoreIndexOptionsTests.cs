using System.Text;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer.Postings;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class PostingsStoreIndexOptionsTests
{
    [Fact(DisplayName = "DocsOnly: stores unique document records without frequency or positions")]
    public void DocsOnly_StoresUniqueDocumentsWithoutFrequencyOrPositions()
    {
        using var store = new PostingsStore();
        store.AddDocOnly("body", "term", 5);
        store.AddDocOnly("body", "term", 5);
        store.AddDocOnly("body", "term", 10);
        store.Freeze();

        int termId = FindTerm(store, "body\0term");
        ref readonly var state = ref store.GetTermState(termId);
        Assert.Equal(PostingFlags.None, state.Flags);

        var reader = store.OpenDocReader(termId);
        Assert.True(reader.MoveNext(out var first));
        Assert.Equal(5, first.DocId);
        Assert.Equal(0, first.Freq);
        Assert.Equal(0, first.PositionCount);
        Assert.True(reader.MoveNext(out var second));
        Assert.Equal(10, second.DocId);
        Assert.False(reader.MoveNext(out _));
    }

    [Fact(DisplayName = "DocsAndFreqs: accumulates frequency without creating a position stream")]
    public void DocsAndFreqs_AccumulatesFrequencyWithoutPositions()
    {
        using var store = new PostingsStore();
        store.Add("body", "term", 5, 42, FieldIndexOptions.DocsAndFreqs, null, 0, 0);
        store.Add("body", "term", 5, 43, FieldIndexOptions.DocsAndFreqs, null, 0, 0);
        store.Add("body", "term", 10, 0, FieldIndexOptions.DocsAndFreqs, null, 0, 0);
        store.Freeze();

        int termId = FindTerm(store, "body\0term");
        ref readonly var state = ref store.GetTermState(termId);
        Assert.Equal(PostingFlags.HasFreqs, state.Flags);
        var reader = store.OpenDocReader(termId);
        Assert.True(reader.MoveNext(out var first));
        Assert.Equal(2, first.Freq);
        Assert.Equal(0, first.PositionCount);
        Assert.True(reader.MoveNext(out var second));
        Assert.Equal(1, second.Freq);
        Assert.False(reader.MoveNext(out _));
    }

    [Fact(DisplayName = "Positions: widens term flags and preserves position order")]
    public void Positions_WidensTermFlagsAndPreservesPositionOrder()
    {
        using var store = new PostingsStore();
        store.Add("body", "term", 5, 10, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        store.Add("body", "term", 5, 20, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        store.Add("body", "term", 10, 5, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        store.Freeze();

        int termId = FindTerm(store, "body\0term");
        ref readonly var state = ref store.GetTermState(termId);
        Assert.Equal(PostingFlags.HasFreqs | PostingFlags.HasPositions, state.Flags);

        var docs = store.OpenDocReader(termId);
        var prox = store.OpenProxReader(termId);
        Assert.True(docs.MoveNext(out var first));
        Assert.Equal(2, first.Freq);
        prox.StartDocument();
        Assert.True(prox.ReadNext(out var firstPosition));
        Assert.Equal(10, firstPosition.Position);
        prox.SkipPayload();
        Assert.True(prox.ReadNext(out var secondPosition));
        Assert.Equal(20, secondPosition.Position);
        prox.SkipPayload();
        Assert.True(docs.MoveNext(out var second));
        Assert.Equal(1, second.Freq);
        prox.StartDocument();
        Assert.True(prox.ReadNext(out var thirdPosition));
        Assert.Equal(5, thirdPosition.Position);
        prox.SkipPayload();
        Assert.False(prox.ReadNext(out _));
        Assert.False(docs.MoveNext(out _));
    }

    [Fact(DisplayName = "Offsets and payloads: union flags preserve optional position metadata")]
    public void OffsetsAndPayloads_PreserveOptionalPositionMetadata()
    {
        using var store = new PostingsStore(storePayloads: true, storeTermVectors: true);
        store.Add("body", "term", 0, 4,
            FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets, [1, 2, 3], 7, 11);
        store.Add("body", "term", 0, 9,
            FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        store.Freeze();

        int termId = FindTerm(store, "body\0term");
        ref readonly var state = ref store.GetTermState(termId);
        Assert.Equal(
            PostingFlags.HasFreqs | PostingFlags.HasPositions | PostingFlags.HasPayloads | PostingFlags.HasOffsets,
            state.Flags);

        var prox = store.OpenProxReader(termId);
        prox.StartDocument();
        Assert.True(prox.ReadNext(out var first));
        Assert.Equal(4, first.Position);
        Assert.Equal(3, first.PayloadLength);
        Assert.True(first.HasOffsets);
        Assert.Equal(7, first.StartOffset);
        Assert.Equal(11, first.EndOffset);
        byte[] payload = new byte[first.PayloadLength];
        prox.CopyPayloadTo(payload);
        Assert.Equal(new byte[] { 1, 2, 3 }, payload);

        Assert.True(prox.ReadNext(out var second));
        Assert.Equal(9, second.Position);
        Assert.Equal(0, second.PayloadLength);
        Assert.False(second.HasOffsets);
        prox.SkipPayload();
        Assert.False(prox.ReadNext(out _));
    }

    [Fact(DisplayName = "Mixed options: retains qualified UTF-8 term bytes without per-term arrays")]
    public void MixedOptions_RetainsQualifiedUtf8TermBytes()
    {
        string term = new('λ', 300);
        using var store = new PostingsStore();
        store.AddDocOnly("тело", term, 0);
        store.Add("тело", term, 0, 2, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        store.Freeze();

        int termId = FindTerm(store, $"тело\0{term}");
        Assert.True(termId >= 0);
        Assert.Equal($"тело\0{term}", Encoding.UTF8.GetString(store.GetTerm(termId)));
        Assert.True(store.AllocatedBytes > 0);
    }

    private static int FindTerm(PostingsStore store, string qualifiedTerm)
        => store.TermHash.Find(Encoding.UTF8.GetBytes(qualifiedTerm));
}
