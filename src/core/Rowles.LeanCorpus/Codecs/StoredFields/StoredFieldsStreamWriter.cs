using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Streaming variant of <see cref="StoredFieldsWriter"/> for the merge path.
/// Documents are added one at a time and flushed in 16-doc blocks so that
/// at most a single block sits in RAM rather than the whole merged segment.
/// </summary>
internal sealed class StoredFieldsStreamWriter : IDisposable
{
    private const int DefaultBlockSize = 16;

    private readonly string _fdtPath;
    private readonly string _fdxPath;
    private readonly int _blockSize;
    private readonly FieldCompressionPolicy _compression;
    private readonly ArrayBufferWriter<byte> _fdtBuf;
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
        _fdxPath = fdxPath;
        _blockSize = blockSize;
        _compression = compression;

        _fdtBuf = new ArrayBufferWriter<byte>(4096);
        _rawBuf = new ArrayBufferWriter<byte>(4096);
        _blockOffsets = new List<long>();
        _intraOffsets = new List<int>(blockSize);

        // Write body prefix: blockSize and compression byte (required by StoredFieldsReader)
        _fdtBuf.WriteInt32(blockSize);
        _fdtBuf.WriteByte((byte)compression);
    }

    internal void AddDocument(IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> fields)
    {
        _intraOffsets.Add((int)_rawBuf.WrittenCount);

        Span<byte> encodeBuf = stackalloc byte[512];

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
                    byte[] bytes = new byte[sizeof(long)];
                    System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(bytes, value.LongValue);
                    _rawBuf.WriteBytes(bytes);
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

        _blockOffsets.Add(_fdtBuf.WrittenCount);
        _fdtBuf.WriteInt32(_docsInBlock);
        _fdtBuf.WriteInt32(rawLength);
        _fdtBuf.WriteInt32(compLength);
        for (int i = 0; i < _docsInBlock; i++)
            _fdtBuf.WriteInt32(_intraOffsets[i]);
        _fdtBuf.WriteBytes(compData.AsSpan(0, compLength));

        _rawBuf.Clear();
        _intraOffsets.Clear();
        _docsInBlock = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        FlushBlock();

        long fdtBodyLength = _fdtBuf.WrittenCount;

        long headerSize;
        using (var fdtOutput = new IndexOutput(_fdtPath, durable: true))
        {
            CodecFileHeader.Write(fdtOutput, CodecFormats.StoredFields, _fdtBuf.WrittenSpan);
            headerSize = fdtOutput.Position - fdtBodyLength;
        }

        // Re-base body-relative block offsets to file-absolute positions.
        for (int i = 0; i < _blockOffsets.Count; i++)
            _blockOffsets[i] += headerSize;

        var fdxBodyBuf = new ArrayBufferWriter<byte>(1024);
        fdxBodyBuf.WriteInt32(_blockSize);
        fdxBodyBuf.WriteInt32(_docCount);
        fdxBodyBuf.WriteInt32(_blockOffsets.Count);
        foreach (var offset in _blockOffsets)
            fdxBodyBuf.WriteInt64(offset);

        using var fdxOutput = new IndexOutput(_fdxPath, durable: true);
        CodecFileHeader.Write(fdxOutput, CodecFormats.StoredFields, fdxBodyBuf.WrittenSpan);
    }
}
