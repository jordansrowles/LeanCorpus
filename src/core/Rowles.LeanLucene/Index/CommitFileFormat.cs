namespace Rowles.LeanLucene.Index;

/// <summary>
/// On-disk format helpers for <c>segments_N</c> commit files. Commits written by
/// <see cref="Indexer.IndexWriter"/> append a trailer of the form
/// <c>\n#crc32=&lt;hex&gt;\n</c> after the JSON body so torn writes can be detected
/// on recovery. This helper centralises read/write so every parser strips and
/// validates the trailer consistently.
/// </summary>
internal static class CommitFileFormat
{
    private const string Marker = "\n#crc32=";

    /// <summary>
    /// Wraps a JSON commit body with a CRC32 trailer.
    /// </summary>
    public static string Wrap(string json)
    {
        var crc = Util.Crc32.Compute(json);
        return json + Marker + crc.ToString("x8") + "\n";
    }

    /// <summary>
    /// Reads a commit file, returning the JSON body with any CRC trailer stripped
    /// and validated. Throws <see cref="InvalidDataException"/> if the trailer is
    /// present but does not match the body.
    /// </summary>
    public static string ReadJson(string path)
    {
        var raw = File.ReadAllText(path);
        return StripAndValidate(raw)
            ?? throw new InvalidDataException(
                $"Commit file '{path}' has a CRC mismatch; the file is likely the result of a torn write.");
    }

    /// <summary>
    /// Returns the JSON body if the trailer is absent or matches; otherwise null.
    /// Suitable for recovery code paths that prefer to fall back to an older commit.
    /// </summary>
    public static string? TryReadJson(string path)
    {
        var raw = File.ReadAllText(path);
        return StripAndValidate(raw);
    }

    private static string? StripAndValidate(string raw)
    {
        int idx = raw.LastIndexOf(Marker, StringComparison.Ordinal);
        if (idx < 0) return raw;

        var json = raw.Substring(0, idx);
        var trailer = raw.Substring(idx + Marker.Length).TrimEnd('\r', '\n');
        if (!uint.TryParse(trailer, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var expected))
            return null;

        return Util.Crc32.Compute(json) == expected ? json : null;
    }
}
