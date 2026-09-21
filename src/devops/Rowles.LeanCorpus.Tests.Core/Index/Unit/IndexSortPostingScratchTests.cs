using System.Buffers;
using Rowles.LeanCorpus.Index.Indexer.Postings;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class IndexSortPostingScratchTests
{
    [Fact]
    public void Scratch_GrowsResetsAndReturnsPooledBuffersExactlyOnce()
    {
        var docPool = new TrackingPool<PostingSortDoc>();
        var positionPool = new TrackingPool<PostingSortPosition>();
        var payloadPool = new TrackingPool<byte>();
        var scratch = new IndexSortPostingScratch(docPool, positionPool, payloadPool);

        for (int i = 0; i < 300; i++)
        {
            ref var doc = ref scratch.AppendDoc();
            doc.NewDocId = 300 - i;
            doc.PositionStart = scratch.PositionCount;
            doc.PositionCount = 1;

            ref var position = ref scratch.AppendPosition();
            position.Position = i;
            position.PayloadOffset = scratch.PayloadCount;
            position.PayloadLength = 2;
            scratch.GetPayloadDestination(2).Fill((byte)i);
            scratch.AdvancePayload(2);
        }

        Assert.Equal(300, scratch.DocCount);
        Assert.Equal(300, scratch.PositionCount);
        Assert.Equal(600, scratch.PayloadCount);
        Assert.True(docPool.RentCount > 1);
        Assert.True(positionPool.RentCount > 1);
        Assert.True(payloadPool.RentCount > 1);

        scratch.Reset();
        Assert.Equal(0, scratch.DocCount);
        Assert.Equal(0, scratch.PositionCount);
        Assert.Equal(0, scratch.PayloadCount);
        ref var resetDoc = ref scratch.AppendDoc();
        resetDoc.NewDocId = 0;
        Assert.Equal(1, scratch.DocCount);

        scratch.Dispose();
        scratch.Dispose();
        Assert.Equal(0, docPool.ActiveCount);
        Assert.Equal(0, positionPool.ActiveCount);
        Assert.Equal(0, payloadPool.ActiveCount);
        Assert.Equal(0, docPool.DoubleReturnCount);
        Assert.Equal(0, positionPool.DoubleReturnCount);
        Assert.Equal(0, payloadPool.DoubleReturnCount);
    }

    private sealed class TrackingPool<T> : ArrayPool<T>
    {
        private readonly HashSet<T[]> _active = [];

        internal int RentCount { get; private set; }
        internal int ActiveCount => _active.Count;
        internal int DoubleReturnCount { get; private set; }

        public override T[] Rent(int minimumLength)
        {
            var array = new T[Math.Max(minimumLength, 256)];
            _active.Add(array);
            RentCount++;
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
