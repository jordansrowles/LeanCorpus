using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Formats;

/// <summary>
/// Field-lengths (.fln) wire format.
/// Layout: [fieldCount:Int32LE] per field: [nameLen:VarInt][nameBytes][docCount:Int32LE][VarInt*docCount]
/// </summary>
internal static class FieldLengthsFormat
{
    internal sealed class Data
    {
        public IReadOnlyList<FieldEntry> Fields { get; init; } = [];
    }
    internal sealed class FieldEntry
    {
        public string Name { get; init; } = "";
        public int[] Lengths { get; init; } = [];
        public int DocCount => Lengths.Length;
    }

    // Per-field codec: name (VarInt-prefixed UTF-8), docCount (Int32LE), lengths (VarInt repeated)
    private static readonly ICodec<FieldEntry> FieldCodec = Codec.Record<FieldEntry>()
        .Field("name",    e => e.Name,    Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow))
        .Field("docCount", e => e.DocCount, Codec.Int32LE)
        .Field("lengths",  e => e.Lengths, Codec.VarInt32.RepeatFrom("docCount"))
        .Build<string, int, int[]>((name, docCount, lengths) => new FieldEntry { Name = name, Lengths = lengths });

    // File-level codec: fieldCount (Int32LE), then fields repeated
    internal static readonly ICodec<Data> V1 = Codec.Record<Data>()
        .Field("fieldCount", d => d.Fields.Count, Codec.Int32LE)
        .Field("fields",     d => d.Fields,       FieldCodec.RepeatFrom("fieldCount"))
        .Build<int, IReadOnlyList<FieldEntry>>((fieldCount, fields) => new Data { Fields = fields });
}
