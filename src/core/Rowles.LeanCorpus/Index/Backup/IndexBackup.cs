using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Creates, validates, and restores LeanCorpus index backups.
/// </summary>
public static class IndexBackup
{
    /// <summary>Gets the current backup manifest format version.</summary>
    public const string CurrentManifestFormatVersion = "1";

    /// <summary>Gets the manifest file name used in backup directories.</summary>
    public const string ManifestFileName = "leancorpus-backup-manifest.json";

    private static readonly string[] RequiredSegmentExtensions = [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm"];

    /// <summary>
    /// Creates a backup manifest for a selected commit without copying files.
    /// </summary>
    /// <param name="indexDirectoryPath">The source index directory path.</param>
    /// <param name="options">Backup options. When <c>null</c>, the latest commit is selected.</param>
    /// <returns>A manifest containing all files required to restore the selected commit.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="indexDirectoryPath"/> is invalid.</exception>
    /// <exception cref="InvalidDataException">Thrown when the selected commit or segment metadata cannot be read.</exception>
    public static IndexBackupManifest CreateManifest(string indexDirectoryPath, IndexBackupOptions? options = null)
    {
        options ??= new IndexBackupOptions();
        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.BackupManifest);
        IndexBackupManifest? manifest = null;
        var succeeded = false;
        try
        {
            // Retry on transient file-not-found — a concurrent background merge
            // may delete segment files between commit selection and file enumeration.
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    manifest = CreateManifestCore(indexDirectoryPath, options);
                    break;
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    if (attempt >= maxAttempts) throw;
                    Thread.Sleep(20 * attempt);
                }
            }
            succeeded = true;
            return manifest;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            ApplyManifestActivityTags(activity, manifest);
            activity?.SetTag("index.backup.include_commit_stats", options.IncludeCommitStats);
            LeanCorpusMaintenanceMetrics.RecordBackupManifest(sw.Elapsed, succeeded, options.IncludeCommitStats);
        }
    }

    private static IndexBackupManifest CreateManifestCore(string indexDirectoryPath, IndexBackupOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexDirectoryPath);
        var sourceDirectory = Path.GetFullPath(indexDirectoryPath);
        if (!Directory.Exists(sourceDirectory))
            throw new ArgumentException($"Index directory '{sourceDirectory}' does not exist.", nameof(indexDirectoryPath));

        var selectedCommit = SelectCommit(sourceDirectory, options.CommitGeneration);
        var commitJson = CommitFileFormat.ReadJson(selectedCommit.FilePath);
        var commitData = JsonSerializer.Deserialize(commitJson, LeanCorpusJsonContext.Default.CommitData)
            ?? throw new InvalidDataException($"Commit file '{Path.GetFileName(selectedCommit.FilePath)}' cannot be deserialised.");

        commitData.Validate();

        if (commitData.Generation != selectedCommit.Generation)
            throw new InvalidDataException($"Commit file '{Path.GetFileName(selectedCommit.FilePath)}' records generation {commitData.Generation}, expected {selectedCommit.Generation}.");

        var entries = new Dictionary<string, IndexBackupFileEntry>(StringComparer.Ordinal);
        AddEntry(entries, sourceDirectory, Path.GetFileName(selectedCommit.FilePath), null, "commit", isRequired: true, isCommitFile: true);

        foreach (var segmentId in commitData.Segments)
        {
            var segmentFileName = segmentId + ".seg";
            var segmentInfo = SegmentInfo.ReadFrom(Path.Combine(sourceDirectory, segmentFileName));
            if (!string.Equals(segmentInfo.SegmentId, segmentId, StringComparison.Ordinal))
                throw new InvalidDataException($"Segment metadata '{segmentFileName}' records segment ID '{segmentInfo.SegmentId}', expected '{segmentId}'.");

            foreach (var extension in RequiredSegmentExtensions)
                AddEntry(entries, sourceDirectory, segmentId + extension, segmentId, ClassifySegmentFile(segmentId + extension, selectedCommit.Generation), isRequired: true, isCommitFile: false);

            foreach (var fileName in EnumerateSegmentFileNames(sourceDirectory, segmentId))
            {
                if (entries.ContainsKey(fileName))
                    continue;

                AddEntry(entries, sourceDirectory, fileName, segmentId, ClassifySegmentFile(fileName, selectedCommit.Generation), isRequired: false, isCommitFile: false);
            }
        }

        if (options.IncludeCommitStats)
        {
            var statsFileName = $"stats_{selectedCommit.Generation}.json";
            if (File.Exists(Path.Combine(sourceDirectory, statsFileName)))
                AddEntry(entries, sourceDirectory, statsFileName, null, "commit-stats", isRequired: false, isCommitFile: false);
        }

        var files = entries.Values.OrderBy(static entry => entry.FileName, StringComparer.Ordinal).ToList();
        return new IndexBackupManifest
        {
            FormatVersion = CurrentManifestFormatVersion,
            CommitGeneration = selectedCommit.Generation,
            ContentToken = commitData.ContentToken,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CommitFileName = Path.GetFileName(selectedCommit.FilePath),
            Files = files
        };
    }

    /// <summary>
    /// Creates a backup by copying all manifest files into a backup directory.
    /// </summary>
    /// <param name="indexDirectoryPath">The source index directory path.</param>
    /// <param name="backupDirectoryPath">The target backup directory path.</param>
    /// <param name="options">Backup options. When <c>null</c>, the latest commit is selected.</param>
    /// <returns>The backup result.</returns>
    /// <exception cref="ArgumentException">Thrown when a directory path is invalid.</exception>
    /// <exception cref="InvalidDataException">Thrown when the selected commit or segment metadata cannot be read.</exception>
    public static IndexBackupResult Backup(string indexDirectoryPath, string backupDirectoryPath, IndexBackupOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectoryPath);
        options ??= new IndexBackupOptions();
        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.BackupCopy);
        IndexBackupManifest? manifest = null;
        IndexBackupResult? result = null;
        var succeeded = false;
        try
        {
            var sourceDirectory = Path.GetFullPath(indexDirectoryPath);
            var backupDirectory = Path.GetFullPath(backupDirectoryPath);
            if (SameDirectory(sourceDirectory, backupDirectory))
                throw new ArgumentException("Backup directory must be different from the source index directory.", nameof(backupDirectoryPath));

            // Retry on transient file-not-found — a concurrent background merge
            // may delete segment files between manifest creation and file copy.
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    manifest = CreateManifestCore(sourceDirectory, options);
                    PrepareDirectory(backupDirectory, options.OverwriteBackupDirectory, "Backup");

                    var copiedFiles = new List<string>(manifest.Files.Count);
                    foreach (var entry in manifest.Files)
                    {
                        ValidateManifestFileName(entry.FileName);
                        var sourcePath = Path.Combine(sourceDirectory, entry.FileName);
                        var targetPath = Path.Combine(backupDirectory, entry.FileName);
                        CopyFileAtomically(sourcePath, targetPath);
                        copiedFiles.Add(entry.FileName);
                    }

                    var manifestJson = JsonSerializer.Serialize(manifest, LeanCorpusJsonContext.Default.IndexBackupManifest);
                    IndexAtomicFileWriter.WriteText(Path.Combine(backupDirectory, ManifestFileName), manifestJson, durable: true);

                    result = new IndexBackupResult
                    {
                        Manifest = manifest,
                        BackupDirectoryPath = backupDirectory,
                        CopiedFiles = copiedFiles
                    };
                    succeeded = true;
                    return result;
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    if (attempt >= maxAttempts) throw;
                    Thread.Sleep(20 * attempt);
                }
            }
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            ApplyManifestActivityTags(activity, manifest);
            activity?.SetTag("index.backup.overwrite", options.OverwriteBackupDirectory);
            LeanCorpusMaintenanceMetrics.RecordBackupCopy(sw.Elapsed, succeeded, options.OverwriteBackupDirectory);
        }
    }

    /// <summary>
    /// Reads a backup manifest from a backup directory.
    /// </summary>
    /// <param name="backupDirectoryPath">The backup directory path.</param>
    /// <returns>The deserialised backup manifest.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="backupDirectoryPath"/> is invalid.</exception>
    /// <exception cref="InvalidDataException">Thrown when the manifest is missing or invalid.</exception>
    public static IndexBackupManifest ReadManifest(string backupDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectoryPath);
        var backupDirectory = Path.GetFullPath(backupDirectoryPath);
        var manifestPath = Path.Combine(backupDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new InvalidDataException($"Backup manifest '{ManifestFileName}' was not found.");

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.IndexBackupManifest)
            ?? throw new InvalidDataException($"Backup manifest '{ManifestFileName}' cannot be deserialised.");

        if (!string.Equals(manifest.FormatVersion, CurrentManifestFormatVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Backup manifest format '{manifest.FormatVersion}' is not supported.");

        return manifest;
    }

    /// <summary>
    /// Validates that every file listed in a backup manifest is present and has the recorded length and checksum.
    /// </summary>
    /// <param name="backupDirectoryPath">The backup directory path.</param>
    /// <returns>The validated backup manifest.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="backupDirectoryPath"/> is invalid.</exception>
    /// <exception cref="InvalidDataException">Thrown when any manifest entry is unsafe, missing, or corrupt.</exception>
    public static IndexBackupManifest ValidateBackup(string backupDirectoryPath)
    {
        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.BackupValidate);
        IndexBackupManifest? manifest = null;
        var succeeded = false;
        try
        {
            manifest = ValidateBackupCore(backupDirectoryPath);
            succeeded = true;
            return manifest;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            ApplyManifestActivityTags(activity, manifest);
            LeanCorpusMaintenanceMetrics.RecordBackupValidate(sw.Elapsed, succeeded);
        }
    }

    private static IndexBackupManifest ValidateBackupCore(string backupDirectoryPath)
    {
        var backupDirectory = Path.GetFullPath(backupDirectoryPath);
        var manifest = ReadManifest(backupDirectory);
        foreach (var entry in manifest.Files)
        {
            ValidateManifestFileName(entry.FileName);
            var path = Path.Combine(backupDirectory, entry.FileName);
            if (!File.Exists(path))
                throw new InvalidDataException($"Backup file '{entry.FileName}' is missing.");

            var info = new FileInfo(path);
            if (info.Length != entry.Length)
                throw new InvalidDataException($"Backup file '{entry.FileName}' has length {info.Length}, expected {entry.Length}.");

            var checksum = ComputeFileCrc32(path);
            if (checksum != entry.Crc32)
                throw new InvalidDataException($"Backup file '{entry.FileName}' has CRC-32 {checksum:x8}, expected {entry.Crc32:x8}.");
        }

        return manifest;
    }

    /// <summary>
    /// Restores a validated backup into a target index directory.
    /// </summary>
    /// <param name="backupDirectoryPath">The source backup directory path.</param>
    /// <param name="targetIndexDirectoryPath">The target index directory path.</param>
    /// <param name="options">Restore options. When <c>null</c>, validation is run and non-empty targets are rejected.</param>
    /// <returns>The restore result.</returns>
    /// <exception cref="ArgumentException">Thrown when a directory path is invalid.</exception>
    /// <exception cref="InvalidDataException">Thrown when the backup is invalid or unsafe.</exception>
    public static IndexRestoreResult Restore(string backupDirectoryPath, string targetIndexDirectoryPath, IndexRestoreOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIndexDirectoryPath);
        options ??= new IndexRestoreOptions();
        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.BackupRestore);
        IndexBackupManifest? manifest = null;
        IndexRestoreResult? result = null;
        var succeeded = false;
        try
        {
            var backupDirectory = Path.GetFullPath(backupDirectoryPath);
            var targetDirectory = Path.GetFullPath(targetIndexDirectoryPath);
            if (SameDirectory(backupDirectory, targetDirectory))
                throw new ArgumentException("Restore target directory must be different from the backup directory.", nameof(targetIndexDirectoryPath));

            manifest = ValidateBackupCore(backupDirectory);
            PrepareDirectory(targetDirectory, options.OverwriteTargetDirectory, "Restore target");

            var restoredFiles = new List<string>(manifest.Files.Count);
            foreach (var entry in manifest.Files)
            {
                ValidateManifestFileName(entry.FileName);
                if (!options.RestoreCommitStats && string.Equals(entry.Role, "commit-stats", StringComparison.Ordinal))
                    continue;

                var sourcePath = Path.Combine(backupDirectory, entry.FileName);
                var targetPath = Path.Combine(targetDirectory, entry.FileName);
                CopyFileAtomically(sourcePath, targetPath);
                restoredFiles.Add(entry.FileName);
            }

            IndexCheckResult? validation = null;
            if (options.ValidateAfterRestore)
            {
                using var directory = new MMapDirectory(targetDirectory);
                validation = IndexValidator.Check(directory);
            }

            result = new IndexRestoreResult
            {
                Manifest = manifest,
                TargetDirectoryPath = targetDirectory,
                RestoredFiles = restoredFiles,
                ValidationResult = validation
            };
            succeeded = true;
            return result;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            ApplyManifestActivityTags(activity, manifest);
            activity?.SetTag("index.restore.file_count", result?.RestoredFiles.Count ?? 0);
            activity?.SetTag("index.restore.validate_after_restore", options.ValidateAfterRestore);
            activity?.SetTag("index.restore.restore_commit_stats", options.RestoreCommitStats);
            activity?.SetTag("index.restore.overwrite", options.OverwriteTargetDirectory);
            LeanCorpusMaintenanceMetrics.RecordBackupRestore(
                sw.Elapsed,
                succeeded,
                options.ValidateAfterRestore,
                options.RestoreCommitStats,
                options.OverwriteTargetDirectory);
        }
    }

    private static (int Generation, string FilePath) SelectCommit(string directoryPath, int? generation)
    {
        var commits = IndexFileInspector.FindCommitFiles(directoryPath);
        if (generation is null)
        {
            if (commits.Count == 0)
                throw new InvalidDataException("No commit file (segments_N) was found in the source index directory.");

            return commits[0];
        }

        foreach (var commit in commits)
        {
            if (commit.Generation == generation.Value)
                return commit;
        }

        throw new InvalidDataException($"Commit generation {generation.Value} was not found in the source index directory.");
    }

    private static IEnumerable<string> EnumerateSegmentFileNames(string directoryPath, string segmentId)
    {
        var fileNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directoryPath, segmentId + ".*"))
            fileNames.Add(Path.GetFileName(path));
        foreach (var path in Directory.EnumerateFiles(directoryPath, segmentId + "_gen_*.del"))
            fileNames.Add(Path.GetFileName(path));
        foreach (var path in Directory.EnumerateFiles(directoryPath, segmentId + "_v_*.*"))
            fileNames.Add(Path.GetFileName(path));

        return fileNames.OrderBy(static name => name, StringComparer.Ordinal);
    }

    private static void AddEntry(
        Dictionary<string, IndexBackupFileEntry> entries,
        string directoryPath,
        string fileName,
        string? segmentId,
        string role,
        bool isRequired,
        bool isCommitFile)
    {
        ValidateManifestFileName(fileName);
        var path = Path.Combine(directoryPath, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required backup file '{fileName}' was not found.", path);

        var info = new FileInfo(path);
        entries[fileName] = new IndexBackupFileEntry
        {
            FileName = fileName,
            Length = info.Length,
            Crc32 = ComputeFileCrc32(path),
            SegmentId = segmentId,
            Role = role,
            IsRequired = isRequired,
            IsCommitFile = isCommitFile
        };
    }

    private static string ClassifySegmentFile(string fileName, int commitGeneration)
    {
        if (string.Equals(fileName, $"stats_{commitGeneration}.json", StringComparison.Ordinal))
            return "commit-stats";
        if (fileName.EndsWith(".stats.json", StringComparison.OrdinalIgnoreCase))
            return "segment-stats";

        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".seg" => "segment-metadata",
            ".dic" => "term-dictionary",
            ".pos" => "postings",
            ".nrm" => "norms",
            ".fln" => "field-length",
            ".num" => "numeric-field-index",
            ".bkd" => "bkd",
            ".dvn" => "numeric-doc-values",
            ".dvs" => "sorted-doc-values",
            ".dss" => "sorted-set-doc-values",
            ".dsn" => "sorted-numeric-doc-values",
            ".dvb" => "binary-doc-values",
            ".fdt" => "stored-fields",
            ".fdx" => "stored-fields",
            ".tvd" => "term-vector-data",
            ".tvx" => "term-vector-index",
            ".pbs" => "parent-bitset",
            ".del" => "live-docs",
            ".vec" => "vector",
            ".hnsw" => "hnsw",
            _ => "sidecar"
        };
    }

    private static void ApplyManifestActivityTags(Activity? activity, IndexBackupManifest? manifest)
    {
        if (manifest is null)
            return;

        activity?.SetTag("index.commit_generation", manifest.CommitGeneration);
        activity?.SetTag("index.backup.file_count", manifest.Files.Count);
        activity?.SetTag("index.backup.byte_count", GetManifestByteCount(manifest));
    }

    private static long GetManifestByteCount(IndexBackupManifest manifest)
    {
        long total = 0;
        foreach (var file in manifest.Files)
            total += file.Length;

        return total;
    }

    private static uint ComputeFileCrc32(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Crc32.Compute(stream);
    }

    private static void PrepareDirectory(string directoryPath, bool overwrite, string description)
    {
        if (Directory.Exists(directoryPath))
        {
            if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                if (!overwrite)
                    throw new InvalidOperationException($"{description} directory '{directoryPath}' is not empty.");

                ClearDirectory(directoryPath);
            }
        }
        else
        {
            Directory.CreateDirectory(directoryPath);
        }
    }

    private static void ClearDirectory(string directoryPath)
    {
        foreach (var file in Directory.EnumerateFiles(directoryPath))
            File.Delete(file);
        foreach (var directory in Directory.EnumerateDirectories(directoryPath))
            Directory.Delete(directory, recursive: true);
    }

    private static void CopyFileAtomically(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
        IndexAtomicFileWriter.Write(targetPath, durable: true, stream =>
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            source.CopyTo(stream);
        });
    }

    private static void ValidateManifestFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("Backup manifest contains an empty file name.");
        if (Path.IsPathRooted(fileName))
            throw new InvalidDataException($"Backup manifest file name '{fileName}' is rooted.");
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            throw new InvalidDataException($"Backup manifest file name '{fileName}' is not a simple file name.");
        if (fileName.Contains("..", StringComparison.Ordinal) ||
            fileName.Contains(Path.DirectorySeparatorChar) ||
            fileName.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidDataException($"Backup manifest file name '{fileName}' is unsafe.");
    }

    private static bool SameDirectory(string left, string right)
        => string.Equals(NormaliseDirectory(left), NormaliseDirectory(right), StringComparison.OrdinalIgnoreCase);

    private static string NormaliseDirectory(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
