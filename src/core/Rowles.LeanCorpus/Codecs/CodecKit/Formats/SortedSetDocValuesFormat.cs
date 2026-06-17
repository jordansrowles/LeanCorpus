using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Sorted-set doc-values (.dss) wire format.
/// Layout: [fieldCount:Int32LE] per field:
///   [nameLen:VarInt][nameBytes][docCount:Int32LE][ordCount:Int32LE]
///   [VarInt-prefixed terms *ordCount]
///   [docStartCount:Int32LE][docStarts:Int32LE*docStartCount]
///   [totalOrdinals:Int32LE][VarInt ordinals *totalOrdinals]
/// </summary>
internal static class SortedSetDocValuesFormat
{
    internal sealed class Data
    {
        public IReadOnlyList<Field> Fields { get; init; } = [];
    }
    internal sealed class Field
    {
        public string Name { get; init; } = "";
        public int DocCount { get; init; }
        public string[] Terms { get; init; } = [];
        public int[] DocStarts { get; init; } = [];
        public int[] Ordinals { get; init; } = [];
    }

    // Per-field codec
    private static readonly ICodec<Field> FieldCodec = Codec.Record<Field>()
        .Field("name",           f => f.Name,          Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount",        f => f.DocCount,      Codec.Int32LE)
        .Field("ordCount",        f => f.Terms.Length,  Codec.Int32LE)
        .Field("terms",           f => f.Terms,         Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow).RepeatFrom("ordCount"))
        .Field("docStartCount",   f => f.DocStarts.Length, Codec.Int32LE)
        .Field("docStarts",       f => f.DocStarts,     Codec.Int32LE.RepeatFrom("docStartCount"))
        .Field("totalOrdinals",   f => f.Ordinals.Length, Codec.Int32LE)
        .Field("ordinals",        f => f.Ordinals,      Codec.VarInt32.RepeatFrom("totalOrdinals"))
        .Build<string, int, int, string[], int, int[], int, int[]>(
            (name, docCount, ordCount, terms, docStartCount, docStarts, totalOrdinals, ordinals) =>
                new Field { Name = name, DocCount = docCount, Terms = terms, DocStarts = docStarts, Ordinals = ordinals });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<Field>>((fieldCount, fields) => new Data { Fields = fields });
}
