using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;


internal readonly record struct ShapeDocValuesFieldMetadata(
    string FieldName,
    SpatialFieldKind Kind,
    int MaxDoc,
    int RecordCount,
    long SectionOffset,
    long SectionLength,
    long RecordDirectoryOffset);
