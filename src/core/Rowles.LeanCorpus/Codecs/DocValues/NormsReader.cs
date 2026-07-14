using System.Collections.Generic;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Reads quantised per-field norm values as compact byte arrays.
/// Uses memory-mapped I/O to avoid loading the entire file into a managed byte[].
/// </summary>
internal static class NormsReader
{
    public static NormsData Read(string filePath)
    {
        using var input = new IndexInput(filePath);

        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.Norms);

        return version switch
        {
            0 or 2 or 3 => ReadV2Body(input), // v0 is a test downgrade; v3 has same body format as v2
            1 => ReadV1Body(input),
            _ => throw new NotSupportedException($"Unsupported norms version: {version}")
        };
    }

    internal static List<(string Name, byte[] NormBytes, float[]? Boosts)> EnumerateFields(string filePath)
    {
        using var input = new IndexInput(filePath);

        byte version = CodecFileHeader.ReadVersionAndSkipHeader(input);

        int fieldCount;
        Func<IndexInput, int> readLen, readCount, readBoostCount, readDocId;
        switch (version)
        {
            case 1:
                fieldCount = input.ReadInt32();
                readLen = static i => i.ReadInt32();
                readCount = static i => i.ReadInt32();
                readBoostCount = static i => i.ReadInt32();
                readDocId = static i => i.ReadInt32();
                break;
            case 0:
            case 2:
                fieldCount = input.ReadVarInt();
                readLen = static i => i.ReadVarInt();
                readCount = static i => i.ReadVarInt();
                readBoostCount = static i => i.ReadVarInt();
                readDocId = static i => i.ReadVarInt();
                break;
            case 3:
                fieldCount = input.ReadVarInt();
                readLen = static i => i.ReadVarInt();
                readCount = static i => i.ReadVarInt();
                readBoostCount = static i => i.ReadVarInt();
                readDocId = static i => i.ReadVarInt();
                break;
            default:
                throw new NotSupportedException($"Unsupported norms version: {version}");
        }

        var results = new List<(string Name, byte[] NormBytes, float[]? Boosts)>(fieldCount);
        for (int f = 0; f < fieldCount; f++)
        {
            int fieldNameLen = readLen(input);

            byte[] nameBytes = input.ReadBytes(fieldNameLen);
            string fieldName = Encoding.UTF8.GetString(nameBytes, 0, fieldNameLen);

            int docCount = readCount(input);

            byte[] norms = input.ReadBytes(docCount);

            int boostCount = readBoostCount(input);
            if ((uint)boostCount > (uint)docCount)
                throw new InvalidDataException($"Invalid norms file: boost count {boostCount} exceeds document count {docCount} for field '{fieldName}'.");

            float[]? boosts = null;
            for (int i = 0; i < boostCount; i++)
            {
                int docId = readDocId(input);
                float boost = input.ReadSingle();

                if ((uint)docId >= (uint)docCount)
                    throw new InvalidDataException($"Invalid norms file: boost doc ID {docId} is outside field '{fieldName}' document count {docCount}.");

                boosts ??= CreateDefaultBoosts(docCount);
                boosts[docId] = boost;
            }

            results.Add((fieldName, norms, boosts));
        }

        return results;
    }

    private static NormsData ReadV1Body(IndexInput input)
    {
        int fieldCount = input.ReadInt32();
        return ReadFields(input, fieldCount, readLen: static i => i.ReadInt32(), readCount: static i => i.ReadInt32(), readBoostCount: static i => i.ReadInt32(), readDocId: static i => i.ReadInt32());
    }

    private static NormsData ReadV2Body(IndexInput input)
    {
        int fieldCount = input.ReadVarInt();
        return ReadFields(input, fieldCount, readLen: static i => i.ReadVarInt(), readCount: static i => i.ReadVarInt(), readBoostCount: static i => i.ReadVarInt(), readDocId: static i => i.ReadVarInt());
    }

    private static NormsData ReadFields(IndexInput input, int fieldCount,
        Func<IndexInput, int> readLen, Func<IndexInput, int> readCount,
        Func<IndexInput, int> readBoostCount, Func<IndexInput, int> readDocId)
    {
        var result = new NormsData();

        for (int f = 0; f < fieldCount; f++)
        {
            int fieldNameLen = readLen(input);

            byte[] nameBytes = input.ReadBytes(fieldNameLen);
            string fieldName = Encoding.UTF8.GetString(nameBytes, 0, fieldNameLen);

            int docCount = readCount(input);

            byte[] norms = input.ReadBytes(docCount);

            result.Norms[fieldName] = norms;

            int boostCount = readBoostCount(input);
            if ((uint)boostCount > (uint)docCount)
                throw new InvalidDataException($"Invalid norms file: boost count {boostCount} exceeds document count {docCount} for field '{fieldName}'.");

            float[]? boosts = null;
            for (int i = 0; i < boostCount; i++)
            {
                int docId = readDocId(input);
                float boost = input.ReadSingle();

                if ((uint)docId >= (uint)docCount)
                    throw new InvalidDataException($"Invalid norms file: boost doc ID {docId} is outside field '{fieldName}' document count {docCount}.");

                boosts ??= CreateDefaultBoosts(docCount);
                boosts[docId] = boost;
            }

            if (boosts is not null)
                result.Boosts[fieldName] = boosts;
        }

        return result;
    }

    private static float[] CreateDefaultBoosts(int docCount)
    {
        var boosts = new float[docCount];
        Array.Fill(boosts, 1.0f);
        return boosts;
    }
}
