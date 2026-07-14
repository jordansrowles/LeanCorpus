using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Norms (.nrm) wire format (v1).
/// Layout: [fieldCount:Int32LE] per field:
///   [nameLen:VarInt][nameBytes][docCount:Int32LE]
///   [norms:UInt8*docCount]
///   [boostCount:Int32LE][boosts:Float32LE*boostCount]
/// </summary>
internal static class NormsFormat
{
    internal sealed class Data
    {
        public IReadOnlyList<Field> Fields { get; init; } = [];
    }
    internal sealed class Field
    {
        public string Name { get; init; } = "";
        public IReadOnlyList<byte> Norms { get; init; } = [];
        public float[]? Boosts { get; init; }
        public int DocCount => Norms.Count;
    }

    // Per-field codec
    private static readonly ICodec<Field> FieldCodec = Codec.Record<Field>()
        .Field("name",       f => f.Name,               Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount",    f => f.DocCount,           Codec.Int32LE)
        .Field("norms",       f => f.Norms,              Codec.UInt8.RepeatFrom("docCount"))
        .Field("boostCount",  f => f.Boosts?.Length ?? 0, Codec.Int32LE)
        .Field("boosts",      f => f.Boosts ?? [],       Codec.Float32LE.RepeatFrom("boostCount"))
        .Build<string, int, IReadOnlyList<byte>, int, float[]>((name, docCount, norms, boostCount, boosts) =>
            new Field { Name = name, Norms = norms, Boosts = boostCount == 0 ? null : boosts });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<Field>>((fieldCount, fields) => new Data { Fields = fields });
}
