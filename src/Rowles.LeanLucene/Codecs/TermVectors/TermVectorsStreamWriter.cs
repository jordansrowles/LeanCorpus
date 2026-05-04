namespace Rowles.LeanLucene.Codecs.TermVectors;

/// <summary>
/// Streaming variant of <see cref="TermVectorsWriter"/> for the merge path.
/// Per-doc term vectors are appended directly to .tvd; .tvx is written on dispose.
/// Only the offsets list is buffered (8B per doc).
/// </summary>
internal sealed class TermVectorsStreamWriter : IDisposable
{
    private readonly string _tvxPath;
    private readonly FileStream _tvdStream;
    private readonly BinaryWriter _tvdWriter;
    private readonly List<long> _offsets;
    private bool _disposed;

    internal TermVectorsStreamWriter(string tvdPath, string tvxPath)
    {
        _tvxPath = tvxPath;
        _tvdStream = new FileStream(tvdPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _tvdWriter = new BinaryWriter(_tvdStream, System.Text.Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(_tvdWriter, CodecConstants.TermVectorsVersion);
        _offsets = new List<long>();
    }

    internal void AddDocument(IReadOnlyDictionary<string, List<TermVectorEntry>>? fields)
    {
        _offsets.Add(_tvdStream.Position);
        if (fields is null)
        {
            _tvdWriter.Write(0);
            return;
        }

        _tvdWriter.Write(fields.Count);
        foreach (var (fieldName, entries) in fields)
        {
            _tvdWriter.Write(fieldName);
            _tvdWriter.Write(entries.Count);
            foreach (var entry in entries)
            {
                _tvdWriter.Write(entry.Term);
                _tvdWriter.Write(entry.Freq);
                _tvdWriter.Write(entry.Positions.Length);
                foreach (var pos in entry.Positions)
                    _tvdWriter.Write(pos);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tvdWriter.Flush();
        _tvdWriter.Dispose();
        _tvdStream.Dispose();

        using var tvxFs = new FileStream(_tvxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var tvxWriter = new BinaryWriter(tvxFs, System.Text.Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(tvxWriter, CodecConstants.TermVectorsVersion);
        tvxWriter.Write(_offsets.Count);
        foreach (var off in _offsets)
            tvxWriter.Write(off);
    }
}
