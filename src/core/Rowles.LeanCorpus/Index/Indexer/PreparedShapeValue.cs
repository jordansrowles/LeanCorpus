using System.Buffers;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Index.Indexer;

internal readonly record struct PreparedShapeValue(
    int FieldIndex,
    string FieldName,
    SpatialFieldKind FieldKind,
    uint ValueOrdinal,
    bool StoreDocValues,
    int ByteOffset,
    int PrimitiveCount);
