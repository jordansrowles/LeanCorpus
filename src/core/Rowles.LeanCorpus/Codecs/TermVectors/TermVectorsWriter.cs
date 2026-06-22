namespace Rowles.LeanCorpus.Codecs.TermVectors;

/// <summary>
/// Writes per-document term vectors to .tvd (data) and .tvx (offset index) files.
/// Format: .tvx: [docCount:int32] [long[] offsets into .tvd]
///         .tvd per doc: [fieldCount:int32] per field: [fieldName:string] [termCount:int32]
///              per term: [term:string] [freq:int32] [posCount:int32] [positions:int32[]] [hasPayloads:bool] [payloads]
/// </summary>
internal static class TermVectorsWriter
{
    public static void Write(string tvdPath, string tvxPath,
        IReadOnlyList<Dictionary<string, List<TermVectorEntry>>?> docs)
    {
        using var tvdFs = new FileStream(tvdPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var tvdWriter = new BinaryWriter(tvdFs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(tvdWriter, CodecConstants.TermVectorsVersion);

        var offsets = new long[docs.Count];

        for (int d = 0; d < docs.Count; d++)
        {
            offsets[d] = tvdFs.Position;
            var fields = docs[d];
            if (fields is null)
            {
                tvdWriter.Write(0);
                continue;
            }
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
                    WritePayloads(tvdWriter, entry);
                }
            }
        }

        tvdFs.Flush(flushToDisk: true);
        // Write .tvx index
        using var tvxFs = new FileStream(tvxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var tvxWriter = new BinaryWriter(tvxFs, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(tvxWriter, CodecConstants.TermVectorsVersion);

        tvxWriter.Write(docs.Count);
        foreach (var offset in offsets)
            tvxWriter.Write(offset);
        tvxFs.Flush(flushToDisk: true);
    }

    private static void WritePayloads(BinaryWriter writer, TermVectorEntry entry)
    {
        bool hasPayloads = entry.Payloads is { Length: > 0 } payloads
            && payloads.Any(static payload => payload is { Length: > 0 });
        writer.Write(hasPayloads);

        if (!hasPayloads)
            return;

        if (entry.Payloads is null || entry.Payloads.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector payload count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.Payloads.Length; i++)
        {
            var payload = entry.Payloads[i];
            writer.Write(payload?.Length ?? 0);
            if (payload is { Length: > 0 })
                writer.Write(payload);
        }
    }
}
