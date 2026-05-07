using System.IO.MemoryMappedFiles;
using System.Text;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Reads quantised per-field norm values as compact byte arrays.
/// Uses memory-mapped I/O to avoid loading the entire file into a managed byte[].
/// </summary>
internal static class NormsReader
{
    public static Dictionary<string, byte[]> Read(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
            return new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using var mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var accessor = mmf.CreateViewAccessor(0, fileInfo.Length, MemoryMappedFileAccess.Read);

        long offset = 0;

        // Validate header: magic (4 bytes) + version (1 byte)
        int magic = accessor.ReadInt32(offset);
        offset += 4;
        if (magic != CodecConstants.Magic)
            throw new InvalidDataException(
                $"Invalid norms file: expected magic 0x{CodecConstants.Magic:X8}, got 0x{magic:X8}. " +
                "The file may be corrupted or from an incompatible version.");

        byte version = accessor.ReadByte(offset);
        offset += 1;
        if (version > CodecConstants.NormsVersion)
            throw new InvalidDataException(
                $"Unsupported norms format version {version}. " +
                $"This build supports up to version {CodecConstants.NormsVersion}. " +
                "Please upgrade LeanLucene.");

        int fieldCount = accessor.ReadInt32(offset);
        offset += 4;

        var result = new Dictionary<string, byte[]>(fieldCount, StringComparer.Ordinal);
        Span<byte> nameBuf = stackalloc byte[256];

        for (int f = 0; f < fieldCount; f++)
        {
            int fieldNameLen = accessor.ReadInt32(offset);
            offset += 4;

            byte[] nameBytes = fieldNameLen <= 256 ? nameBuf[..fieldNameLen].ToArray() : new byte[fieldNameLen];
            accessor.ReadArray(offset, nameBytes, 0, fieldNameLen);
            string fieldName = Encoding.UTF8.GetString(nameBytes, 0, fieldNameLen);
            offset += fieldNameLen;

            int docCount = accessor.ReadInt32(offset);
            offset += 4;

            var norms = new byte[docCount];
            accessor.ReadArray(offset, norms, 0, docCount);
            offset += docCount;

            result[fieldName] = norms;
        }

        return result;
    }
}
