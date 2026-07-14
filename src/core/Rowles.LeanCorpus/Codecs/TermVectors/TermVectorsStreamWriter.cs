using Rowles.LeanCorpus.Codecs.CodecKit;
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
    private readonly IndexOutput _tvdOutput;
    private readonly CodecFileHeader.StreamingWriteScope _tvdScope;
    private readonly List<long> _offsets;
    private bool _disposed;

    internal TermVectorsStreamWriter(string tvdPath, string tvxPath)
    {
        _tvdPath = tvdPath;
        _tvxPath = tvxPath;
        _tvdOutput = new IndexOutput(tvdPath, durable: true);
        _tvdScope = CodecFileHeader.BeginStreamingWrite(_tvdOutput, CodecConstants.TermVectorsVersion);
        _offsets = new List<long>();
    }

    internal void AddDocument(IReadOnlyDictionary<string, List<TermVectorEntry>>? fields)
    {
        _offsets.Add(_tvdScope.Output.Position);
        if (fields is null)
        {
            _tvdScope.Output.WriteInt32(0);
            return;
        }

        _tvdScope.Output.WriteInt32(fields.Count);
        foreach (var (fieldName, entries) in fields)
        {
            _tvdScope.Output.WriteString(fieldName);
            _tvdScope.Output.WriteInt32(entries.Count);
            foreach (var entry in entries)
            {
                _tvdScope.Output.WriteString(entry.Term);
                _tvdScope.Output.WriteInt32(entry.Freq);
                _tvdScope.Output.WriteInt32(entry.Positions.Length);
                foreach (var pos in entry.Positions)
                    _tvdScope.Output.WriteInt32(pos);
                WritePayloads(entry);
                WriteOffsets(entry);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Finish .tvd: dispose scope (writes trailer), then dispose the underlying output.
        _tvdScope.Dispose();
        _tvdOutput.Dispose();

        // Write .tvx offset index via streaming scope.
        using var tvxOutput = new IndexOutput(_tvxPath, durable: true);
        using var tvxScope = CodecFileHeader.BeginStreamingWrite(tvxOutput, CodecConstants.TermVectorsVersion);
        tvxScope.Output.WriteInt32(_offsets.Count);
        foreach (var off in _offsets)
            tvxScope.Output.WriteInt64(off);
    }

    private void WriteOffsets(TermVectorEntry entry)
    {
        bool hasOffsets = entry.StartOffsets is { Length: > 0 } && entry.EndOffsets is { Length: > 0 };
        _tvdScope.Output.WriteByte(hasOffsets ? (byte)1 : (byte)0);
        if (!hasOffsets) return;

        if (entry.StartOffsets!.Length != entry.Positions.Length || entry.EndOffsets!.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector offset count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.StartOffsets.Length; i++)
            _tvdScope.Output.WriteInt32(entry.StartOffsets[i]);
        for (int i = 0; i < entry.EndOffsets.Length; i++)
            _tvdScope.Output.WriteInt32(entry.EndOffsets[i]);
    }

    private void WritePayloads(TermVectorEntry entry)
    {
        bool hasPayloads = entry.Payloads is { Length: > 0 } payloads
            && payloads.Any(static payload => payload is { Length: > 0 });
        _tvdScope.Output.WriteByte(hasPayloads ? (byte)1 : (byte)0);

        if (!hasPayloads)
            return;

        if (entry.Payloads is null || entry.Payloads.Length != entry.Positions.Length)
            throw new InvalidDataException($"Term vector payload count for term '{entry.Term}' must match the position count.");

        for (int i = 0; i < entry.Payloads.Length; i++)
        {
            var payload = entry.Payloads[i];
            _tvdScope.Output.WriteInt32(payload?.Length ?? 0);
            if (payload is { Length: > 0 })
                _tvdScope.Output.WriteBytes(payload);
        }
    }
}
