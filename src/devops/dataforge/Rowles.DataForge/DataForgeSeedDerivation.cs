using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.DataForge;

public static class DataForgeSeedDerivation
{
    private static readonly byte[] Prefix = "DF-SEED-1"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ulong Derive(
        ulong rootSeed,
        string profileId,
        int profileVersion,
        ulong recordOrdinal,
        params string[] hierarchicalLabels)
    {
        Span<byte> digest = stackalloc byte[32];
        DeriveDigest(rootSeed, profileId, profileVersion, recordOrdinal, digest, hierarchicalLabels);
        return BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    public static byte[] DeriveDigest(
        ulong rootSeed,
        string profileId,
        int profileVersion,
        ulong recordOrdinal,
        params string[] hierarchicalLabels)
    {
        var digest = new byte[32];
        DeriveDigest(rootSeed, profileId, profileVersion, recordOrdinal, digest, hierarchicalLabels);
        return digest;
    }

    private static void DeriveDigest(
        ulong rootSeed,
        string profileId,
        int profileVersion,
        ulong recordOrdinal,
        Span<byte> digest,
        string[] hierarchicalLabels)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(hierarchicalLabels);
        if (profileVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(profileVersion));

        var profileBytes = GetStrictUtf8Bytes(profileId);
        if (profileBytes.Length is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(profileId), "Profile ID must contain 1 to 128 UTF-8 bytes.");

        using var input = new MemoryStream(Prefix.Length + 8 + 2 + profileBytes.Length + 4 + 8 + (hierarchicalLabels.Length * 66));
        input.Write(Prefix);

        Span<byte> numeric = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(numeric, rootSeed);
        input.Write(numeric);

        Span<byte> shortLength = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(shortLength, checked((ushort)profileBytes.Length));
        input.Write(shortLength);
        input.Write(profileBytes);

        Span<byte> version = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(version, checked((uint)profileVersion));
        input.Write(version);

        BinaryPrimitives.WriteUInt64BigEndian(numeric, recordOrdinal);
        input.Write(numeric);

        foreach (var label in hierarchicalLabels)
        {
            ArgumentNullException.ThrowIfNull(label);
            var labelBytes = GetStrictUtf8Bytes(label);
            if (labelBytes.Length > 64)
                throw new ArgumentOutOfRangeException(nameof(hierarchicalLabels), "Component labels must contain at most 64 UTF-8 bytes.");

            BinaryPrimitives.WriteUInt16BigEndian(shortLength, checked((ushort)labelBytes.Length));
            input.Write(shortLength);
            input.Write(labelBytes);
        }

        SHA256.HashData(input.GetBuffer().AsSpan(0, checked((int)input.Length)), digest);
    }

    public static byte[] GetStrictUtf8Bytes(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return StrictUtf8.GetBytes(value);
    }

    public static int GetStrictUtf8ByteCount(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return StrictUtf8.GetByteCount(value);
    }
}

public sealed class DataForgeRecordContext
{
    public DataForgeRecordContext(ulong rootSeed, string profileId, int profileVersion, ulong recordOrdinal)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        RootSeed = rootSeed;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
        RecordOrdinal = recordOrdinal;
    }

    public ulong RootSeed { get; }

    public string ProfileId { get; }

    public int ProfileVersion { get; }

    public ulong RecordOrdinal { get; }

    public ulong Seed(string label) =>
        DataForgeSeedDerivation.Derive(RootSeed, ProfileId, ProfileVersion, RecordOrdinal, label);

    public DataForgePrng Random(string label) => new(Seed(label));

    public DataForgeFaker Faker(string locale, string label) => DataForgeFakerFactory.Create(locale, Seed(label));
}
