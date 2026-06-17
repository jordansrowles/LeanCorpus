using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Sorted doc-values (.dvs) wire format.
/// Layout: [fieldCount:Int32LE] per field: [nameLen:VarInt][nameBytes][docCount:Int32LE][ordCount:Int32LE][VarInt-prefixed terms *ordCount][VarInt ordinals *docCount][presenceBitCount:Int32LE][VarInt*presenceBitCount]
/// </summary>
internal static class SortedDocValuesFormat
{
    internal sealed class Data
    {
        public IReadOnlyList<Field> Fields { get; init; } = [];
    }
    internal sealed class Field
    {
        public string Name { get; init; } = "";
        public string[] Terms { get; init; } = [];
        public int[] Ordinals { get; init; } = [];
        public int[]? Presence { get; init; }
    }

    // Per-field codec: name, docCount, ordCount, terms, ordinals, presenceBitCount, presence
    private static readonly ICodec<Field> FieldCodec = Codec.Record<Field>()
        .Field("name",           f => f.Name,               Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount",        f => f.Ordinals.Length,    Codec.Int32LE)
        .Field("ordCount",        f => f.Terms.Length,       Codec.Int32LE)
        .Field("terms",           f => f.Terms,              Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow).RepeatFrom("ordCount"))
        .Field("ordinals",        f => f.Ordinals,           Codec.VarInt32.RepeatFrom("docCount"))
        .Field("presenceBitCount", f => f.Presence?.Length ?? 0, Codec.Int32LE)
        .Field("presence",        f => f.Presence ?? [],     Codec.VarInt32.RepeatFrom("presenceBitCount"))
        .Build<string, int, int, string[], int[], int, int[]>((name, docCount, ordCount, terms, ordinals, presenceBitCount, presence) =>
            new Field { Name = name, Terms = terms, Ordinals = ordinals, Presence = presenceBitCount == 0 ? null : presence });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<Field>>((fieldCount, fields) => new Data { Fields = fields });
}
