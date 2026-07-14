using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// A typed, bidirectional codec that encodes values of type <typeparamref name="T"/> to bytes
/// and decodes bytes back to values of type <typeparamref name="T"/>.
/// Implementations must be immutable and thread-safe. All mutable state lives in <see cref="CodecContext"/>.
/// </summary>
public interface ICodec<T>
{
    /// <summary>
    /// Encodes <paramref name="value"/> into the <paramref name="writer"/>.
    /// </summary>
    void Encode(T value, IBufferWriter<byte> writer, CodecContext context);

    /// <summary>
    /// Decodes a value of type <typeparamref name="T"/> from the <paramref name="reader"/>.
    /// On failure, the reader position is restored to its state before this call (atomicity).
    /// </summary>
    T Decode(ref SequenceReader<byte> reader, CodecContext context);
}
