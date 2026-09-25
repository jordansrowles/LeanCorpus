using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal readonly record struct ShapeRecordMetadata(
    SpatialDimension HighestDimension,
    byte Flags,
    byte[] Bounds,
    double Accumulator0,
    double Accumulator1,
    double Accumulator2,
    double Weight);
