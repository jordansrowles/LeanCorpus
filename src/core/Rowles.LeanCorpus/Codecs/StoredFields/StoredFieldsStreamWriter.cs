using System.Buffers;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Streaming variant of <see cref="StoredFieldsWriter"/> for the merge path.
/// Documents are added one at a time and flushed in byte-bounded blocks so that
/// at most one raw block sits in RAM rather than the whole merged segment.
/// </summary>
internal sealed class StoredFieldsStreamWriter : IDisposable
{
    private const int DefaultBlockSize = 16;

    private readonly IndexOutput _fdtOutput;
    private readonly CodecWriteSession _fdtScope;
    private readonly string _fdtPath;
    private readonly string _fdxPath;
    private readonly int _blockSize;
    private readonly FieldCompressionPolicy _compression;
    private readonly ArrayBufferWriter<byte> _rawBuf;
    private readonly List<long> _blockOffsets;
    private readonly List<int> _intraOffsets;

    private int _docsInBlock;
    private int _docCount;
    private bool _failed;
    private bool _disposed;

    internal StoredFieldsStreamWriter(string fdtPath, string fdxPath,
        int blockSize = DefaultBlockSize, FieldCompressionPolicy compression = FieldCompressionPolicy.Deflate)
    {
        StoredFieldsBlockPolicy.ValidateMaximumDocumentCount(blockSize);
        _fdtPath = fdtPath;
        _fdxPath = fdxPath;
        _blockSize = blockSize;
        _compression = compression;

        _rawBuf = new ArrayBufferWriter<byte>(4096);
        _blockOffsets = new List<long>();
        _intraOffsets = new List<int>(blockSize);

        _fdtOutput = new IndexOutput(fdtPath);
        _fdtScope = CodecFileWriter.Begin(_fdtOutput, StoredFieldsCodecFiles.Data);
        _fdtScope.Output.WriteInt32(blockSize);
        _fdtScope.Output.WriteByte((byte)compression);
    }

    internal void AddDocument(IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> fields)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            long documentRawLength = StoredFieldsBlockPolicy.GetDocumentRawLength(fields);
            StoredFieldsBlockPolicy.ValidateRawLength(documentRawLength);
            Span<byte> encodeBuf = stackalloc byte[512];

            if (documentRawLength > StoredFieldsBlockPolicy.TargetRawBytes)
            {
                FlushBlock();
                var oversizedBuffer = new ArrayBufferWriter<byte>(checked((int)documentRawLength));
                StoredFieldsBlockSerializer.WriteDocument(oversizedBuffer, fields, encodeBuf);
                StoredFieldsWriter.WriteBlock(
                    _fdtScope.Output, _blockOffsets, oversizedBuffer.WrittenSpan, [0], _compression);
            }
            else
            {
                if (StoredFieldsBlockPolicy.ShouldFlushBeforeAdd(
                        _docsInBlock, _rawBuf.WrittenCount, documentRawLength, _blockSize))
                    FlushBlock();

                _intraOffsets.Add(_rawBuf.WrittenCount);
                StoredFieldsBlockSerializer.WriteDocument(_rawBuf, fields, encodeBuf);
                _docsInBlock++;
                if (StoredFieldsBlockPolicy.ShouldFlushAfterAdd(_docsInBlock, _rawBuf.WrittenCount, _blockSize))
                    FlushBlock();
            }

            _docCount++;
        }
        catch
        {
            _failed = true;
            throw;
        }
    }

    private void FlushBlock()
    {
        if (_docsInBlock == 0) return;

        StoredFieldsWriter.WriteBlock(
            _fdtScope.Output, _blockOffsets, _rawBuf.WrittenSpan, _intraOffsets, _compression);

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
            if (!_failed)
            {
                FlushBlock();
                _fdtScope.Complete();
            }
        }
        catch
        {
            _failed = true;
            throw;
        }
        finally
        {
            try
            {
                _fdtScope.Dispose();
            }
            finally
            {
                _fdtOutput.Dispose();
                if (_failed)
                {
                    TryDeleteFile(_fdtPath);
                    TryDeleteFile(_fdxPath);
                }
            }
        }

        if (_failed)
        {
            TryDeleteFile(_fdtPath);
            TryDeleteFile(_fdxPath);
            return;
        }

        try
        {
            using var fdxOutput = new IndexOutput(_fdxPath);
            using var fdxScope = CodecFileWriter.Begin(fdxOutput, StoredFieldsCodecFiles.Index);
            fdxScope.Output.WriteInt32(_blockSize);
            fdxScope.Output.WriteInt32(_docCount);
            fdxScope.Output.WriteInt32(_blockOffsets.Count);
            foreach (var offset in _blockOffsets)
                fdxScope.Output.WriteInt64(offset);
            fdxScope.Complete();
        }
        catch
        {
            TryDeleteFile(_fdtPath);
            TryDeleteFile(_fdxPath);
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
