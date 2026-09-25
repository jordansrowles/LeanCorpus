using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal readonly record struct ShapeDocValuesRecord(
    int DocumentId,
    int ByteOffset,
    int PrimitiveCount,
    uint ValueCount);
