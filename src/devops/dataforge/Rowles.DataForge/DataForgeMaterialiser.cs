namespace Rowles.DataForge;

public sealed record DataForgeMaterialisationSummary(
    int RecordCount,
    long LogicalByteCount,
    string ContentSha256,
    IReadOnlyList<DataForgeSummary> Summaries);

public sealed record DataForgeMaterialisationResult(
    string OutputDirectory,
    DataForgeManifest Manifest,
    DataForgeDatasetIdentity Identity);

/// <summary>Materialises generated profiles as canonical, uncompressed NDJSON datasets.</summary>
public static class DataForgeMaterialiser
{
    public static DataForgeMaterialisationResult Materialise<TRecord>(
        IDataForgeGeneratedProfile<TRecord> profile,
        DataForgeGenerationOptions options,
        string outputDirectory,
        bool force = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ValidateProfile(profile);

        var fullOutputPath = Path.GetFullPath(outputDirectory);
        var parent = Path.GetDirectoryName(fullOutputPath) ?? throw new ArgumentException("Output directory must have a parent directory.", nameof(outputDirectory));
        Directory.CreateDirectory(parent);

        if (File.Exists(fullOutputPath))
            throw new IOException($"Output path '{fullOutputPath}' is a file.");
        if (Directory.Exists(fullOutputPath) && !force)
            throw new IOException($"Output directory '{fullOutputPath}' already exists. Use -Force to replace it.");

        var temporaryDirectory = fullOutputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var backupDirectory = fullOutputPath + ".old-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var recordsPath = Path.Combine(temporaryDirectory, "records.ndjson");
            DataForgeMaterialisationSummary generated;
            using (var stream = new FileStream(recordsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            {
                generated = GenerateToStream(profile, options, stream);
                stream.Flush(flushToDisk: true);
            }

            var manifest = new DataForgeManifest(
                DataForgeVersions.ManifestSchemaVersion,
                DataForgeVersions.CanonicalFormatVersion,
                DataForgeVersions.DataForgeVersion,
                DataForgeSourceKind.Generated,
                profile.Descriptor.ProfileId,
                profile.Descriptor.ProfileVersion,
                options.Seed,
                generated.RecordCount,
                options.Parameters.Select(static pair => new DataForgeParameter(pair.Key, pair.Value)).ToArray(),
                profile.Dependencies,
                generated.LogicalByteCount,
                generated.ContentSha256,
                generated.ContentSha256,
                generated.Summaries,
                Source: null);
            var manifestPath = Path.Combine(temporaryDirectory, "manifest.json");
            using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.SequentialScan))
            {
                DataForgeManifestCodec.Write(stream, manifest);
                stream.Flush(flushToDisk: true);
            }

            var movedExistingDirectory = false;
            try
            {
                if (Directory.Exists(fullOutputPath))
                {
                    Directory.Move(fullOutputPath, backupDirectory);
                    movedExistingDirectory = true;
                }
                Directory.Move(temporaryDirectory, fullOutputPath);
            }
            catch
            {
                if (movedExistingDirectory && !Directory.Exists(fullOutputPath) && Directory.Exists(backupDirectory))
                    Directory.Move(backupDirectory, fullOutputPath);
                throw;
            }

            if (movedExistingDirectory)
                Directory.Delete(backupDirectory, recursive: true);

            return new DataForgeMaterialisationResult(
                fullOutputPath,
                manifest,
                DataForgeDatasetIdentity.FromManifest(manifest));
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    public static DataForgeMaterialisationSummary GenerateToStream<TRecord>(
        IDataForgeGeneratedProfile<TRecord> profile,
        DataForgeGenerationOptions options,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        ValidateProfile(profile);

        using var writer = new CanonicalJsonWriter(destination);
        using var records = profile.Generate(options).GetEnumerator();
        for (var ordinal = 0; ordinal < options.RecordCount; ordinal++)
        {
            if (!records.MoveNext())
                throw new InvalidDataException($"Profile '{profile.Descriptor.ProfileId}' generated {ordinal} records; expected {options.RecordCount}.");
            var recordStart = writer.BytesWritten;
            profile.CanonicalRecordWriter.Write(writer, records.Current);
            if (writer.BytesWritten == recordStart)
                throw new InvalidDataException($"Profile '{profile.Descriptor.ProfileId}' wrote an empty record at ordinal {ordinal}.");
            if (writer.BytesWritten - recordStart > 4_194_304)
                throw new InvalidDataException($"Profile '{profile.Descriptor.ProfileId}' wrote a record larger than 4 MiB at ordinal {ordinal}.");
            writer.WriteLine();
        }

        if (records.MoveNext())
            throw new InvalidDataException($"Profile '{profile.Descriptor.ProfileId}' generated more than the requested {options.RecordCount} records.");

        var hash = Convert.ToHexString(writer.GetSha256()).ToLowerInvariant();
        return new DataForgeMaterialisationSummary(
            options.RecordCount,
            writer.BytesWritten,
            hash,
            profile.Summarise(options));
    }

    private static void ValidateProfile<TRecord>(IDataForgeGeneratedProfile<TRecord> profile)
    {
        var descriptor = profile.Descriptor ?? throw new InvalidOperationException("Profile descriptor cannot be null.");
        if (descriptor.ProfileVersion < 1)
            throw new InvalidOperationException("Profile version must be positive.");
        var length = DataForgeSeedDerivation.GetStrictUtf8ByteCount(descriptor.ProfileId);
        if (length is < 1 or > 128)
            throw new InvalidOperationException("Profile ID must contain 1 to 128 UTF-8 bytes.");
    }
}
