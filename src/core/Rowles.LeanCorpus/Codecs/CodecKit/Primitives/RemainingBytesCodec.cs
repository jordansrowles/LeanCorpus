using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

/// <summary>
/// Reads all remaining bytes from the reader into a <see cref="byte"/> array.
/// Use for opaque blob sections like FST node data where the format header
/// is decoded by CodecKit but the body is navigated with raw byte access.
/// </summary>
internal sealed class RemainingBytesCodec : ICodec<byte[]>
{
    public static readonly RemainingBytesCodec Instance = new();

    public void Encode(byte[] value, IBufferWriter<byte> writer, CodecContext context)
    {
        writer.Write(value);
    }

    public byte[] Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        if (reader.Remaining == 0)
            return [];

        var result = new byte[reader.Remaining];
        reader.TryCopyTo(result);
        reader.Advance(reader.Remaining);
        return result;
    }
}
