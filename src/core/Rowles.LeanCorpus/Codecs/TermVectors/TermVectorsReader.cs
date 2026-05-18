namespace Rowles.LeanCorpus.Codecs.TermVectors;

/// <summary>Reads per-document term vectors from .tvd/.tvx files using memory-mapped I/O.</summary>
internal sealed class TermVectorsReader : IDisposable
{
    private readonly Store.IndexInput _tvdInput;
    private readonly long[] _offsets;
    private readonly byte _version;

    private TermVectorsReader(Store.IndexInput tvdInput, long[] offsets, byte version)
    {
        _tvdInput = tvdInput;
        _offsets = offsets;
        _version = version;
    }

    public static TermVectorsReader Open(string tvdPath, string tvxPath)
    {
        // Read offsets from .tvx index file
        using var tvxInput = new Store.IndexInput(tvxPath);
        byte tvxVersion = CodecConstants.ReadHeaderVersion(tvxInput, CodecConstants.TermVectorsVersion, "term vectors index (.tvx)");

        int docCount = tvxInput.ReadInt32();
        var offsets = new long[docCount];
        for (int i = 0; i < docCount; i++)
            offsets[i] = tvxInput.ReadInt64();

        // Open .tvd data file as mmap
        var tvdInput = new Store.IndexInput(tvdPath);
        byte tvdVersion = CodecConstants.ReadHeaderVersion(tvdInput, CodecConstants.TermVectorsVersion, "term vectors data (.tvd)");

        if (tvdVersion != tvxVersion)
            throw new InvalidDataException($"Mismatched term vector versions between '{tvdPath}' and '{tvxPath}'.");

        return new TermVectorsReader(tvdInput, offsets, tvdVersion);
    }

    /// <summary>Returns all term vectors for a document across all stored fields.</summary>
    public Dictionary<string, List<TermVectorEntry>> GetTermVector(int docId)
    {
        if ((uint)docId >= (uint)_offsets.Length)
            return new();

        long position = _offsets[docId];
        int fieldCount = _tvdInput.ReadInt32(ref position);
        var result = new Dictionary<string, List<TermVectorEntry>>(fieldCount, StringComparer.Ordinal);

        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = _tvdInput.ReadLengthPrefixedString(ref position);
            int termCount = _tvdInput.ReadInt32(ref position);
            var entries = new List<TermVectorEntry>(termCount);
            for (int t = 0; t < termCount; t++)
            {
                string term = _tvdInput.ReadLengthPrefixedString(ref position);
                int freq = _tvdInput.ReadInt32(ref position);
                int posCount = _tvdInput.ReadInt32(ref position);
                var positions = new int[posCount];
                for (int p = 0; p < posCount; p++)
                    positions[p] = _tvdInput.ReadInt32(ref position);
                byte[]?[]? payloads = null;
                if (_version >= 2)
                {
                    bool hasPayloads = _tvdInput.ReadBoolean(ref position);
                    if (hasPayloads)
                    {
                        payloads = new byte[]?[posCount];
                        for (int p = 0; p < posCount; p++)
                        {
                            int payloadLength = _tvdInput.ReadInt32(ref position);
                            payloads[p] = payloadLength > 0
                                ? _tvdInput.ReadSpan(payloadLength, ref position).ToArray()
                                : null;
                        }
                    }
                }

                entries.Add(new TermVectorEntry(term, freq, positions, payloads));
            }
            result[fieldName] = entries;
        }

        return result;
    }

    /// <summary>Returns term vectors for a specific field in a document, or null if unavailable.</summary>
    public IReadOnlyList<TermVectorEntry>? GetTermVector(int docId, string field)
    {
        var all = GetTermVector(docId);
        return all.GetValueOrDefault(field);
    }

    public void Dispose() => _tvdInput.Dispose();
}
