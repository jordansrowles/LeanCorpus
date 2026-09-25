using System.Buffers;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Index.Indexer;

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
