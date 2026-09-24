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

internal readonly record struct PreparedShapeValue(
    int FieldIndex,
    string FieldName,
    SpatialFieldKind FieldKind,
    uint ValueOrdinal,
    bool StoreDocValues,
    int ByteOffset,
    int PrimitiveCount);

internal sealed class EncodedShapePrimitiveSink : IShapePrimitiveSink
{
    private const int MaximumOutputPrimitives = 1_000_000;
    private readonly ShapePrimitiveByteBuffer _buffer;
    private readonly SpatialFieldKind _fieldKind;

    internal EncodedShapePrimitiveSink(ShapePrimitiveByteBuffer buffer, SpatialFieldKind fieldKind)
    {
        _buffer = buffer;
        _fieldKind = fieldKind;
        Offset = buffer.Length;
    }

    internal int Offset { get; }
    public int Count { get; private set; }

    public void Add(ShapePrimitive primitive)
    {
        if (Count >= MaximumOutputPrimitives)
            throw new ArgumentException($"A shape value cannot emit more than {MaximumOutputPrimitives} output primitives.");
        _buffer.Append(primitive, _fieldKind);
        Count++;
    }
}
