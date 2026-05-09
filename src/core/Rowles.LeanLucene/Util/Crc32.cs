namespace Rowles.LeanLucene.Util;

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
        uint c = 0xFFFFFFFFu;
        foreach (var b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>Computes the CRC-32 of <paramref name="text"/> using UTF-8.</summary>
    public static uint Compute(string text)
        => Compute(System.Text.Encoding.UTF8.GetBytes(text));
}
