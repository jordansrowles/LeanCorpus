using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Bkd;

internal static class NumericIndexCodec
{
    internal static CodecFileDescriptor DoubleDescriptor { get; } =
        CodecCatalog.Default.GetFile("leancorpus.numeric-structures.numeric-index");

    internal static CodecFileDescriptor Int64Descriptor { get; } =
        CodecCatalog.Default.GetFile("leancorpus.numeric-structures.int64-numeric-index");

    internal static void WriteDouble(string path, IReadOnlyDictionary<string, Dictionary<int, double>> fields)
    {
        using var output = new IndexOutput(path);
        using var frame = CodecFileWriter.Begin(output, DoubleDescriptor);
        frame.Output.WriteInt32(fields.Count);
        foreach (var (field, values) in fields)
        {
            frame.Output.WriteString(field);
            frame.Output.WriteInt32(values.Count);
            foreach (var (docId, value) in values)
            {
                frame.Output.WriteInt32(docId);
                frame.Output.WriteInt64(BitConverter.DoubleToInt64Bits(value));
            }
        }
        frame.Complete();
    }

    internal static void WriteInt64(string path, IReadOnlyDictionary<string, Dictionary<int, long>> fields)
    {
        using var output = new IndexOutput(path);
        using var frame = CodecFileWriter.Begin(output, Int64Descriptor);
        frame.Output.WriteInt32(fields.Count);
        foreach (var (field, values) in fields)
        {
            frame.Output.WriteString(field);
            frame.Output.WriteInt32(values.Count);
            foreach (var (docId, value) in values)
            {
                frame.Output.WriteInt32(docId);
                frame.Output.WriteInt64(value);
            }
        }
        frame.Complete();
    }

    internal static Dictionary<string, Dictionary<int, double>> ReadDouble(IndexInput input)
    {
        using var inputLifetime = input;
        using var frame = Open(input, DoubleDescriptor);
        int fieldCount = ReadCount(input, frame.BodyEnd, "numeric field");
        var result = new Dictionary<string, Dictionary<int, double>>(fieldCount, StringComparer.Ordinal);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string field = input.ReadLengthPrefixedString();
            int entryCount = ReadCount(input, frame.BodyEnd, "numeric entry", 12);
            var values = new Dictionary<int, double>(entryCount);
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
                values[input.ReadInt32()] = input.ReadDouble();
            result[field] = values;
        }
        RequireBodyEnd(input, frame.BodyEnd, "numeric index");
        return result;
    }

    internal static Dictionary<string, Dictionary<int, long>> ReadInt64(IndexInput input)
    {
        using var inputLifetime = input;
        using var frame = Open(input, Int64Descriptor);
        int fieldCount = ReadCount(input, frame.BodyEnd, "Int64 numeric field");
        var result = new Dictionary<string, Dictionary<int, long>>(fieldCount, StringComparer.Ordinal);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string field = input.ReadLengthPrefixedString();
            int entryCount = ReadCount(input, frame.BodyEnd, "Int64 numeric entry", 12);
            var values = new Dictionary<int, long>(entryCount);
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
                values[input.ReadInt32()] = input.ReadInt64();
            result[field] = values;
        }
        RequireBodyEnd(input, frame.BodyEnd, "Int64 numeric index");
        return result;
    }

    private static SidecarReadFrame Open(IndexInput input, CodecFileDescriptor descriptor)
    {
        long start = input.Position;
        if (input.Length - start >= sizeof(int))
        {
            int magic = input.ReadInt32();
            input.Seek(start);
            if (unchecked((uint)magic) == CodecFileWriter.Magic)
            {
                var canonical = CodecFileReader.Open(input, descriptor);
                canonical.ValidateChecksum();
                input.Seek(canonical.Metadata.BodyStart);
                return new SidecarReadFrame(canonical.BodyEnd, canonical);
            }
        }

        input.Seek(start);
        return new SidecarReadFrame(input.Length, session: null);
    }

    private static int ReadCount(IndexInput input, long bodyEnd, string description, int minimumBytes = 1)
    {
        int count = input.ReadInt32();
        if (count < 0 || count > (bodyEnd - input.Position) / minimumBytes)
            throw new InvalidDataException($"The {description} count {count} is invalid for the remaining body.");
        return count;
    }

    private static void RequireBodyEnd(IndexInput input, long bodyEnd, string description)
    {
        if (input.Position != bodyEnd)
            throw new InvalidDataException($"The {description} contains trailing or unparsed bytes.");
    }

    private sealed class SidecarReadFrame(long bodyEnd, IDisposable? session) : IDisposable
    {
        internal long BodyEnd { get; } = bodyEnd;
        public void Dispose() => session?.Dispose();
    }
}
