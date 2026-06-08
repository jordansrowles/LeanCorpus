using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

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
        // Buffer .tvd body and track body-relative offsets.
        var bodyOffsets = new long[docs.Count];
        byte[] tvdBody;
        var bodyBuf = new ArrayBufferWriter<byte>(4096);
        for (int d = 0; d < docs.Count; d++)
        {
            bodyOffsets[d] = bodyBuf.WrittenCount;
            var fields = docs[d];
            if (fields is null)
            {
                bodyBuf.WriteInt32(0);
                continue;
            }
            bodyBuf.WriteInt32(fields.Count);
            foreach (var (fieldName, entries) in fields)
            {
                bodyBuf.WriteString(fieldName);
                bodyBuf.WriteInt32(entries.Count);
                foreach (var entry in entries)
                {
                    bodyBuf.WriteString(entry.Term);
                    bodyBuf.WriteInt32(entry.Freq);
                    bodyBuf.WriteInt32(entry.Positions.Length);
                    foreach (var pos in entry.Positions)
                        bodyBuf.WriteInt32(pos);
                    WritePayloads(bodyBuf, entry);
                    WriteOffsets(bodyBuf, entry);
                }
            }
        }
        tvdBody = bodyBuf.WrittenSpan.ToArray();

        // Write .tvd file and capture envelope header size for offset computation.
        long headerSize;
        using (var tvdOutput = new IndexOutput(tvdPath))
        {
            CodecFileHeader.Write(tvdOutput, CodecFormats.TermVectors, tvdBody);
            headerSize = tvdOutput.Position - tvdBody.Length;
        }

        // Compute file-absolute offsets: envelope header size + body-relative offset.
        var offsets = new long[docs.Count];
        for (int d = 0; d < docs.Count; d++)
            offsets[d] = headerSize + bodyOffsets[d];

        // Buffer .tvx body.
        var tvxBodyBuf = new ArrayBufferWriter<byte>(1024);
        tvxBodyBuf.WriteInt32(docs.Count);
        foreach (var offset in offsets)
            tvxBodyBuf.WriteInt64(offset);
        byte[] tvxBody = tvxBodyBuf.WrittenSpan.ToArray();

        // Write .tvx index.
        using var tvxOutput = new IndexOutput(tvxPath);
        CodecFileHeader.Write(tvxOutput, CodecFormats.TermVectors, tvxBody);
    }


    private static void WriteOffsets(IBufferWriter<byte> writer, TermVectorEntry entry)
    {
        bool hasOffsets = entry.StartOffsets is { Length: > 0 } && entry.EndOffsets is { Length: > 0 };
        writer.WriteByte(hasOffsets ? (byte)1 : (byte)0);
        if (!hasOffsets) return;

        if (entry.StartOffsets!.Length != entry.Positions.Length || entry.EndOffsets!.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector offset count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.StartOffsets.Length; i++)
            writer.WriteInt32(entry.StartOffsets[i]);
        for (int i = 0; i < entry.EndOffsets.Length; i++)
            writer.WriteInt32(entry.EndOffsets[i]);
    }
    private static void WritePayloads(IBufferWriter<byte> writer, TermVectorEntry entry)
    {
        bool hasPayloads = entry.Payloads is { Length: > 0 } payloads
            && payloads.Any(static payload => payload is { Length: > 0 });
        writer.WriteByte(hasPayloads ? (byte)1 : (byte)0);

        if (!hasPayloads)
            return;

        if (entry.Payloads is null || entry.Payloads.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector payload count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.Payloads.Length; i++)
        {
            var payload = entry.Payloads[i];
            writer.WriteInt32(payload?.Length ?? 0);
            if (payload is { Length: > 0 })
                writer.WriteBytes(payload);
        }
    }
}
