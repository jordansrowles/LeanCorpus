using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.TermVectors;

/// <summary>
/// Streaming variant of <see cref="TermVectorsWriter"/> for the merge path.
/// Per-doc term vectors are appended directly to .tvd; .tvx is written on dispose.
/// Only the offsets list is buffered (8B per doc).
/// </summary>
internal sealed class TermVectorsStreamWriter : IDisposable
{
    private readonly string _tvdPath;
    private readonly string _tvxPath;
    private readonly ArrayBufferWriter<byte> _tvdBuf;
    private readonly List<long> _offsets;
    private bool _disposed;

    internal TermVectorsStreamWriter(string tvdPath, string tvxPath)
    {
        _tvdPath = tvdPath;
        _tvxPath = tvxPath;
        _tvdBuf = new ArrayBufferWriter<byte>(4096);
        _offsets = new List<long>();
    }

    internal void AddDocument(IReadOnlyDictionary<string, List<TermVectorEntry>>? fields)
    {
        _offsets.Add(_tvdBuf.WrittenCount);
        if (fields is null)
        {
            _tvdBuf.WriteInt32(0);
            return;
        }

        _tvdBuf.WriteInt32(fields.Count);
        foreach (var (fieldName, entries) in fields)
        {
            _tvdBuf.WriteString(fieldName);
            _tvdBuf.WriteInt32(entries.Count);
            foreach (var entry in entries)
            {
                _tvdBuf.WriteString(entry.Term);
                _tvdBuf.WriteInt32(entry.Freq);
                _tvdBuf.WriteInt32(entry.Positions.Length);
                foreach (var pos in entry.Positions)
                    _tvdBuf.WriteInt32(pos);
                WritePayloads(entry);
                WriteOffsets(entry);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        byte[] tvdBody = _tvdBuf.WrittenSpan.ToArray();

        long headerSize;
        using (var tvdOutput = new IndexOutput(_tvdPath))
        {
            CodecFileHeader.Write(tvdOutput, CodecFormats.TermVectors, tvdBody);
            headerSize = tvdOutput.Position - tvdBody.Length;
        }

        // Re-base body-relative offsets to file-absolute positions.
        for (int i = 0; i < _offsets.Count; i++)
            _offsets[i] += headerSize;

        var tvxBodyBuf = new ArrayBufferWriter<byte>(1024);
        tvxBodyBuf.WriteInt32(_offsets.Count);
        foreach (var off in _offsets)
            tvxBodyBuf.WriteInt64(off);
        byte[] tvxBody = tvxBodyBuf.WrittenSpan.ToArray();

        using var tvxOutput = new IndexOutput(_tvxPath);
        CodecFileHeader.Write(tvxOutput, CodecFormats.TermVectors, tvxBody);
    }

    private void WriteOffsets(TermVectorEntry entry)
    {
        bool hasOffsets = entry.StartOffsets is { Length: > 0 } && entry.EndOffsets is { Length: > 0 };
        _tvdBuf.WriteByte(hasOffsets ? (byte)1 : (byte)0);
        if (!hasOffsets) return;

        if (entry.StartOffsets!.Length != entry.Positions.Length || entry.EndOffsets!.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector offset count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.StartOffsets.Length; i++)
            _tvdBuf.WriteInt32(entry.StartOffsets[i]);
        for (int i = 0; i < entry.EndOffsets.Length; i++)
            _tvdBuf.WriteInt32(entry.EndOffsets[i]);
    }

    private void WritePayloads(TermVectorEntry entry)
    {
        bool hasPayloads = entry.Payloads is { Length: > 0 } payloads
            && payloads.Any(static payload => payload is { Length: > 0 });
        _tvdBuf.WriteByte(hasPayloads ? (byte)1 : (byte)0);

        if (!hasPayloads)
            return;

        if (entry.Payloads is null || entry.Payloads.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector payload count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.Payloads.Length; i++)
        {
            var payload = entry.Payloads[i];
            _tvdBuf.WriteInt32(payload?.Length ?? 0);
            if (payload is { Length: > 0 })
                _tvdBuf.WriteBytes(payload);
        }
    }
}
