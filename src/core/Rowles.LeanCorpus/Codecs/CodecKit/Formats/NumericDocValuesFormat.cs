using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Numeric doc-values (.dvn) wire format.
/// Layout: [fieldCount:Int32LE] per field: [nameLen:VarInt][nameBytes][docCount:Int32LE][Float64LE*docCount][presenceBitCount:Int32LE][VarInt*presenceBitCount]
/// </summary>
internal static class NumericDocValuesFormat
{
    internal sealed class Data
    {
        public IReadOnlyList<Field> Fields { get; init; } = [];
    }
    internal sealed class Field
    {
        public string Name { get; init; } = "";
        public double[] Values { get; init; } = [];
        public int[]? Presence { get; init; }
    }

    // Per-field codec: name (VarInt-prefixed UTF-8), docCount (Int32LE), values (Float64LE repeated), presenceBitCount (Int32LE), presence (VarInt repeated)
    private static readonly ICodec<Field> FieldCodec = Codec.Record<Field>()
        .Field("name",           f => f.Name,               Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount",        f => f.Values.Length,      Codec.Int32LE)
        .Field("values",          f => f.Values,             Codec.Float64LE.RepeatFrom("docCount"))
        .Field("presenceBitCount", f => f.Presence?.Length ?? 0, Codec.Int32LE)
        .Field("presence",        f => f.Presence ?? [],     Codec.VarInt32.RepeatFrom("presenceBitCount"))
        .Build<string, int, double[], int, int[]>((name, docCount, values, presenceBitCount, presence) =>
            new Field { Name = name, Values = values, Presence = presenceBitCount == 0 ? null : presence });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<Field>>((fieldCount, fields) => new Data { Fields = fields });
}
