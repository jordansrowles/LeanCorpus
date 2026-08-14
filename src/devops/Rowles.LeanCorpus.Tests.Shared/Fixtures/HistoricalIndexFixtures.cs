using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>Immutable full-index fixtures emitted by historical LeanCorpus releases.</summary>
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
            _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "Unknown historical index fixture."),
        };
    }

    private readonly record struct FixtureDescriptor(string ResourceName, string Sha256);
}
