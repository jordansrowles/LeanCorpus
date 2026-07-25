using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.TermVectors;

/// <summary>
/// Writes per-document term vectors to .tvd (data) and .tvx (offset index) files (v3 format).
/// Format: .tvx: [docCount:int32] [long[] offsets into .tvd]
///         .tvd per doc: [fieldCount:int32] per field: [fieldName:string] [termCount:int32]
///              per term: [term:string] [freq:int32] [posCount:int32] [positions:int32[]]
///                        [hasPayloads:bool] [payloads] [hasOffsets:bool] [startOffsets:int32[]] [endOffsets:int32[]]
/// </summary>
/// <remarks>
/// <para>Version history:</para>
/// <para>v1: per-term layout ended after payloads.  No offset data.</para>
/// <para>v2: adds [hasOffsets:bool] with conditional [startOffsets:int32[]] [endOffsets:int32[]] for highlighters.</para>
/// <para>v3: CodecKit trailer format (streaming write, 8-byte Int64 trailer).</para>
/// </remarks>
internal static class TermVectorsWriter
{
    public static void Write(string tvdPath, string tvxPath,
        IReadOnlyList<Dictionary<string, List<TermVectorEntry>>?> docs)
    {
        // Track file-absolute offsets; BeginStreamingWrite writes the version byte
        // at position 0, so body starts at position 1.  Offsets are thus file-absolute.
        var offsets = new long[docs.Count];

        // Write .tvd data via streaming scope (trailer written on dispose).
        using (var tvdOutput = new IndexOutput(tvdPath))
        using (var tvdScope = CodecFileHeader.BeginStreamingWrite(tvdOutput, CodecConstants.TermVectorsVersion))
        {
            for (int d = 0; d < docs.Count; d++)
            {
                offsets[d] = tvdScope.Output.Position;
                var fields = docs[d];
                if (fields is null)
                {
                    tvdScope.Output.WriteInt32(0);
                    continue;
                }
                tvdScope.Output.WriteInt32(fields.Count);
                foreach (var (fieldName, entries) in fields)
                {
                    tvdScope.Output.WriteString(fieldName);
                    tvdScope.Output.WriteInt32(entries.Count);
                    foreach (var entry in entries)
                    {
                        tvdScope.Output.WriteString(entry.Term);
                        tvdScope.Output.WriteInt32(entry.Freq);
                        tvdScope.Output.WriteInt32(entry.Positions.Length);
                        foreach (var pos in entry.Positions)
                            tvdScope.Output.WriteInt32(pos);
                        WritePayloads(tvdScope.Output, entry);
                        WriteOffsets(tvdScope.Output, entry);
                    }
                }
            }
        }

        // Write .tvx offset index via streaming scope.
        using (var tvxOutput = new IndexOutput(tvxPath))
        using (var tvxScope = CodecFileHeader.BeginStreamingWrite(tvxOutput, CodecConstants.TermVectorsVersion))
        {
            tvxScope.Output.WriteInt32(docs.Count);
            foreach (var offset in offsets)
                tvxScope.Output.WriteInt64(offset);
        }
    }

    private static void WriteOffsets(IndexOutput output, TermVectorEntry entry)
    {
        bool hasOffsets = entry.StartOffsets is { Length: > 0 } && entry.EndOffsets is { Length: > 0 };
        output.WriteByte(hasOffsets ? (byte)1 : (byte)0);
        if (!hasOffsets) return;

        if (entry.StartOffsets!.Length != entry.Positions.Length || entry.EndOffsets!.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector offset count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.StartOffsets.Length; i++)
            output.WriteInt32(entry.StartOffsets[i]);
        for (int i = 0; i < entry.EndOffsets.Length; i++)
            output.WriteInt32(entry.EndOffsets[i]);
    }

    private static void WritePayloads(IndexOutput output, TermVectorEntry entry)
    {
        bool hasPayloads = entry.Payloads is { Length: > 0 } payloads
            && payloads.Any(static payload => payload is { Length: > 0 });
        output.WriteByte(hasPayloads ? (byte)1 : (byte)0);

        if (!hasPayloads)
            return;

        if (entry.Payloads is null || entry.Payloads.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector payload count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.Payloads.Length; i++)
        {
            var payload = entry.Payloads[i];
            output.WriteInt32(payload?.Length ?? 0);
            if (payload is { Length: > 0 })
                output.WriteBytes(payload);
        }
    }
}
