using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Sorted-numeric doc-values (.dsn) wire format.
/// Layout: [fieldCount:Int32LE] per field:
///   [nameLen:VarInt][nameBytes][docCount:Int32LE]
///   [docStartCount:Int32LE][docStarts:Int32LE*docStartCount]
///   [totalValues:Int32LE][Float64LE values *totalValues]
/// </summary>
internal static class SortedNumericDocValuesFormat
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
        public double[] Values { get; init; } = [];
    }

    // Per-field codec
    private static readonly ICodec<Field> FieldCodec = Codec.Record<Field>()
        .Field("name",         f => f.Name,            Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount",      f => f.DocCount,        Codec.Int32LE)
        .Field("docStartCount", f => f.DocStarts.Length, Codec.Int32LE)
        .Field("docStarts",     f => f.DocStarts,       Codec.Int32LE.RepeatFrom("docStartCount"))
        .Field("totalValues",   f => f.Values.Length,    Codec.Int32LE)
        .Field("values",        f => f.Values,           Codec.Float64LE.RepeatFrom("totalValues"))
        .Build<string, int, int, int[], int, double[]>(
            (name, docCount, docStartCount, docStarts, totalValues, values) =>
                new Field { Name = name, DocCount = docCount, DocStarts = docStarts, Values = values });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<Field>>((fieldCount, fields) => new Data { Fields = fields });
}
