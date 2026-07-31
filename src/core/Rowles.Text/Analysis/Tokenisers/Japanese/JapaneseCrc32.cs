namespace Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

#if ROWLES_TEXT
internal static class JapaneseCrc32
{
    private static readonly uint[] Table = BuildTable();

    internal static uint Compute(ReadOnlySpan<byte> data)
    {
        uint value = 0xFFFFFFFFu;
        foreach (byte item in data)
            value = Table[(value ^ item) & 0xFF] ^ (value >> 8);
        return value ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }
}
#endif
