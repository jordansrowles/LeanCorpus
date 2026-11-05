using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>Immutable index and codec fixtures emitted by historical LeanCorpus writers.</summary>
public enum HistoricalIndexFixture
{
    /// <summary>Loose-file index written by the final 2.0.0 release source.</summary>
    Version200Loose,

    /// <summary>Loose-file index written by the tagged 2.3.0 release source.</summary>
    Version230Loose,

    /// <summary>Compound index written by the tagged 2.3.0 release source.</summary>
    Version230Compound,

    /// <summary>Loose-file index written by the 3.0 canonical writer.</summary>
    Version300CurrentLoose,

    /// <summary>Package-generated 3.1.1 sparse float32 and Int8 vector indexes with HNSW, loose and compound.</summary>
    Version311SparseHnsw,

    /// <summary>Package-generated comprehensive 3.1.1 loose release corpus.</summary>
    Version311ReleaseLoose,

    /// <summary>Package-generated comprehensive 3.1.1 compound release corpus.</summary>
    Version311ReleaseCompound,

    /// <summary>Loose and compound BBQ v2 indexes generated from the pinned 31 October branch baseline.</summary>
    PreV3BbqV2,

    /// <summary>Pre-retirement one-bit v3 loose, compound, mixed and all-deleted experimental indexes.</summary>
    ExperimentalBbqV3,

    /// <summary>Historical v2 BBQ structurally migrated to a committed v3 sign-BBQ tag-2 index.</summary>
    ExperimentalBbqV3LegacySign,

    /// <summary>Pre-retirement vector bodies for historical decoder/scorer regression inputs.</summary>
    ExperimentalBbqCodecBodies,

    /// <summary>Pre-retirement indexes for historical query, backup and reader-lifetime checks.</summary>
    ExperimentalBbqQueryIndexes,
}

/// <summary>
/// Extracts committed historical fixture bytes without invoking a current codec writer.
/// The compressed archive hashes make accidental fixture regeneration visible.
/// </summary>
public static class HistoricalIndexFixtures
{
    /// <summary>Extracts one historical fixture beneath <paramref name="destinationPath"/>.</summary>
    public static string Extract(HistoricalIndexFixture fixture, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var descriptor = Describe(fixture);
        using Stream resource = typeof(HistoricalIndexFixtures).Assembly.GetManifestResourceStream(descriptor.ResourceName)
            ?? throw new InvalidOperationException($"Embedded historical fixture '{descriptor.ResourceName}' is missing.");
        using var reader = new StreamReader(resource);
        byte[] archive = Convert.FromBase64String(reader.ReadToEnd());
        string actualHash = Convert.ToHexStringLower(SHA256.HashData(archive));
        if (!actualHash.Equals(descriptor.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Historical fixture '{fixture}' has SHA-256 {actualHash}, expected {descriptor.Sha256}.");

        Directory.CreateDirectory(destinationPath);
        using var archiveStream = new MemoryStream(archive, writable: false);
        using var gzip = new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: false);
        TarFile.ExtractToDirectory(gzip, destinationPath, overwriteFiles: false);
        return destinationPath;
    }

    private static FixtureDescriptor Describe(HistoricalIndexFixture fixture)
    {
        const string prefix = "Rowles.LeanCorpus.Tests.Shared.Fixtures.Indexes.";
        return fixture switch
        {
            HistoricalIndexFixture.Version200Loose => new(
                prefix + "2.0.0-loose.fixture.b64",
                "d022f448cfe11cc499048c83b0a875aaa13ca62987b4f990fa1934e5a0f992ac"),
            HistoricalIndexFixture.Version230Loose => new(
                prefix + "2.3.0-loose.fixture.b64",
                "b3ef24c5ff1e15644d85ff515a5923ddf7101a48c12a83210d8d8cc33f37a1bc"),
            HistoricalIndexFixture.Version230Compound => new(
                prefix + "2.3.0-compound.fixture.b64",
                "8eccca72a7440c55c29f76257fe07d3efe6ddbc9befdf559b0d21b3909d7e482"),
            HistoricalIndexFixture.Version300CurrentLoose => new(
                prefix + "3.0.0-current-loose.fixture.b64",
                "7ee9c07289caef25fcc2fce0cba08db00918a3ea2b3e3a16fdaa83f05088b33f"),
            HistoricalIndexFixture.Version311SparseHnsw => new(
                prefix + "3.1.1-sparse-hnsw.fixture.b64",
                "ebaec3b42819af4c35049e589d75fc682dd4b6169ad85fbf2e7b7197e066a961"),
            HistoricalIndexFixture.Version311ReleaseLoose => new(
                prefix + "3.1.1-release-loose.fixture.b64",
                "657007c6f4d09eb317fe2903861c0ce1820cf672b2a7c5d2833035ea349a2299"),
            HistoricalIndexFixture.Version311ReleaseCompound => new(
                prefix + "3.1.1-release-compound.fixture.b64",
                "e3adfcba53955611f65282e3080f19035d15fa1ed2b8612f110de4d1b60c79a2"),
            HistoricalIndexFixture.PreV3BbqV2 => new(
                prefix + "pre-v3-bbq-v2.fixture.b64",
                "189d29dbe1fe84cbd5806ab56c32babcada457efdb1c3ff5da9f6530b11f2b9c"),
            HistoricalIndexFixture.ExperimentalBbqV3 => new(
                prefix + "experimental-bbq-v3.fixture.b64",
                "9093003ea1a6c00e2d19f6a46e603762ab4086df9c0a4ce1975f5d12e63f316c"),
            HistoricalIndexFixture.ExperimentalBbqV3LegacySign => new(
                prefix + "experimental-bbq-v3-legacy-sign.fixture.b64",
                "9d99f8b29309b3741d8071d99338b5d9f651afb9926b8ba6b3849ad70f75fcbd"),
            HistoricalIndexFixture.ExperimentalBbqCodecBodies => new(
                prefix + "experimental-bbq-codec-bodies.fixture.b64",
                "7fa4fbc127ed0bbb24ea692d6260d84f49e5a6cbf3f04b2b39a31635e68d38b3"),
            HistoricalIndexFixture.ExperimentalBbqQueryIndexes => new(
                prefix + "experimental-bbq-query-indexes.fixture.b64",
                "acda9cbe4d774705f918a3a4f6135be95faf9394d4d9ac674155080e6b0bc41c"),
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown historical index fixture."),
        };
    }

    private readonly record struct FixtureDescriptor(string ResourceName, string Sha256);
}
