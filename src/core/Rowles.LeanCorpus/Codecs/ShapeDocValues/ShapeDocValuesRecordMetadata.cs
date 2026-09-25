using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;


internal readonly record struct ShapeDocValuesRecordMetadata(
    uint DocumentId,
    long RecordOffset,
    uint RecordLength,
    uint ValueCount,
    uint PrimitiveCount,
    SpatialDimension HighestDimension,
    byte Flags,
    uint Bound0,
    uint Bound1,
    uint Bound2,
    uint Bound3,
    uint Bound4,
    uint Bound5,
    double Accumulator0,
    double Accumulator1,
    double Accumulator2,
    double Weight,
    int TreeLength);
