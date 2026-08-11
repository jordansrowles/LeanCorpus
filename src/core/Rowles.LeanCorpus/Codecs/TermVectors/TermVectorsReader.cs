using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Codecs.TermVectors;

/// <summary>Reads per-document term vectors from .tvd/.tvx files using memory-mapped I/O.</summary>
internal sealed class TermVectorsReader : IDisposable
{
    private readonly Store.IndexInput _tvdInput;
    private readonly long[] _offsets;
    private readonly int _version;
    private readonly long _bodyEnd;
    private readonly IDisposable _tvdFrame;

    private TermVectorsReader(
        Store.IndexInput tvdInput,
        long[] offsets,
        int version,
        long bodyEnd,
        IDisposable tvdFrame)
    {
        _tvdInput = tvdInput;
        _offsets = offsets;
        _version = version;
        _bodyEnd = bodyEnd;
        _tvdFrame = tvdFrame;
    }

    public static TermVectorsReader Open(string tvdPath, string tvxPath)
    {
        var tvdInput = new Store.IndexInput(tvdPath);
        try
        {
            return Open(tvdInput, new Store.IndexInput(tvxPath));
        }
        catch
        {
            tvdInput.Dispose();
            throw;
        }
    }

    internal static TermVectorsReader Open(Store.IndexInput tvdInput, Store.IndexInput tvxInput)
    {
        TermVectorsReadFrame? tvdFrame = null;
        try
        {
            int tvxVersion;
            long[] offsets;
            using (tvxInput)
            using (var tvxFrame = TermVectorsCodecFiles.OpenIndex(tvxInput))
            {
                tvxVersion = tvxFrame.Version;
                int docCount = tvxInput.ReadInt32();
                if (docCount < 0)
                    throw new InvalidDataException($"Term vectors index declares a negative document count {docCount}.");
                long offsetsEnd = checked(tvxInput.Position + (long)docCount * sizeof(long));
                if (offsetsEnd != tvxFrame.BodyEnd)
                    throw new InvalidDataException("Term vectors index length does not match its declared document count.");

                offsets = new long[docCount];
                for (int i = 0; i < docCount; i++)
                    offsets[i] = tvxInput.ReadInt64();
            }

            tvdFrame = TermVectorsCodecFiles.OpenData(tvdInput);
            if (tvdFrame.Version != tvxVersion)
                throw new InvalidDataException("Mismatched term vectors versions between '.tvd' and '.tvx'.");

            long previousOffset = -1;
            foreach (long offset in offsets)
            {
                if (offset < tvdFrame.BodyStart || offset >= tvdFrame.BodyEnd)
                    throw new InvalidDataException($"Term vector document offset {offset} is outside the data body.");
                if (offset <= previousOffset)
                    throw new InvalidDataException("Term vector document offsets must be strictly increasing.");
                previousOffset = offset;
            }

            var result = new TermVectorsReader(tvdInput, offsets, tvdFrame.Version, tvdFrame.BodyEnd, tvdFrame);
            tvdFrame = null;
            return result;
        }
        catch
        {
            tvdFrame?.Dispose();
            tvdInput.Dispose();
            tvxInput.Dispose();
            throw;
        }
    }

    /// <summary>Returns all term vectors for a document across all stored fields.</summary>
    public Dictionary<string, List<TermVectorEntry>> GetTermVector(int docId)
    {
        if ((uint)docId >= (uint)_offsets.Length)
            return new();

        long position = _offsets[docId];
        int fieldCount = _tvdInput.ReadInt32(ref position);
        if (fieldCount < 0 || fieldCount > (_bodyEnd - position) / 5)
            throw new InvalidDataException($"Term vector field count {fieldCount} is invalid for the remaining data body.");
        var result = new Dictionary<string, List<TermVectorEntry>>(fieldCount, StringComparer.Ordinal);

        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = _tvdInput.ReadLengthPrefixedString(ref position);
            int termCount = _tvdInput.ReadInt32(ref position);
            if (termCount < 0 || termCount > (_bodyEnd - position) / 11)
                throw new InvalidDataException($"Term vector term count {termCount} is invalid for the remaining data body.");
            var entries = new List<TermVectorEntry>(termCount);
            for (int t = 0; t < termCount; t++)
            {
                string term = _tvdInput.ReadLengthPrefixedString(ref position);
                int freq = _tvdInput.ReadInt32(ref position);
                int posCount = _tvdInput.ReadInt32(ref position);
                if (posCount < 0 || posCount > (_bodyEnd - position) / sizeof(int))
                    throw new InvalidDataException($"Term vector position count {posCount} is invalid for the remaining data body.");
                var positions = new int[posCount];
                for (int p = 0; p < posCount; p++)
                    positions[p] = _tvdInput.ReadInt32(ref position);
                bool hasPayloads = _tvdInput.ReadBoolean(ref position);
                byte[]?[]? payloads = null;
                if (hasPayloads)
                {
                    payloads = new byte[]?[posCount];
                    for (int p = 0; p < posCount; p++)
                    {
                        int payloadLength = _tvdInput.ReadInt32(ref position);
                        if (payloadLength < 0 || payloadLength > _bodyEnd - position)
                            throw new InvalidDataException($"Term vector payload length {payloadLength} is invalid for the remaining data body.");
                        payloads[p] = payloadLength > 0
                            ? _tvdInput.ReadSpan(payloadLength, ref position).ToArray()
                            : null;
                    }
                }
                if (_version >= 2)
                {
                    bool hasOffsets = _tvdInput.ReadBoolean(ref position);
                    int[]? startOffsets = null;
                    int[]? endOffsets = null;
                    if (hasOffsets)
                    {
                        startOffsets = new int[posCount];
                        for (int p = 0; p < posCount; p++)
                            startOffsets[p] = _tvdInput.ReadInt32(ref position);
                        endOffsets = new int[posCount];
                        for (int p = 0; p < posCount; p++)
                            endOffsets[p] = _tvdInput.ReadInt32(ref position);
                    }

                    entries.Add(new TermVectorEntry(term, freq, positions, payloads, startOffsets, endOffsets));
                }
                else
                {
                    entries.Add(new TermVectorEntry(term, freq, positions, payloads));
                }
            }
            result[fieldName] = entries;
        }

        if (position > _bodyEnd)
            throw new InvalidDataException("Term vector document extends beyond the data body.");

        return result;
    }

    /// <summary>Returns term vectors for a specific field in a document, or null if unavailable.</summary>
    public IReadOnlyList<TermVectorEntry>? GetTermVector(int docId, string field)
    {
        var all = GetTermVector(docId);
        return all.GetValueOrDefault(field);
    }

    internal int DocCount => _offsets.Length;

    public void Dispose()
    {
        _tvdFrame.Dispose();
        _tvdInput.Dispose();
    }
}
