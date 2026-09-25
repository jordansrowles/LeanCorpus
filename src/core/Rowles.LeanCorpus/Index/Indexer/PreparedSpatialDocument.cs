using System.Buffers;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Index.Indexer;

internal sealed class PreparedSpatialDocument : IDisposable
{
    private readonly ShapePrimitiveByteBuffer _primitiveBytes;
    private readonly List<PreparedShapeValue> _values = [];
    private int _disposed;

    internal PreparedSpatialDocument(Action<long>? allocationChanged = null, ArrayPool<byte>? pool = null)
        => _primitiveBytes = new ShapePrimitiveByteBuffer(allocationChanged, pool);

    internal long AllocatedBytes => _primitiveBytes.AllocatedBytes;
    internal IReadOnlyList<PreparedShapeValue> Values => _values;

    internal EncodedShapePrimitiveSink CreateSink(SpatialFieldKind kind)
        => new(_primitiveBytes, kind);

    internal void AddValue(
        int fieldIndex,
        string fieldName,
        SpatialFieldKind fieldKind,
        uint valueOrdinal,
        bool storeDocValues,
        EncodedShapePrimitiveSink sink)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (sink.Count <= 0)
            throw new ArgumentException("A shape field value must produce at least one primitive.", nameof(sink));
        _values.Add(new PreparedShapeValue(
            fieldIndex,
            fieldName,
            fieldKind,
            valueOrdinal,
            storeDocValues,
            sink.Offset,
            sink.Count));
    }

    internal ReadOnlyMemory<byte> GetPackedPrimitives(PreparedShapeValue value)
        => _primitiveBytes.GetMemory(value.ByteOffset, checked(value.PrimitiveCount * ShapePrimitiveCodec.PackedValueLength));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _primitiveBytes.Dispose();
        _values.Clear();
    }
}
