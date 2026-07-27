using System.Buffers.Binary;
using System.Runtime.CompilerServices;

using Rowles.LeanCorpus.Codecs.Fst;

namespace Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

internal sealed class JapaneseDictionary : IDisposable
{
    private const int EntrySize = 4;

    private readonly JapaneseLanguageCodec _codec;
    private readonly FstReader _fst;
    private readonly int _knownSourceCount;
    private readonly int _knownEntryCount;
    private readonly int _unknownSourceCount;
    private readonly int _unknownEntryCount;
    private readonly int _forwardCount;
    private readonly int _backwardCount;

    internal JapaneseDictionary(string path)
    {
        _codec = JapaneseLanguageCodec.Open(path);
        try
        {
            _fst = FstReader.Open(_codec.GetSection(JapaneseCodecSection.Fst).ToArray());

            _knownEntryCount = ValidateEntries(JapaneseCodecSection.KnownEntries);
            _knownSourceCount = ValidateOffsets(
                JapaneseCodecSection.KnownTargetOffsets,
                _knownEntryCount);
            if (_fst.Count != _knownSourceCount)
                throw new InvalidDataException("Japanese FST and target map counts differ.");

            _unknownEntryCount = ValidateEntries(JapaneseCodecSection.UnknownEntries);
            _unknownSourceCount = ValidateOffsets(
                JapaneseCodecSection.UnknownTargetOffsets,
                _unknownEntryCount);
            if (_unknownSourceCount != CharacterDefinition.ClassCount)
                throw new InvalidDataException("Japanese unknown dictionary class count is invalid.");

            _ = new CharacterDefinition(_codec.GetSection(JapaneseCodecSection.CharacterDefinition));

            var costs = _codec.GetSection(JapaneseCodecSection.ConnectionCosts);
            if (costs.Length < 8)
                throw new InvalidDataException("Japanese connection-cost section is truncated.");

            _forwardCount = BinaryPrimitives.ReadInt32LittleEndian(costs);
            _backwardCount = BinaryPrimitives.ReadInt32LittleEndian(costs[4..]);
            if (_forwardCount <= 0 || _backwardCount <= 0)
                throw new InvalidDataException("Japanese connection-cost dimensions are invalid.");

            int expectedLength = checked(8 + (_forwardCount * _backwardCount * sizeof(short)));
            if (costs.Length != expectedLength)
                throw new InvalidDataException("Japanese connection-cost section has an invalid length.");

            ValidateContextIds(JapaneseCodecSection.KnownEntries, _knownEntryCount);
            ValidateContextIds(JapaneseCodecSection.UnknownEntries, _unknownEntryCount);
        }
        catch
        {
            _codec.Dispose();
            throw;
        }
    }

    internal FstReader.PrefixCursor CreateKnownWordCursor() => _fst.CreatePrefixCursor();

    internal CharacterDefinition CharacterDefinition
        => new(_codec.GetSection(JapaneseCodecSection.CharacterDefinition));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetKnownRange(int sourceId, out int start, out int count)
        => GetRange(
            JapaneseCodecSection.KnownTargetOffsets,
            sourceId,
            _knownSourceCount,
            out start,
            out count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetUnknownRange(int sourceId, out int start, out int count)
        => GetRange(
            JapaneseCodecSection.UnknownTargetOffsets,
            sourceId,
            _unknownSourceCount,
            out start,
            out count);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetKnownEntry(int index, out int contextId, out int wordCost)
        => GetEntry(
            JapaneseCodecSection.KnownEntries,
            index,
            _knownEntryCount,
            out contextId,
            out wordCost);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void GetUnknownEntry(int index, out int contextId, out int wordCost)
        => GetEntry(
            JapaneseCodecSection.UnknownEntries,
            index,
            _unknownEntryCount,
            out contextId,
            out wordCost);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int GetConnectionCost(int forwardId, int backwardId)
    {
        if ((uint)forwardId >= (uint)_forwardCount || (uint)backwardId >= (uint)_backwardCount)
            throw new InvalidDataException("Japanese dictionary context id is outside the connection matrix.");

        var section = _codec.GetSection(JapaneseCodecSection.ConnectionCosts);
        int offset = 8 + checked(((backwardId * _forwardCount) + forwardId) * sizeof(short));
        return BinaryPrimitives.ReadInt16LittleEndian(section[offset..]);
    }

    public void Dispose() => _codec.Dispose();

    private int ValidateEntries(JapaneseCodecSection sectionId)
    {
        int length = _codec.GetSection(sectionId).Length;
        if (length == 0 || length % EntrySize != 0)
            throw new InvalidDataException($"Japanese dictionary section {sectionId} has an invalid length.");
        return length / EntrySize;
    }

    private void ValidateContextIds(JapaneseCodecSection sectionId, int entryCount)
    {
        var entries = _codec.GetSection(sectionId);
        for (int i = 0; i < entryCount; i++)
        {
            int contextId = BinaryPrimitives.ReadUInt16LittleEndian(entries[(i * EntrySize)..]);
            if (contextId >= _forwardCount || contextId >= _backwardCount)
                throw new InvalidDataException(
                    $"Japanese dictionary section {sectionId} contains an invalid context id.");
        }
    }

    private int ValidateOffsets(JapaneseCodecSection sectionId, int entryCount)
    {
        var offsets = _codec.GetSection(sectionId);
        if (offsets.Length < sizeof(int) * 2 || offsets.Length % sizeof(int) != 0)
            throw new InvalidDataException($"Japanese dictionary section {sectionId} has an invalid length.");

        int count = offsets.Length / sizeof(int);
        int previous = BinaryPrimitives.ReadInt32LittleEndian(offsets);
        if (previous != 0)
            throw new InvalidDataException($"Japanese dictionary section {sectionId} must start at zero.");

        for (int i = 1; i < count; i++)
        {
            int current = BinaryPrimitives.ReadInt32LittleEndian(offsets[(i * sizeof(int))..]);
            if (current < previous || current > entryCount)
                throw new InvalidDataException($"Japanese dictionary section {sectionId} is not monotonic.");
            previous = current;
        }

        if (previous != entryCount)
            throw new InvalidDataException($"Japanese dictionary section {sectionId} does not cover every entry.");

        return count - 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GetRange(
        JapaneseCodecSection sectionId,
        int sourceId,
        int sourceCount,
        out int start,
        out int count)
    {
        if ((uint)sourceId >= (uint)sourceCount)
            throw new InvalidDataException($"Japanese dictionary source id {sourceId} is invalid.");

        var offsets = _codec.GetSection(sectionId);
        int byteOffset = sourceId * sizeof(int);
        start = BinaryPrimitives.ReadInt32LittleEndian(offsets[byteOffset..]);
        int end = BinaryPrimitives.ReadInt32LittleEndian(offsets[(byteOffset + sizeof(int))..]);
        count = end - start;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GetEntry(
        JapaneseCodecSection sectionId,
        int index,
        int entryCount,
        out int contextId,
        out int wordCost)
    {
        if ((uint)index >= (uint)entryCount)
            throw new InvalidDataException($"Japanese dictionary entry id {index} is invalid.");

        var entries = _codec.GetSection(sectionId);
        int offset = index * EntrySize;
        contextId = BinaryPrimitives.ReadUInt16LittleEndian(entries[offset..]);
        wordCost = BinaryPrimitives.ReadInt16LittleEndian(entries[(offset + sizeof(ushort))..]);
    }
}
