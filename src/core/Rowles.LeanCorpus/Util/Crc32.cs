namespace Rowles.LeanCorpus.Util;

/// <summary>
/// IEEE 802.3 CRC-32 (polynomial 0xEDB88320). Used to detect torn commit-file writes.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
                c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            table[i] = c;
        }
        return table;
    }

    /// <summary>Computes the CRC-32 of <paramref name="data"/>.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Finish(Update(Begin(), data));
    }

    /// <summary>Computes the CRC-32 of all bytes read from <paramref name="stream"/>.</summary>
    public static uint Compute(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        uint c = Begin();
        Span<byte> buffer = stackalloc byte[16 * 1024];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            c = Update(c, buffer[..read]);

        return Finish(c);
    }

    /// <summary>Computes the CRC-32 of <paramref name="text"/> using UTF-8.</summary>
    public static uint Compute(string text)
        => Compute(System.Text.Encoding.UTF8.GetBytes(text));

    internal static uint Begin() => 0xFFFFFFFFu;

    internal static uint Update(uint state, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            state = Table[(state ^ b) & 0xFF] ^ (state >> 8);

        return state;
    }

    internal static uint Finish(uint state) => state ^ 0xFFFFFFFFu;
}
