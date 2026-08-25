using System.Buffers;
using System.Text;

namespace Rowles.LeanCorpus.Store;

/// <summary>Writes and opens memory-mapped compound files used by immutable segments.</summary>
internal static class CompoundFileWriter
{
    internal const int Magic = 0x5346434C;
    internal const int Version = 1;
    internal const int MaxEntries = 4096;

    internal static bool Pack(string directoryPath, string segmentId)
    {
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pattern in new[] { segmentId + ".*", segmentId + "_v_*.*" })
        {
            foreach (var path in FileOpenRetry.GetFiles(directoryPath, pattern))
                sourceNames.Add(Path.GetFileName(path));
        }

        var sourceFiles = sourceNames
            .Where(name => !name.Equals(segmentId + ".seg", StringComparison.Ordinal)
                && !name.Equals(segmentId + ".cfs", StringComparison.Ordinal)
                && !name.EndsWith(".cfs.tmp", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".stats.json", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".del", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        if (sourceFiles.Length == 0)
            return false;
        if (sourceFiles.Length > MaxEntries)
            throw new InvalidDataException($"Segment '{segmentId}' has too many files for a compound file.");

        var cfsName = segmentId + ".cfs";
        var cfsPath = Path.Combine(directoryPath, cfsName);
        var temporaryPath = cfsPath + ".tmp";
        var entries = new Entry[sourceFiles.Length];
        // Keep Windows compound writes incremental rather than eagerly extending every output.
        // Retain reservation on POSIX, where it avoids fragmented merge output.
        long expectedLength = OperatingSystem.IsWindows()
            ? 0
            : GetExpectedLength(directoryPath, sourceFiles);
        try
        {
            long directoryOffset;
            using (var output = new IndexOutput(temporaryPath, preallocationSize: expectedLength))
            {
                output.WriteInt32(Magic);
                output.WriteInt32(Version);
                output.WriteInt32(sourceFiles.Length);
                directoryOffset = output.Position;

                foreach (var name in sourceFiles)
                {
                    output.WriteString(name);
                    output.WriteInt64(0);
                    output.WriteInt64(0);
                }

                byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    for (int i = 0; i < sourceFiles.Length; i++)
                    {
                        string sourcePath = Path.Combine(directoryPath, sourceFiles[i]);
                        entries[i] = new Entry(sourceFiles[i], output.Position, 0);
                        long copied = 0;
                        using var source = FileOpenRetry.OpenReadDelete(sourcePath);
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.WriteBytes(buffer.AsSpan(0, read));
                            copied += read;
                        }
                        entries[i] = entries[i] with { Length = copied };
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                output.Seek(directoryOffset);
                foreach (var entry in entries)
                {
                    output.WriteString(entry.Name);
                    output.WriteInt64(entry.Offset);
                    output.WriteInt64(entry.Length);
                }
            }

            FileOpenRetry.Move(temporaryPath, cfsPath, overwrite: true);
            foreach (var name in sourceFiles)
                FileOpenRetry.Delete(Path.Combine(directoryPath, name));
            return true;
        }
        catch
        {
            try { FileOpenRetry.Delete(temporaryPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "compound temporary file cleanup"); }
            throw;
        }
    }

    private static long GetExpectedLength(string directoryPath, IReadOnlyList<string> sourceFiles)
    {
        long length = 3L * sizeof(int);
        foreach (var name in sourceFiles)
        {
            int nameByteCount = Encoding.UTF8.GetByteCount(name);
            length = checked(length + Get7BitEncodedLength(nameByteCount) + nameByteCount + 2L * sizeof(long));
            length = checked(length + FileOpenRetry.GetFileLength(Path.Combine(directoryPath, name)));
        }
        return length;
    }

    private static int Get7BitEncodedLength(int value)
    {
        int length = 1;
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            remaining >>= 7;
            length++;
        }
        return length;
    }

    private readonly record struct Entry(string Name, long Offset, long Length);
}

/// <summary>Directory metadata for a compound file without materialising member bytes.</summary>
internal sealed class CompoundFileReader : IDisposable
{
    private readonly string _fileName;
    private readonly Dictionary<string, Entry> _entries;
    private readonly IReadOnlyList<string> _fileNames;
    private readonly IndexInput _input;

    private CompoundFileReader(string fileName, Dictionary<string, Entry> entries, IndexInput input)
    {
        _fileName = fileName;
        _entries = entries;
        _input = input;
        _fileNames = Array.AsReadOnly(entries.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    internal static CompoundFileReader Open(MMapDirectory directory, string fileName)
    {
        var input = directory.OpenInput(fileName);
        try
        {
            long fileLength = input.Length;
            if (input.ReadInt32() != CompoundFileWriter.Magic)
                throw new InvalidDataException($"Compound file '{fileName}' has an invalid magic.");
            if (input.ReadInt32() != CompoundFileWriter.Version)
                throw new InvalidDataException($"Compound file '{fileName}' has an unsupported version.");

            int entryCount = input.ReadInt32();
            if (entryCount < 1 || entryCount > CompoundFileWriter.MaxEntries)
                throw new InvalidDataException($"Compound file '{fileName}' has an invalid entry count {entryCount}.");

            var entries = new Dictionary<string, Entry>(entryCount, StringComparer.Ordinal);
            for (int i = 0; i < entryCount; i++)
            {
                string name = ReadBoundedString(input, fileName);
                long offset = input.ReadInt64();
                long length = input.ReadInt64();
                if (!entries.TryAdd(name, new Entry(offset, length)))
                    throw new InvalidDataException($"Compound file '{fileName}' contains duplicate member '{name}'.");
            }

            long dataStart = input.Position;
            foreach (var (name, entry) in entries)
            {
                if (entry.Offset < dataStart || entry.Offset > fileLength || entry.Length < 0 || entry.Length > fileLength - entry.Offset)
                    throw new InvalidDataException($"Compound file '{fileName}' has an out-of-range member '{name}'.");
            }

            var orderedEntries = entries
                .OrderBy(static pair => pair.Value.Offset)
                .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToArray();
            for (int i = 1; i < orderedEntries.Length; i++)
            {
                var previous = orderedEntries[i - 1];
                var current = orderedEntries[i];
                if (current.Value.Offset < previous.Value.Offset + previous.Value.Length)
                {
                    throw new InvalidDataException(
                        $"Compound file '{fileName}' has overlapping members '{previous.Key}' and '{current.Key}'.");
                }
            }

            return new CompoundFileReader(fileName, entries, input);
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    internal bool HasFile(string fileName) => _entries.ContainsKey(fileName);

    /// <summary>Gets a stable snapshot of logical member names in ordinal order.</summary>
    internal IReadOnlyList<string> FileNames => _fileNames;

    internal long GetFileLength(string fileName)
    {
        if (!_entries.TryGetValue(fileName, out var entry))
            throw new FileNotFoundException($"Compound member '{fileName}' was not found.", _fileName);
        return entry.Length;
    }

    internal IndexInput OpenInput(MMapDirectory directory, string fileName)
    {
        if (!_entries.TryGetValue(fileName, out var entry))
            throw new FileNotFoundException($"Compound member '{fileName}' was not found.", _fileName);
        return _input.OpenSharedSlice(entry.Offset, entry.Length);
    }

    public void Dispose() => _input.Dispose();

    private static string ReadBoundedString(IndexInput input, string fileName)
    {
        int byteCount = ReadBounded7BitInt(input, fileName);
        if (byteCount is < 0 or > 1024)
            throw new InvalidDataException($"Compound file '{fileName}' has an overlong member name.");
        var bytes = input.ReadBytes(byteCount);
        return Encoding.UTF8.GetString(bytes);
    }

    private static int ReadBounded7BitInt(IndexInput input, string fileName)
    {
        int result = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            int value = input.ReadByte();
            result |= (value & 0x7F) << shift;
            if ((value & 0x80) == 0)
                return result;
        }
        throw new InvalidDataException($"Compound file '{fileName}' has a malformed member name length.");
    }

    private readonly record struct Entry(long Offset, long Length);
}
