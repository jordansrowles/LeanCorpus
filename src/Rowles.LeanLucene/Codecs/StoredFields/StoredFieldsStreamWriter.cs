namespace Rowles.LeanLucene.Codecs.StoredFields;

/// <summary>
/// Streaming variant of <see cref="StoredFieldsWriter"/> for the merge path.
/// Documents are added one at a time and flushed in 16-doc blocks so that
/// at most a single block sits in RAM rather than the whole merged segment.
/// </summary>
internal sealed class StoredFieldsStreamWriter : IDisposable
{
    private const int DefaultBlockSize = 16;

    private readonly string _fdxPath;
    private readonly int _blockSize;
    private readonly FieldCompressionPolicy _compression;
    private readonly FileStream _fdtStream;
    private readonly BinaryWriter _fdtWriter;
    private readonly MemoryStream _rawStream;
    private readonly BinaryWriter _rawWriter;
    private readonly List<long> _blockOffsets;
    private readonly List<int> _intraOffsets;

    private int _docsInBlock;
    private int _docCount;
    private bool _disposed;

    internal StoredFieldsStreamWriter(string fdtPath, string fdxPath,
        int blockSize = DefaultBlockSize, FieldCompressionPolicy compression = FieldCompressionPolicy.Lz4)
    {
        _fdxPath = fdxPath;
        _blockSize = blockSize;
        _compression = compression;

        _fdtStream = new FileStream(fdtPath, FileMode.Create, FileAccess.Write, FileShare.None);
        _fdtWriter = new BinaryWriter(_fdtStream, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(_fdtWriter, CodecConstants.StoredFieldsVersion);
        _fdtWriter.Write(blockSize);
        _fdtWriter.Write((byte)compression);

        _rawStream = new MemoryStream(4096);
        _rawWriter = new BinaryWriter(_rawStream, System.Text.Encoding.UTF8, leaveOpen: true);
        _blockOffsets = new List<long>();
        _intraOffsets = new List<int>(blockSize);
    }

    internal void AddDocument(IReadOnlyDictionary<string, IReadOnlyList<string>> fields)
    {
        _intraOffsets.Add((int)_rawStream.Position);

        Span<byte> encodeBuf = stackalloc byte[512];

        _rawWriter.Write(fields.Count);
        foreach (var (name, values) in fields)
        {
            int nameByteCount = System.Text.Encoding.UTF8.GetByteCount(name);
            Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
            System.Text.Encoding.UTF8.GetBytes(name, nameBuf);
            _rawWriter.Write(nameByteCount);
            _rawWriter.Write(nameBuf[..nameByteCount]);

            _rawWriter.Write(values.Count);
            foreach (var value in values)
            {
                int valueByteCount = System.Text.Encoding.UTF8.GetByteCount(value);
                Span<byte> valueBuf = valueByteCount <= encodeBuf.Length ? encodeBuf : new byte[valueByteCount];
                System.Text.Encoding.UTF8.GetBytes(value, valueBuf);
                _rawWriter.Write(valueByteCount);
                _rawWriter.Write(valueBuf[..valueByteCount]);
            }
        }

        _docsInBlock++;
        _docCount++;

        if (_docsInBlock >= _blockSize)
            FlushBlock();
    }

    private void FlushBlock()
    {
        if (_docsInBlock == 0) return;

        _rawWriter.Flush();
        int rawLength = (int)_rawStream.Length;
        var rawData = _rawStream.GetBuffer().AsSpan(0, rawLength);

        var (compData, compLength) = StoredFieldCompression.Compress(rawData, _compression);

        _blockOffsets.Add(_fdtStream.Position);
        _fdtWriter.Write(_docsInBlock);
        _fdtWriter.Write(rawLength);
        _fdtWriter.Write(compLength);
        for (int i = 0; i < _docsInBlock; i++)
            _fdtWriter.Write(_intraOffsets[i]);
        _fdtWriter.Write(compData.AsSpan(0, compLength));

        _rawStream.SetLength(0);
        _rawStream.Position = 0;
        _intraOffsets.Clear();
        _docsInBlock = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        FlushBlock();

        _fdtWriter.Flush();
        _rawWriter.Dispose();
        _rawStream.Dispose();
        _fdtWriter.Dispose();
        _fdtStream.Dispose();

        using var fdxStream = new FileStream(_fdxPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var fdxWriter = new BinaryWriter(fdxStream, System.Text.Encoding.UTF8, leaveOpen: false);

        CodecConstants.WriteHeader(fdxWriter, CodecConstants.StoredFieldsVersion);
        fdxWriter.Write(_blockSize);
        fdxWriter.Write(_docCount);
        fdxWriter.Write(_blockOffsets.Count);
        foreach (var offset in _blockOffsets)
            fdxWriter.Write(offset);
    }
}
