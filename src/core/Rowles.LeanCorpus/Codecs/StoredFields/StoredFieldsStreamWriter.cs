using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Streaming variant of <see cref="StoredFieldsWriter"/> for the merge path.
/// Documents are added one at a time and flushed in blocks so that at most one
/// raw block sits in RAM rather than the whole merged segment.
/// </summary>
internal sealed class StoredFieldsStreamWriter : IDisposable
{
    private const int DefaultBlockSize = 16;

    private readonly IndexOutput _fdtOutput;
    private readonly CodecFileHeader.StreamingWriteScope _fdtScope;
    private readonly string _fdtPath;
    private readonly string _fdxPath;
    private readonly int _blockSize;
    private readonly FieldCompressionPolicy _compression;
    private readonly ArrayBufferWriter<byte> _rawBuf;
    private readonly List<long> _blockOffsets;
    private readonly List<int> _intraOffsets;

    private int _docsInBlock;
    private int _docCount;
    private bool _disposed;

    internal StoredFieldsStreamWriter(string fdtPath, string fdxPath,
        int blockSize = DefaultBlockSize, FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate)
    {
        _fdtPath = fdtPath;
        _fdtOutput = new IndexOutput(fdtPath);
        _fdxPath = fdxPath;
        _blockSize = blockSize;
        _compression = compression;

        _rawBuf = new ArrayBufferWriter<byte>(4096);
        _blockOffsets = new List<long>();
        _intraOffsets = new List<int>(blockSize);

        _fdtScope = CodecFileHeader.BeginStreamingWrite(_fdtOutput, CodecConstants.StoredFieldsVersion);
        _fdtScope.Output.WriteInt32(blockSize);
        _fdtScope.Output.WriteByte((byte)compression);
    }

    internal void AddDocument(IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> fields)
    {
        _intraOffsets.Add((int)_rawBuf.WrittenCount);

        Span<byte> encodeBuf = stackalloc byte[512];
        Span<byte> longBytes = stackalloc byte[sizeof(long)];

        _rawBuf.WriteInt32(fields.Count);
        foreach (var (name, values) in fields)
        {
            int nameByteCount = Encoding.UTF8.GetByteCount(name);
            Span<byte> nameBuf = nameByteCount <= encodeBuf.Length ? encodeBuf : new byte[nameByteCount];
            Encoding.UTF8.GetBytes(name, nameBuf);
            _rawBuf.WriteInt32(nameByteCount);
            _rawBuf.WriteBytes(nameBuf[..nameByteCount]);

            _rawBuf.WriteInt32(values.Count);
            foreach (var value in values)
            {
                _rawBuf.WriteByte((byte)value.Kind);
                if (value.IsBinary)
                {
                    var bytes = value.BinaryValue ?? [];
                    _rawBuf.WriteInt32(bytes.Length);
                    _rawBuf.WriteBytes(bytes);
                }
                else if (value.IsLong)
                {
                    _rawBuf.WriteInt32(sizeof(long));
                    System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(longBytes, value.LongValue);
                    _rawBuf.WriteBytes(longBytes);
                }
                else
                {
                    var text = value.StringValue ?? string.Empty;
                    int valueByteCount = Encoding.UTF8.GetByteCount(text);
                    Span<byte> valueBuf = valueByteCount <= encodeBuf.Length ? encodeBuf : new byte[valueByteCount];
                    Encoding.UTF8.GetBytes(text, valueBuf);
                    _rawBuf.WriteInt32(valueByteCount);
                    _rawBuf.WriteBytes(valueBuf[..valueByteCount]);
                }
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

        int rawLength = (int)_rawBuf.WrittenCount;
        var rawData = _rawBuf.WrittenSpan;

        var (compData, compLength) = StoredFieldCompression.Compress(rawData, _compression);

        _blockOffsets.Add(_fdtOutput.Position);
        _fdtOutput.WriteInt32(_docsInBlock);
        _fdtOutput.WriteInt32(rawLength);
        _fdtOutput.WriteInt32(compLength);
        for (int i = 0; i < _docsInBlock; i++)
            _fdtOutput.WriteInt32(_intraOffsets[i]);
        _fdtOutput.WriteBytes(compData.AsSpan(0, compLength));

        _rawBuf.Clear();
        _intraOffsets.Clear();
        _docsInBlock = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            FlushBlock();
            _fdtScope.Dispose();
        }
        finally
        {
            _fdtOutput.Dispose();
        }

        try
        {
            using var fdxOutput = new IndexOutput(_fdxPath);
            StoredFieldsFileHeader.WriteV3FdxHeader(fdxOutput, _blockSize, _docCount, _blockOffsets.Count);
            foreach (var offset in _blockOffsets)
                fdxOutput.WriteInt64(offset);
        }
        catch
        {
            TryDeleteFile(_fdtPath);
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { FileOpenRetry.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
