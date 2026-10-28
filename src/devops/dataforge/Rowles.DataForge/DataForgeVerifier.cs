using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.DataForge;

public sealed record DataForgeVerificationResult(
    string ManifestPath,
    string RecordsPath,
    DataForgeManifest Manifest,
    DataForgeDatasetIdentity Identity,
    bool IsReproduced);

public static class DataForgeVerifier
{
    public static DataForgeVerificationResult VerifyMaterialised(string datasetDirectoryOrManifest)
    {
        var manifestPath = ResolveManifestPath(datasetDirectoryOrManifest);
        var recordsPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "records.ndjson");
        var manifest = DataForgeManifestCodec.Read(manifestPath);
        VerifyRecordsFile(recordsPath, manifest);
        return new DataForgeVerificationResult(
            manifestPath,
            recordsPath,
            manifest,
            DataForgeDatasetIdentity.FromManifest(manifest),
            IsReproduced: false);
    }

    public static DataForgeVerificationResult Reproduce<TRecord>(
        string datasetDirectoryOrManifest,
        IDataForgeGeneratedProfile<TRecord> profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var manifestPath = ResolveManifestPath(datasetDirectoryOrManifest);
        var manifest = DataForgeManifestCodec.Read(manifestPath);
        if (manifest.SourceKind != DataForgeSourceKind.Generated)
            throw new InvalidDataException("Only generated manifests can be reproduced by a generated profile.");
        if (!string.Equals(manifest.ProfileId, profile.Descriptor.ProfileId, StringComparison.Ordinal) ||
            manifest.ProfileVersion != profile.Descriptor.ProfileVersion)
            throw new InvalidDataException($"Manifest profile {manifest.ProfileId} v{manifest.ProfileVersion} does not match {profile.Descriptor.ProfileId} v{profile.Descriptor.ProfileVersion}.");

        var parameters = manifest.Parameters.ToDictionary(static item => item.Name, static item => item.Value, StringComparer.Ordinal);
        var options = new DataForgeGenerationOptions(manifest.Seed!.Value, manifest.RecordCount, parameters);
        using var sink = Stream.Null;
        var generated = DataForgeMaterialiser.GenerateToStream(profile, options, sink);
        Compare("record count", manifest.RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture), generated.RecordCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Compare("contentSha256", manifest.ContentSha256, generated.ContentSha256);
        Compare("logicalByteCount", manifest.LogicalByteCount.ToString(System.Globalization.CultureInfo.InvariantCulture), generated.LogicalByteCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var recordsPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "records.ndjson");
        return new DataForgeVerificationResult(
            manifestPath,
            recordsPath,
            manifest,
            DataForgeDatasetIdentity.FromManifest(manifest),
            IsReproduced: true);
    }

    public static string ResolveManifestPath(string datasetDirectoryOrManifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(datasetDirectoryOrManifest);
        var path = Path.GetFullPath(datasetDirectoryOrManifest);
        var manifest = Directory.Exists(path) ? Path.Combine(path, "manifest.json") : path;
        if (!File.Exists(manifest))
            throw new FileNotFoundException($"Manifest '{manifest}' does not exist.", manifest);
        return manifest;
    }

    private static void VerifyRecordsFile(string path, DataForgeManifest manifest)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Records file '{path}' does not exist.", path);

        var maximumLength = (long)manifest.RecordCount * (4_194_304L + 1);
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > maximumLength)
            throw new InvalidDataException($"Records file length {fileInfo.Length} exceeds the declared bound {maximumLength}.");
        if (fileInfo.Length != manifest.LogicalByteCount)
            throw new InvalidDataException($"logicalByteCount mismatch: expected {manifest.LogicalByteCount}, actual {fileInfo.Length}.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var line = new MemoryStream(8 * 1024);
        var buffer = new byte[64 * 1024];
        var totalBytes = 0L;
        var recordCount = 0;
        var read = 0;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            hash.AppendData(buffer.AsSpan(0, read));
            totalBytes += read;
            var offset = 0;
            while (offset < read)
            {
                var lineFeed = Array.IndexOf(buffer, (byte)'\n', offset, read - offset);
                var end = lineFeed < 0 ? read : lineFeed;
                var segment = buffer.AsSpan(offset, end - offset);
                if (segment.Contains((byte)'\r'))
                    throw new InvalidDataException($"CR bytes are not permitted in canonical NDJSON near record {recordCount}.");
                line.Write(segment);
                if (line.Length > 4_194_304)
                    throw new InvalidDataException($"Record {recordCount} exceeds the 4 MiB canonical record limit.");

                if (lineFeed < 0)
                    break;
                if (line.Length == 0)
                    throw new InvalidDataException($"Empty NDJSON record at line {recordCount + 1}.");

                try
                {
                    using var document = JsonDocument.Parse(line.GetBuffer().AsMemory(0, checked((int)line.Length)));
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException($"NDJSON record {recordCount} is not a JSON object.");
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException($"NDJSON record {recordCount} is not one valid JSON object.", exception);
                }

                recordCount++;
                if (recordCount > manifest.RecordCount)
                    throw new InvalidDataException($"Records file contains more than the declared {manifest.RecordCount} records.");
                line.SetLength(0);
                offset = lineFeed + 1;
            }
        }

        if (totalBytes == 0 || line.Length != 0)
            throw new InvalidDataException("Canonical NDJSON must end with LF after its final record.");
        if (recordCount != manifest.RecordCount)
            throw new InvalidDataException($"recordCount mismatch: expected {manifest.RecordCount}, actual {recordCount}.");

        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        Compare("contentSha256", manifest.ContentSha256, actualHash);
        Compare("artefactSha256", manifest.ArtefactSha256, actualHash);
    }

    private static void Compare(string name, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidDataException($"{name} mismatch: expected {expected}, actual {actual}.");
    }
}
