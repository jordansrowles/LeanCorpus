using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.TermVectors;

internal static class TermVectorsCodecFiles
{
    internal static CodecFileDescriptor Data { get; } =
        CodecCatalog.Default.GetFile("leancorpus.term-vectors.data");

    internal static CodecFileDescriptor Index { get; } =
        CodecCatalog.Default.GetFile("leancorpus.term-vectors.index");

    internal static TermVectorsReadFrame OpenData(IndexInput input) => Open(input, Data);

    internal static TermVectorsReadFrame OpenIndex(IndexInput input) => Open(input, Index);

    private static TermVectorsReadFrame Open(IndexInput input, CodecFileDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(input);
        long frameStart = input.Position;
        if (input.Length - frameStart >= sizeof(int))
        {
            int magic = input.ReadInt32();
            input.Seek(frameStart);
            if (unchecked((uint)magic) == CodecFileWriter.Magic)
            {
                var canonical = CodecFileReader.Open(input, descriptor);
                input.Seek(canonical.Metadata.BodyStart);
                return new TermVectorsReadFrame(
                    canonical.Metadata.FormatVersion,
                    canonical.Metadata.BodyStart,
                    canonical.BodyEnd,
                    canonical);
            }
        }

        byte version = input.ReadByte();
        input.Seek(frameStart);
        if (version > descriptor.CurrentFormatVersion)
            throw new InvalidDataException(
                $"Unsupported term vectors format version {version}. This build supports up to version {descriptor.CurrentFormatVersion}.");
        if (TryOpenLegacyEnvelope(input, frameStart, out long bodyStart))
            return new TermVectorsReadFrame(version, bodyStart, input.Length, frameSession: null);

        var legacy = LegacyCodecFileReader.Open(input, descriptor);
        input.Seek(legacy.Metadata.BodyStart);
        return new TermVectorsReadFrame(
            legacy.Metadata.FormatVersion,
            legacy.Metadata.BodyStart,
            checked(legacy.Metadata.BodyStart + legacy.Metadata.BodyLength),
            legacy);
    }

    private static bool TryOpenLegacyEnvelope(IndexInput input, long frameStart, out long bodyStart)
    {
        input.Seek(frameStart + sizeof(byte));
        ulong encodedLength = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            byte value = input.ReadByte();
            encodedLength |= (ulong)(value & 0x7f) << shift;
            if ((value & 0x80) != 0)
                continue;

            bodyStart = input.Position;
            long remaining = input.Length - bodyStart;
            long zigZagLength = (long)(encodedLength >> 1);
            if ((encodedLength & 1) != 0)
                zigZagLength = ~zigZagLength;
            if (zigZagLength == remaining || encodedLength <= long.MaxValue && (long)encodedLength == remaining)
                return true;
            break;
        }

        input.Seek(frameStart);
        bodyStart = 0;
        return false;
    }
}

internal sealed class TermVectorsReadFrame : IDisposable
{
    private readonly IDisposable? _frameSession;

    internal TermVectorsReadFrame(int version, long bodyStart, long bodyEnd, IDisposable? frameSession)
    {
        Version = version;
        BodyStart = bodyStart;
        BodyEnd = bodyEnd;
        _frameSession = frameSession;
    }

    internal int Version { get; }

    internal long BodyStart { get; }

    internal long BodyEnd { get; }

    public void Dispose() => _frameSession?.Dispose();
}
