using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.Cli.Creation;

/// <summary>Installs the pinned, package-generated 3.1.1 release fixture.</summary>
internal static class MigrationFixtureInstaller
{
    private const string ResourcePrefix = "Rowles.LeanCorpus.Cli.Fixtures.Migration.3.1.1-release-loose";
    private const string ArchiveSha256 = "657007c6f4d09eb317fe2903861c0ce1820cf672b2a7c5d2833035ea349a2299";
    private const string ManifestSha256 = "793ee7380213b809767a5f918ecb11ae34ffa9834a213f0015102c79bfddc2a9";

    internal static JsonDocument ExtractRelease311Loose(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        using Stream archiveResource = OpenResource(ResourcePrefix + ".fixture.b64");
        using var archiveText = new StreamReader(archiveResource);
        byte[] archive = Convert.FromBase64String(archiveText.ReadToEnd());
        if (Convert.ToHexStringLower(SHA256.HashData(archive)) != ArchiveSha256)
            throw new InvalidDataException("The embedded 3.1.1 fixture archive hash does not match its pin.");

        byte[] manifestBytes;
        using (Stream manifestResource = OpenResource(ResourcePrefix + ".manifest.json"))
        using (var bytes = new MemoryStream())
        {
            manifestResource.CopyTo(bytes);
            manifestBytes = bytes.ToArray();
        }
        if (Convert.ToHexStringLower(SHA256.HashData(manifestBytes)) != ManifestSha256)
            throw new InvalidDataException("The embedded 3.1.1 fixture manifest hash does not match its pin.");
        JsonDocument manifest = JsonDocument.Parse(manifestBytes);
        JsonElement root = manifest.RootElement;
        if (root.GetProperty("FixtureFormatVersion").GetInt32() != 1
            || root.GetProperty("LeanCorpusPackageVersion").GetString() != "3.1.1"
            || root.GetProperty("IsCompound").GetBoolean()
            || root.GetProperty("ArchiveSha256").GetString() != ArchiveSha256)
        {
            manifest.Dispose();
            throw new InvalidDataException("The embedded fixture provenance is invalid.");
        }

        try
        {
            Directory.CreateDirectory(destinationPath);
            using var archiveStream = new MemoryStream(archive, writable: false);
            using var gzip = new GZipStream(archiveStream, CompressionMode.Decompress, leaveOpen: false);
            TarFile.ExtractToDirectory(gzip, destinationPath, overwriteFiles: false);
            VerifyFiles(root, destinationPath);
            return manifest;
        }
        catch
        {
            manifest.Dispose();
            throw;
        }
    }

    private static Stream OpenResource(string name)
        => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded migration fixture resource '{name}' is missing.");

    private static void VerifyFiles(JsonElement manifest, string directoryPath)
    {
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in manifest.GetProperty("Files").EnumerateArray())
        {
            string name = item.GetProperty("Name").GetString()
                ?? throw new InvalidDataException("The release fixture contains a null file name.");
            if (Path.GetFileName(name) != name || !expectedFiles.Add(name))
                throw new InvalidDataException("The release fixture manifest contains an invalid file name.");

            string path = Path.Combine(directoryPath, name);
            if (!File.Exists(path) || new FileInfo(path).Length != item.GetProperty("Length").GetInt64())
                throw new InvalidDataException($"Release fixture file '{name}' is missing or has changed length.");
            using Stream stream = File.OpenRead(path);
            string hash = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(hash, item.GetProperty("Sha256").GetString(), StringComparison.Ordinal))
                throw new InvalidDataException($"Release fixture file '{name}' has changed content.");
        }

        string[] actualFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directoryPath, path))
            .ToArray();
        if (actualFiles.Length != expectedFiles.Count || actualFiles.Any(path => !expectedFiles.Contains(path)))
            throw new InvalidDataException("The extracted release fixture file set differs from its manifest.");
    }
}
