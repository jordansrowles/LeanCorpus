using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Index.Segment;

internal sealed partial class SegmentReaderState
{
    private ShapeDocValuesReader? _shapeDocValuesReader;
    private bool _shapeDocValuesReaderLoaded;

    internal IReadOnlyList<string> GetShapeDocValuesFieldNames()
        => EnsureShapeDocValuesReader()?.FieldNames.ToArray() ?? Array.Empty<string>();

    internal bool TryGetShapeDocValuesFieldMetadata(string field, out ShapeDocValuesFieldMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(field);
        ShapeDocValuesReader? reader = EnsureShapeDocValuesReader();
        if (reader is null || !reader.HasField(field))
        {
            metadata = default;
            return false;
        }

        metadata = reader.GetFieldMetadata(field);
        return true;
    }

    internal bool TryGetShapeDocValuesRecordMetadata(
        string field,
        int documentId,
        out ShapeDocValuesRecordMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(field);
        ShapeDocValuesReader? reader = EnsureShapeDocValuesReader();
        if (reader is null)
        {
            metadata = default;
            return false;
        }
        return reader.TryGetRecordMetadata(field, documentId, out metadata);
    }

    internal int VisitShapeDocValuesPrimitives(
        string field,
        int documentId,
        Action<ShapePrimitive> visitor)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(visitor);
        ShapeDocValuesReader? reader = EnsureShapeDocValuesReader();
        return reader?.VisitPrimitives(field, documentId, visitor) ?? 0;
    }

    internal byte[] ReadShapeDocValuesRecordBytes(string field, int documentId)
    {
        ArgumentNullException.ThrowIfNull(field);
        ShapeDocValuesReader? reader = EnsureShapeDocValuesReader();
        if (reader is null)
            throw new KeyNotFoundException("Shape DocValues file is not present.");
        return reader.ReadRecordBytes(field, documentId);
    }

    internal void ValidateShapeDocValuesChecksum()
        => EnsureShapeDocValuesReader()?.ValidateChecksum();

    internal void DeepValidateShapeDocValues()
        => EnsureShapeDocValuesReader()?.DeepValidate();

    private ShapeDocValuesReader? EnsureShapeDocValuesReader()
    {
        if (Volatile.Read(ref _shapeDocValuesReaderLoaded))
            return _shapeDocValuesReader;

        object lockObj = LazyInitializer.EnsureInitialized(ref _lazyInitLock)!;
        lock (lockObj)
        {
            if (_shapeDocValuesReaderLoaded)
                return _shapeDocValuesReader;

            if (!_files.Exists(".dvg"))
            {
                Volatile.Write(ref _shapeDocValuesReaderLoaded, true);
                return null;
            }

            ShapeDocValuesReader reader = ShapeDocValuesReader.Open(_files.OpenInput(".dvg"));
            _shapeDocValuesReader = reader;
            Volatile.Write(ref _shapeDocValuesReaderLoaded, true);
            return reader;
        }
    }
}
