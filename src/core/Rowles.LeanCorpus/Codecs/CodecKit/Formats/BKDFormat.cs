using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// BKD tree (.bkd) wire format (1-dimensional).
/// Layout: [fieldCount:Int32LE] per field:
///   [fieldNameLen:Int32LE][fieldName:UTF8][nodes...]
/// Each node is a recursive binary tree of leaf/internal markers.
/// </summary>
internal static class BKDFormat
{
    internal sealed class Data
    {
        public int FieldCount { get; init; }
        public IReadOnlyList<byte> FieldsData { get; init; } = [];
    }

    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount",  d => d.FieldCount,  Codec.Int32LE)
        .Field("fieldsLen",   d => d.FieldsData.Count, Codec.Int32LE)
        .Field("fieldsData",  d => d.FieldsData,  Codec.UInt8.RepeatFrom("fieldsLen"))
        .Build<int, int, IReadOnlyList<byte>>((fieldCount, fieldsLen, fieldsData) =>
            new Data { FieldCount = fieldCount, FieldsData = fieldsData });
}
