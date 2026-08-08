using System.Buffers.Binary;

#if !ROWLES_TEXT
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;
#endif

namespace Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

internal sealed class JapaneseLanguageCodec : IDisposable
{
    internal const int Version = 1;
    internal const int EntrySize = 32;

    private static ReadOnlySpan<byte> Magic => "JLC1"u8;

#if ROWLES_TEXT
    private readonly byte[] _input;
#else
    private readonly IndexInput _input;
#endif
    private readonly SectionDescriptor[] _sections;
    private bool _disposed;

#if ROWLES_TEXT
    private JapaneseLanguageCodec(byte[] input, SectionDescriptor[] sections)
#else
    private JapaneseLanguageCodec(IndexInput input, SectionDescriptor[] sections)
#endif
    {
        _input = input;
        _sections = sections;
    }

    internal static JapaneseLanguageCodec Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

#if ROWLES_TEXT
        var input = File.ReadAllBytes(path);
#else
        var input = new IndexInput(path);
#endif
        try
        {
            if (GetLength(input) < 12)
                throw new InvalidDataException("Japanese language codec is too small.");

            long position = 0;
            if (!ReadSpan(input, 4, ref position).SequenceEqual(Magic))
                throw new InvalidDataException("Invalid Japanese language codec magic.");

            int version = ReadInt32(input, ref position);
            if (version != Version)
                throw new InvalidDataException(
                    $"Unsupported Japanese language codec version {version}; expected {Version}.");

            int sectionCount = ReadInt32(input, ref position);
            if (sectionCount <= 0 || sectionCount > 32)
                throw new InvalidDataException("Japanese language codec section count is invalid.");

            long headerLength = checked(12L + ((long)sectionCount * EntrySize));
            if (headerLength > GetLength(input))
                throw new InvalidDataException("Japanese language codec table is truncated.");

            var sections = new SectionDescriptor[sectionCount];
            uint seen = 0;
            for (int i = 0; i < sectionCount; i++)
            {
                int rawId = ReadInt32(input, ref position);
                _ = ReadInt32(input, ref position);
                long offset = ReadInt64(input, ref position);
                long length = ReadInt64(input, ref position);

                if (rawId < 1 || rawId > 31)
                    throw new InvalidDataException($"Japanese language codec section id {rawId} is invalid.");

                uint bit = 1u << rawId;
                if ((seen & bit) != 0)
                    throw new InvalidDataException($"Japanese language codec section id {rawId} is duplicated.");
                seen |= bit;

                if (offset < headerLength || length < 0 || offset > GetLength(input) - length)
                    throw new InvalidDataException($"Japanese language codec section id {rawId} is outside the file.");

                uint checksum = unchecked((uint)ReadInt32(input, ref position));
                _ = ReadInt32(input, ref position);
                sections[i] = new SectionDescriptor((JapaneseCodecSection)rawId, offset, length, checksum);
            }

            Array.Sort(sections, static (left, right) => left.Offset.CompareTo(right.Offset));
            long previousEnd = headerLength;
            foreach (var section in sections)
            {
                if (section.Offset < previousEnd)
                    throw new InvalidDataException("Japanese language codec sections overlap.");
                previousEnd = section.Offset + section.Length;

                if (section.Length > int.MaxValue)
                    throw new InvalidDataException($"Japanese language codec section {section.Id} is too large.");

                long sectionPosition = section.Offset;
#if ROWLES_TEXT
                uint actual = JapaneseCrc32.Compute(ReadSpan(input, (int)section.Length, ref sectionPosition));
#else
                uint actual = Crc32.Compute(ReadSpan(input, (int)section.Length, ref sectionPosition));
#endif
                if (actual != section.Checksum)
                    throw new InvalidDataException($"Japanese language codec section {section.Id} failed its checksum.");
            }

            ValidateRequiredSections(sections);
            return new JapaneseLanguageCodec(input, sections);
        }
        catch
        {
#if !ROWLES_TEXT
            input.Dispose();
#endif
            throw;
        }
    }

    internal ReadOnlySpan<byte> GetSection(JapaneseCodecSection id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var section in _sections)
        {
            if (section.Id != id)
                continue;

#if ROWLES_TEXT
            return _input.AsSpan(checked((int)section.Offset), checked((int)section.Length));
#else
            long position = section.Offset;
            return ReadSpan(_input, (int)section.Length, ref position);
#endif
        }

        throw new InvalidDataException($"Japanese language codec section {id} is missing.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
#if !ROWLES_TEXT
        _input.Dispose();
#endif
    }

#if ROWLES_TEXT
    private static long GetLength(byte[] input) => input.LongLength;
    private static ReadOnlySpan<byte> ReadSpan(byte[] input, int count, scoped ref long position)
    {
        if (position < 0 || position > input.LongLength - count) throw new EndOfStreamException();
        var span = input.AsSpan((int)position, count); position += count; return span;
    }
    private static int ReadInt32(byte[] input, ref long position)
        => BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(input, sizeof(int), ref position));
    private static long ReadInt64(byte[] input, ref long position)
        => BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(input, sizeof(long), ref position));
#else
    private static long GetLength(IndexInput input) => input.Length;
    private static ReadOnlySpan<byte> ReadSpan(IndexInput input, int count, scoped ref long position) => input.ReadSpan(count, ref position);
    private static int ReadInt32(IndexInput input, ref long position)
        => BinaryPrimitives.ReadInt32LittleEndian(ReadSpan(input, sizeof(int), ref position));

    private static long ReadInt64(IndexInput input, ref long position)
        => BinaryPrimitives.ReadInt64LittleEndian(ReadSpan(input, sizeof(long), ref position));
#endif

    private static void ValidateRequiredSections(SectionDescriptor[] sections)
    {
        foreach (JapaneseCodecSection required in Enum.GetValues<JapaneseCodecSection>())
        {
            bool found = false;
            foreach (var section in sections)
            {
                if (section.Id == required)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                throw new InvalidDataException($"Japanese language codec section {required} is missing.");
        }
    }

    private readonly record struct SectionDescriptor(
        JapaneseCodecSection Id,
        long Offset,
        long Length,
        uint Checksum);
}

internal enum JapaneseCodecSection
{
    Fst = 1,
    KnownTargetOffsets = 2,
    KnownEntries = 3,
    UnknownTargetOffsets = 4,
    UnknownEntries = 5,
    CharacterDefinition = 6,
    ConnectionCosts = 7
}
