using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Binary doc-values (.dvb) wire format.
/// Layout: [fieldCount:Int32LE] per field:
///   [nameLen:VarInt][nameBytes][docCount:Int32LE]
///   [docStartCount:Int32LE][docStarts:Int32LE*docStartCount]
///   [valueCount:Int32LE][per value: [len:Int32LE][bytes*len]]
/// </summary>
internal static class BinaryDocValuesFormat
{
    internal sealed class Data
    {
        public IReadOnlyList<Field> Fields { get; init; } = [];
    }
    internal sealed class Field
    {
        public string Name { get; init; } = "";
        public int DocCount { get; init; }
        public int[] DocStarts { get; init; } = [];
        public IReadOnlyList<byte[]> Values { get; init; } = [];
    }

    // Codec for a single byte[] value: Int32LE length prefix followed by the bytes.
    private static readonly ICodec<byte[]> ByteArrayCodec =
        Codec.LengthPrefixed(Codec.Int32LE, Codec.BytesOwnedRemaining());

    // Per-field codec
    private static readonly ICodec<Field> FieldCodec = Codec.Record<Field>()
        .Field("name",         f => f.Name,            Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount",      f => f.DocCount,        Codec.Int32LE)
        .Field("docStartCount", f => f.DocStarts.Length, Codec.Int32LE)
        .Field("docStarts",     f => f.DocStarts,       Codec.Int32LE.RepeatFrom("docStartCount"))
        .Field("valueCount",    f => f.Values.Count,    Codec.Int32LE)
        .Field("values",        f => f.Values,           ByteArrayCodec.RepeatFrom("valueCount"))
        .Build<string, int, int, int[], int, IReadOnlyList<byte[]>>(
            (name, docCount, docStartCount, docStarts, valueCount, values) =>
                new Field { Name = name, DocCount = docCount, DocStarts = docStarts, Values = values });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<Field>>((fieldCount, fields) => new Data { Fields = fields });
}
