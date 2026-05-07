namespace Rowles.LeanLucene.Codecs.TermVectors;

/// <summary>
/// Writes per-document term vectors to .tvd (data) and .tvx (offset index) files.
/// Format: .tvx: [docCount:int32] [long[] offsets into .tvd]
///         .tvd per doc: [fieldCount:int32] per field: [fieldName:string] [termCount:int32]
///              per term: [term:string] [freq:int32] [posCount:int32] [positions:int32[]]
/// </summary>
internal static class TermVectorsWriter
{
    public static void Write(string tvdPath, string tvxPath,
        IReadOnlyList<Dictionary<string, List<TermVectorEntry>>> docs)
    {
        using var tvdFs = new FileStream(tvdPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var tvdWriter = new BinaryWriter(tvdFs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(tvdWriter, CodecConstants.TermVectorsVersion);

        var offsets = new long[docs.Count];

        for (int d = 0; d < docs.Count; d++)
        {
            offsets[d] = tvdFs.Position;
            var fields = docs[d];
            tvdWriter.Write(fields.Count);
            foreach (var (fieldName, entries) in fields)
            {
                tvdWriter.Write(fieldName);
                tvdWriter.Write(entries.Count);
                foreach (var entry in entries)
                {
                    tvdWriter.Write(entry.Term);
                    tvdWriter.Write(entry.Freq);
                    tvdWriter.Write(entry.Positions.Length);
                    foreach (var pos in entry.Positions)
                        tvdWriter.Write(pos);
                }
            }
        }

        // Write .tvx index
        using var tvxFs = new FileStream(tvxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var tvxWriter = new BinaryWriter(tvxFs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(tvxWriter, CodecConstants.TermVectorsVersion);

        tvxWriter.Write(docs.Count);
        foreach (var offset in offsets)
            tvxWriter.Write(offset);
    }
}
