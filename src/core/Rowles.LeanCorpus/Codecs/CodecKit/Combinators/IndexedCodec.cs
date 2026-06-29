using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Combinators;

/// <summary>
/// Helper for formats that decode a header eagerly and then navigate the body
/// lazily via seeks on the underlying <see cref="IndexInput"/>.
///
/// Only the header bytes are materialised into a managed buffer; the body is
/// accessed directly through the returned IndexInput without any copy.
/// </summary>
internal sealed class IndexedCodec<THeader, TCursor>
{
    private readonly ICodec<THeader> _headerCodec;
    private readonly Func<THeader, IndexInput, TCursor> _cursorFactory;
    private readonly string _label;
    private const int MaxHeaderBytes = 4096;

    public IndexedCodec(
        ICodec<THeader> headerCodec,
        Func<THeader, IndexInput, TCursor> cursorFactory,
        string label = "Indexed")
    {
        _headerCodec = headerCodec;
        _cursorFactory = cursorFactory;
        _label = label;
    }

    public void EncodeHeader(THeader header, IBufferWriter<byte> writer, CodecContext context)
    {
        using var pathGuard = context.PushPath($"<{_label}>");
        _headerCodec.Encode(header, writer, context);
    }

    /// <summary>
    /// Decodes the header from the IndexInput, advancing position past the header.
    /// Returns a cursor backed by the same IndexInput for lazy body access.
    /// </summary>
    public (THeader Header, TCursor Cursor) Decode(IndexInput input, CodecContext context)
    {
        using var pathGuard = context.PushPath($"<{_label}>");

        // Stateless read: use a local cursor so IndexInput.Position is not advanced
        long readPos = input.Position;
        long remaining = input.Length - readPos;
        int chunkSize = (int)Math.Min(remaining, MaxHeaderBytes);
        var headerBytes = input.ReadSpan(chunkSize, ref readPos);

        // Decode header from the captured bytes
        var sequence = new ReadOnlySequence<byte>(headerBytes.ToArray());
        var reader = new SequenceReader<byte>(sequence);
        var header = _headerCodec.Decode(ref reader, context);
        long consumed = reader.Consumed;

        // Advance IndexInput to just past the header — body starts here
        input.Seek(input.Position + consumed);
        var cursor = _cursorFactory(header, input);
        return (header, cursor);
    }
}
