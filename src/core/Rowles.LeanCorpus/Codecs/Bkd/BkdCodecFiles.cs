using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Bkd;

internal static class BkdCodecFiles
{
    internal static CodecFileDescriptor Double { get; } =
        CodecCatalog.Default.GetFile("leancorpus.numeric-structures.bkd");

    internal static CodecFileDescriptor Int64 { get; } =
        CodecCatalog.Default.GetFile("leancorpus.numeric-structures.int64-bkd");

    internal static BkdReadFrame Open(IndexInput input, CodecFileDescriptor descriptor)
    {
        long start = input.Position;
        if (input.Length - start >= sizeof(int))
        {
            int magic = input.ReadInt32();
            input.Seek(start);
            if (unchecked((uint)magic) == CodecFileWriter.Magic)
            {
                var current = CodecFileReader.Open(input, descriptor);
                input.Seek(current.Metadata.BodyStart);
                return new BkdReadFrame(current.Metadata.BodyStart, current.BodyEnd, current);
            }
        }

        var legacy = LegacyCodecFileReader.Open(input, descriptor);
        input.Seek(legacy.Metadata.BodyStart);
        return new BkdReadFrame(
            legacy.Metadata.BodyStart,
            checked(legacy.Metadata.BodyStart + legacy.Metadata.BodyLength),
            legacy);
    }
}

internal sealed class BkdReadFrame(long bodyStart, long bodyEnd, IDisposable session) : IDisposable
{
    internal long BodyStart { get; } = bodyStart;
    internal long BodyEnd { get; } = bodyEnd;

    public void Dispose() => session.Dispose();
}
