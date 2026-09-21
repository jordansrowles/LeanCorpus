using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Index.Indexer.Postings;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class PostingsStoreTests
{
    [Fact]
    public void MixedOptionsAndPayloads_RoundTripAgainstReferenceModel()
    {
        var operations = new[]
        {
            new Operation("body", "apple", 0, 0, FieldIndexOptions.DocsOnly, null, 0, 0),
            new Operation("body", "apple", 0, 3, FieldIndexOptions.DocsAndFreqsAndPositions, [1, 2], 0, 0),
            new Operation("body", "apple", 0, 3, FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets, [3], 4, 9),
            new Operation("body", "apple", 1, 2, FieldIndexOptions.DocsAndFreqs, null, 0, 0),
            new Operation("body", "apple", 1, 5, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0),
            new Operation("body", "banana", 0, 0, FieldIndexOptions.DocsOnly, null, 0, 0),
            new Operation("body", "banana", 0, 0, FieldIndexOptions.DocsOnly, null, 0, 0),
            new Operation("body", "banana", 4, 0, FieldIndexOptions.DocsOnly, null, 0, 0),
            new Operation("タイトル", "значение", 2, 7, FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets, [7, 8, 9], 12, 21),
        };

        var model = new Dictionary<string, List<ModelPosting>>(StringComparer.Ordinal);
        using var store = new PostingsStore(storePayloads: true, storeTermVectors: true);
        foreach (var operation in operations)
        {
            if (operation.IndexOptions == FieldIndexOptions.DocsOnly && operation.Payload is null && operation.Position == 0)
                store.AddDocOnly(operation.Field, operation.Term, operation.DocId);
            else
                store.Add(operation.Field, operation.Term, operation.DocId, operation.Position,
                    operation.IndexOptions, operation.Payload, operation.StartOffset, operation.EndOffset);
            ApplyModel(model, operation);
        }

        store.Freeze();
        Assert.Throws<InvalidOperationException>(() =>
            store.AddDocOnly("body", "apple", 99));

        foreach (var pair in model)
        {
            int termId = FindQualifiedTerm(store, pair.Key);
            Assert.True(termId >= 0, pair.Key);
            var docs = new List<PostingDoc>();
            var docReader = store.OpenDocReader(termId);
            while (docReader.MoveNext(out var posting))
                docs.Add(posting);

            Assert.Equal(pair.Value.Count, docs.Count);
            var proxReader = store.OpenProxReader(termId);
            for (int i = 0; i < docs.Count; i++)
            {
                var expected = pair.Value[i];
                var actual = docs[i];
                Assert.Equal(expected.DocId, actual.DocId);
                Assert.Equal(expected.Freq, actual.Freq);
                Assert.Equal(expected.Positions.Count, actual.PositionCount);

                proxReader.StartDocument();
                for (int p = 0; p < expected.Positions.Count; p++)
                {
                    Assert.True(proxReader.ReadNext(out var header));
                    Assert.Equal(expected.Positions[p], header.Position);
                    Assert.Equal(expected.Payloads[p].Length, header.PayloadLength);
                    Assert.Equal(expected.HasOffsets[p], header.HasOffsets);
                    if (header.HasOffsets)
                    {
                        Assert.Equal(expected.StartOffsets[p], header.StartOffset);
                        Assert.Equal(expected.EndOffsets[p], header.EndOffset);
                    }

                    byte[] payload = new byte[header.PayloadLength];
                    proxReader.CopyPayloadTo(payload);
                    Assert.Equal(expected.Payloads[p], payload);
                }
            }
            Assert.False(proxReader.ReadNext(out _));
        }
    }

    [Fact]
    public void LargeUnicodeTermAndPayloadUseArenaWithoutChangingQualifiedTermFormat()
    {
        string term = new('λ', 300);
        byte[] payload = Enumerable.Range(0, 50_000).Select(static i => (byte)i).ToArray();
        using var store = new PostingsStore(storePayloads: true, storeTermVectors: false);
        store.Add("body", term, 0, 11, FieldIndexOptions.DocsAndFreqsAndPositions, payload, 0, 0);
        store.Add("body", term, 0, 12, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        store.Freeze();

        int termId = FindQualifiedTerm(store, $"body\0{term}");
        Assert.True(termId >= 0);
        Assert.Equal($"body\0{term}", Encoding.UTF8.GetString(store.GetTerm(termId)));
        Assert.True(store.Arena.BlockCount > 1);

        var prox = store.OpenProxReader(termId);
        prox.StartDocument();
        Assert.True(prox.ReadNext(out var first));
        Assert.Equal(payload.Length, first.PayloadLength);
        byte[] actualPayload = new byte[payload.Length];
        prox.CopyPayloadTo(actualPayload);
        Assert.Equal(payload, actualPayload);
        Assert.True(prox.ReadNext(out var second));
        Assert.Equal(12, second.Position);
        prox.SkipPayload();
        Assert.False(prox.ReadNext(out _));
    }

    [Fact]
    public void StoreDispose_IsIdempotentAndClearsOwnedCapacity()
    {
        using var store = new PostingsStore();
        store.AddDocOnly("body", "one", 0);
        Assert.True(store.AllocatedBytes > 0);
        store.Dispose();
        store.Dispose();
        Assert.Equal(0, store.AllocatedBytes);
    }

    [Fact]
    public void AllocatedBytes_TracksTermStateHashArenaAndFieldPrefixCapacity()
    {
        using var store = new PostingsStore(storePayloads: true);
        long initial = store.AllocatedBytes;

        for (int i = 0; i < 300; i++)
            store.AddDocOnly("body", $"term-{i}", i);

        long afterTerms = store.AllocatedBytes;
        Assert.True(afterTerms > initial);

        store.AddDocOnly(new string('f', 512), "field-term", 300);
        long afterFieldPrefix = store.AllocatedBytes;
        Assert.True(afterFieldPrefix > afterTerms);

        byte[] payload = new byte[(2 * PostingsByteArena.BlockSize) + 17];
        store.Add("body", "large-payload", 301, 0,
            FieldIndexOptions.DocsAndFreqsAndPositions, payload, 0, 0);
        Assert.True(store.AllocatedBytes > afterFieldPrefix);
    }

    [Fact]
    public void StoreDispose_ReturnsArenaTermAndStatePoolsExactlyOnce()
    {
        var bytePool = new TrackingArrayPool<byte>(fill: 0xC1);
        var statePool = new TrackingArrayPool<PostingTermState>();
        var store = new PostingsStore(true, true, statePool, bytePool);
        store.Add("body", "term", 0, 1, FieldIndexOptions.DocsAndFreqsAndPositions, [1, 2, 3], 0, 2);
        store.Dispose();
        store.Dispose();

        Assert.Equal(0, bytePool.ActiveCount);
        Assert.Equal(0, statePool.ActiveCount);
        Assert.Equal(0, bytePool.DoubleReturnCount);
        Assert.Equal(0, statePool.DoubleReturnCount);
    }

    [Fact]
    public void InvalidOrderingAndPositionsAreRejected()
    {
        using var store = new PostingsStore();
        store.AddDocOnly("body", "term", 2);
        Assert.Throws<InvalidOperationException>(() => store.AddDocOnly("body", "term", 1));

        using var positions = new PostingsStore();
        positions.Add("body", "term", 0, 5, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0);
        Assert.Throws<InvalidOperationException>(() =>
            positions.Add("body", "term", 0, 4, FieldIndexOptions.DocsAndFreqsAndPositions, null, 0, 0));
    }

    private static int FindQualifiedTerm(PostingsStore store, string qualified)
        => store.TermHash.Find(Encoding.UTF8.GetBytes(qualified));

    private static void ApplyModel(Dictionary<string, List<ModelPosting>> model, Operation operation)
    {
        string key = $"{operation.Field}\0{operation.Term}";
        if (!model.TryGetValue(key, out var postings))
            model[key] = postings = [];

        ModelPosting posting;
        if (postings.Count == 0 || postings[^1].DocId != operation.DocId)
        {
            if (postings.Count > 0 && operation.DocId < postings[^1].DocId)
                throw new InvalidOperationException("The test model received an out-of-order document.");
            posting = new ModelPosting(operation.DocId);
            postings.Add(posting);
        }
        else
        {
            posting = postings[^1];
        }

        if (operation.IndexOptions >= FieldIndexOptions.DocsAndFreqs)
            posting.Freq = posting.Freq == 0 ? 1 : checked(posting.Freq + 1);
        if (operation.IndexOptions < FieldIndexOptions.DocsAndFreqsAndPositions)
            return;

        posting.Positions.Add(operation.Position);
        posting.Payloads.Add(operation.Payload ?? []);
        bool hasOffsets = operation.IndexOptions >= FieldIndexOptions.DocsAndFreqsAndPositionsAndOffsets;
        posting.HasOffsets.Add(hasOffsets);
        posting.StartOffsets.Add(hasOffsets ? operation.StartOffset : 0);
        posting.EndOffsets.Add(hasOffsets ? operation.EndOffset : 0);
    }

    private readonly record struct Operation(
        string Field,
        string Term,
        int DocId,
        int Position,
        FieldIndexOptions IndexOptions,
        byte[]? Payload,
        int StartOffset,
        int EndOffset);

    private sealed class ModelPosting(int docId)
    {
        internal int DocId { get; } = docId;
        internal int Freq { get; set; }
        internal List<int> Positions { get; } = [];
        internal List<byte[]> Payloads { get; } = [];
        internal List<bool> HasOffsets { get; } = [];
        internal List<int> StartOffsets { get; } = [];
        internal List<int> EndOffsets { get; } = [];
    }

    private sealed class TrackingArrayPool<T>(byte fill = 0) : ArrayPool<T>
    {
        private readonly HashSet<T[]> _active = [];

        internal int ActiveCount => _active.Count;
        internal int DoubleReturnCount { get; private set; }

        public override T[] Rent(int minimumLength)
        {
            var array = new T[Math.Max(minimumLength, 256)];
            if (typeof(T) == typeof(byte) && fill != 0)
                Array.Fill((byte[])(object)array, fill);
            _active.Add(array);
            return array;
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            if (!_active.Remove(array))
                DoubleReturnCount++;
            if (clearArray)
                Array.Clear(array);
        }
    }
}
