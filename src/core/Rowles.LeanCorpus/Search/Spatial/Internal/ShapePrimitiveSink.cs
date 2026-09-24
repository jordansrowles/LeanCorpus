namespace Rowles.LeanCorpus.Search.Spatial.Internal;

internal interface IShapePrimitiveSink
{
    int Count { get; }

    void Add(ShapePrimitive primitive);
}

internal sealed class ShapePrimitiveListSink(List<ShapePrimitive> values) : IShapePrimitiveSink
{
    public int Count => values.Count;

    public void Add(ShapePrimitive primitive) => values.Add(primitive);
}
