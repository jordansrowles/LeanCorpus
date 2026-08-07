using System.Buffers;
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
    public const string CurrentManifestFormatVersion = "2";

    private const string LegacyManifestFormatVersion = "1";

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
            int attempt = 1;
            while (true)
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
                attempt++;
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
        if (!FileOpenRetry.DirectoryExists(sourceDirectory))
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

            var requiredExtensions = segmentInfo.IsCompoundFile
                ? new[] { ".seg", ".cfs" }
                : RequiredSegmentExtensions;
            foreach (var extension in requiredExtensions)
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
            if (FileOpenRetry.FileExists(Path.Combine(sourceDirectory, statsFileName)))
                AddEntry(entries, sourceDirectory, statsFileName, null, "commit-stats", isRequired: false, isCommitFile: false);
        }

        var files = entries.Values.OrderBy(static entry => entry.FileName, StringComparer.Ordinal).ToList();
        IndexBackupManifest? parent = null;
        string? parentFingerprint = null;
        var kind = IndexBackupKind.Full;
        int chainDepth = 1;
        if (!string.IsNullOrWhiteSpace(options.PreviousBackupDirectoryPath))
        {
            var previousDirectory = Path.GetFullPath(options.PreviousBackupDirectoryPath);
            if (SameDirectory(previousDirectory, sourceDirectory))
                throw new ArgumentException("The previous backup directory must be different from the source index directory.", nameof(options));

            parent = ReadManifest(previousDirectory);
            parentFingerprint = ComputeManifestSha256(previousDirectory);
            kind = IndexBackupKind.Incremental;
            chainDepth = checked(parent.ChainDepth + 1);
            var previousByName = parent.Files.ToDictionary(static entry => entry.FileName, StringComparer.Ordinal);
            files = files.Select(entry =>
            {
                bool unchanged = previousByName.TryGetValue(entry.FileName, out var previous)
                    && previous.Length == entry.Length
                    && previous.Crc32 == entry.Crc32;
                if (!unchanged)
                    return entry;

                return new IndexBackupFileEntry
                {
                    FileName = entry.FileName,
                    Length = entry.Length,
                    Crc32 = entry.Crc32,
                    SegmentId = entry.SegmentId,
                    Role = entry.Role,
                    IsRequired = entry.IsRequired,
                    IsCommitFile = entry.IsCommitFile,
                    PresentInBackup = false
                };
            }).ToList();
        }

        return new IndexBackupManifest
        {
            FormatVersion = CurrentManifestFormatVersion,
            Kind = kind,
            ParentManifestSha256 = parentFingerprint,
            ChainDepth = chainDepth,
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
            if (!string.IsNullOrWhiteSpace(options.PreviousBackupDirectoryPath)
                && SameDirectory(Path.GetFullPath(options.PreviousBackupDirectoryPath), backupDirectory))
            {
                throw new ArgumentException(
                    "Backup directory must be different from the previous backup directory.",
                    nameof(backupDirectoryPath));
            }

            // Retry when a concurrent background merge deletes or changes a
            // segment file between manifest creation and file copy.
            const int maxAttempts = 3;
            int attempt = 1;
            bool backupDirectoryPrepared = false;
            while (true)
            {
                try
                {
                    manifest = CreateManifestCore(sourceDirectory, options);
                    if (!backupDirectoryPrepared)
                    {
                        PrepareDirectory(backupDirectory, options.OverwriteBackupDirectory, "Backup");
                        backupDirectoryPrepared = true;
                    }

                    var copiedFiles = new List<string>(manifest.Files.Count);
                    foreach (var entry in OrderForPublication(manifest.Files.Where(static entry => entry.PresentInBackup)))
                    {
                        ValidateManifestFileName(entry.FileName);
                        var sourcePath = Path.Combine(sourceDirectory, entry.FileName);
                        var targetPath = Path.Combine(backupDirectory, entry.FileName);
                        CopyFileAtomically(
                            sourcePath, targetPath, entry.Length, entry.Crc32,
                            $"Source file '{entry.FileName}' changed while the backup was being copied.",
                            syncDirectory: false);
                        copiedFiles.Add(entry.FileName);
                    }

                    var manifestJson = JsonSerializer.Serialize(manifest, LeanCorpusJsonContext.Default.IndexBackupManifest);
                    IndexAtomicFileWriter.WriteText(
                        Path.Combine(backupDirectory, ManifestFileName),
                        manifestJson,
                        durable: true,
                        syncDirectory: false);
                    DirectoryFsync.Sync(backupDirectory, strict: true);

                    result = new IndexBackupResult
                    {
                        Manifest = manifest,
                        BackupDirectoryPath = backupDirectory,
                        CopiedFiles = copiedFiles
                    };
                    succeeded = true;
                    return result;
                }
                catch (Exception ex) when (ex is FileNotFoundException
                    or DirectoryNotFoundException
                    or InvalidDataException)
                {
                    if (attempt >= maxAttempts) throw;
                    if (backupDirectoryPrepared)
                        ClearDirectory(backupDirectory);
                    Thread.Sleep(20 * attempt);
                }
                attempt++;
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
        if (!FileOpenRetry.FileExists(manifestPath))
            throw new InvalidDataException($"Backup manifest '{ManifestFileName}' was not found.");

        var json = FileOpenRetry.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.IndexBackupManifest)
            ?? throw new InvalidDataException($"Backup manifest '{ManifestFileName}' cannot be deserialised.");

        if (!string.Equals(manifest.FormatVersion, CurrentManifestFormatVersion, StringComparison.Ordinal)
            && !string.Equals(manifest.FormatVersion, LegacyManifestFormatVersion, StringComparison.Ordinal))
            throw new InvalidDataException($"Backup manifest format '{manifest.FormatVersion}' is not supported.");

        if (manifest.FormatVersion == LegacyManifestFormatVersion)
            return new IndexBackupManifest
            {
                FormatVersion = manifest.FormatVersion,
                Kind = IndexBackupKind.Full,
                ChainDepth = 1,
                CommitGeneration = manifest.CommitGeneration,
                ContentToken = manifest.ContentToken,
                CreatedAtUtc = manifest.CreatedAtUtc,
                CommitFileName = manifest.CommitFileName,
                Files = manifest.Files
            };

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
            manifest = ValidateBackupCore(backupDirectoryPath, validateChecksums: true);
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

    private static IndexBackupManifest ValidateBackupCore(
        string backupDirectoryPath,
        bool validateChecksums)
    {
        var backupDirectory = Path.GetFullPath(backupDirectoryPath);
        var manifest = ReadManifest(backupDirectory);
        foreach (var entry in manifest.Files.Where(static entry => entry.PresentInBackup))
        {
            ValidateManifestFileName(entry.FileName);
            var path = Path.Combine(backupDirectory, entry.FileName);
            if (!FileOpenRetry.FileExists(path))
                throw new InvalidDataException($"Backup file '{entry.FileName}' is missing.");

            long length = FileOpenRetry.GetFileLength(path);
            if (length != entry.Length)
                throw new InvalidDataException($"Backup file '{entry.FileName}' has length {length}, expected {entry.Length}.");

            if (validateChecksums)
            {
                var checksum = ComputeFileCrc32(path);
                if (checksum != entry.Crc32)
                    throw new InvalidDataException($"Backup file '{entry.FileName}' has CRC-32 {checksum:x8}, expected {entry.Crc32:x8}.");
            }
        }

        if (manifest.Kind == IndexBackupKind.Incremental
            && manifest.Files.Any(static entry => !entry.PresentInBackup))
        {
            throw new InvalidDataException(
                "This incremental backup is not self-contained; validate it with its full parent chain.");
        }

        return manifest;
    }

    /// <summary>
    /// Validates an ordered backup chain from oldest to newest.
    /// </summary>
    /// <param name="backupDirectoryPaths">The full, then incremental, backup directories in restore order.</param>
    /// <returns>The newest manifest in the validated chain.</returns>
    public static IndexBackupManifest ValidateBackup(IReadOnlyList<string> backupDirectoryPaths)
    {
        var chain = ReadBackupChain(backupDirectoryPaths);
        foreach (var (directory, manifest) in chain)
            ValidateManifestFiles(directory, manifest, chain, validateChecksums: true);
        return chain[^1].Manifest;
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
        string? stagingDirectory = null;
        try
        {
            var backupDirectory = Path.GetFullPath(backupDirectoryPath);
            var targetDirectory = Path.GetFullPath(targetIndexDirectoryPath);
            if (SameDirectory(backupDirectory, targetDirectory))
                throw new ArgumentException("Restore target directory must be different from the backup directory.", nameof(targetIndexDirectoryPath));

            // Validate structure and lengths before mutating the target. Checksums
            // are verified while streaming each file, before its atomic publication.
            manifest = ValidateBackupCore(backupDirectory, validateChecksums: false);
            ValidateDirectoryForRestore(targetDirectory, options.OverwriteTargetDirectory);
            stagingDirectory = CreateRestoreStagingDirectory(targetDirectory);

            var restoredFiles = new List<string>(manifest.Files.Count);
            foreach (var entry in OrderForPublication(manifest.Files))
            {
                if (!entry.PresentInBackup)
                    throw new InvalidDataException($"Incremental backup '{backupDirectory}' requires its parent chain for restore.");
                ValidateManifestFileName(entry.FileName);
                if (!options.RestoreCommitStats && string.Equals(entry.Role, "commit-stats", StringComparison.Ordinal))
                    continue;

                var sourcePath = Path.Combine(backupDirectory, entry.FileName);
                var targetPath = Path.Combine(stagingDirectory, entry.FileName);
                CopyFileAtomically(
                    sourcePath, targetPath, entry.Length, entry.Crc32,
                    $"Backup file '{entry.FileName}' is corrupt and was not published to the restore target.",
                    syncDirectory: false);

                restoredFiles.Add(entry.FileName);
            }

            DirectoryFsync.Sync(stagingDirectory, strict: true);

            IndexCheckResult? validation = null;
            if (options.ValidateAfterRestore)
            {
                using var directory = new MMapDirectory(stagingDirectory);
                validation = IndexValidator.Check(directory);
            }

            PublishRestoreDirectory(
                stagingDirectory, targetDirectory, options.OverwriteTargetDirectory);
            stagingDirectory = null;

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
        catch
        {
            DeleteRestoreStagingDirectory(stagingDirectory);
            throw;
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

    /// <summary>
    /// Restores an ordered backup chain into a target index directory.
    /// </summary>
    /// <param name="backupDirectoryPaths">The full, then incremental, backup directories in restore order.</param>
    /// <param name="targetIndexDirectoryPath">The target index directory path.</param>
    /// <param name="options">Restore options.</param>
    /// <returns>The result of restoring the newest manifest.</returns>
    public static IndexRestoreResult Restore(
        IReadOnlyList<string> backupDirectoryPaths,
        string targetIndexDirectoryPath,
        IndexRestoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(backupDirectoryPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIndexDirectoryPath);
        if (backupDirectoryPaths.Count == 0)
            throw new ArgumentException("At least one backup directory is required.", nameof(backupDirectoryPaths));

        options ??= new IndexRestoreOptions();
        var targetDirectory = Path.GetFullPath(targetIndexDirectoryPath);
        if (backupDirectoryPaths.Any(path => SameDirectory(Path.GetFullPath(path), targetDirectory)))
            throw new ArgumentException("Restore target directory must be different from every backup directory.", nameof(targetIndexDirectoryPath));

        var chain = ReadBackupChain(backupDirectoryPaths);
        var newest = chain[^1].Manifest;
        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.BackupRestore);
        var restoredFiles = new List<string>(newest.Files.Count);
        var succeeded = false;
        string? stagingDirectory = null;
        try
        {
            for (int i = 0; i < chain.Count; i++)
                ValidateManifestFiles(
                    chain[i].Directory, chain[i].Manifest, chain,
                    validateChecksums: false);

            ValidateDirectoryForRestore(targetDirectory, options.OverwriteTargetDirectory);
            stagingDirectory = CreateRestoreStagingDirectory(targetDirectory);
            foreach (var entry in OrderForPublication(newest.Files))
            {
                ValidateManifestFileName(entry.FileName);
                if (!options.RestoreCommitStats && string.Equals(entry.Role, "commit-stats", StringComparison.Ordinal))
                    continue;

                var sourceDirectory = FindFileDirectory(chain, entry);
                var sourcePath = Path.Combine(sourceDirectory, entry.FileName);
                var targetPath = Path.Combine(stagingDirectory, entry.FileName);
                CopyFileAtomically(
                    sourcePath, targetPath, entry.Length, entry.Crc32,
                    $"Backup file '{entry.FileName}' is corrupt and was not published to the restore target.",
                    syncDirectory: false);
                restoredFiles.Add(entry.FileName);
            }

            DirectoryFsync.Sync(stagingDirectory, strict: true);

            IndexCheckResult? validation = null;
            if (options.ValidateAfterRestore)
            {
                using var directory = new MMapDirectory(stagingDirectory);
                validation = IndexValidator.Check(directory);
            }

            PublishRestoreDirectory(
                stagingDirectory, targetDirectory, options.OverwriteTargetDirectory);
            stagingDirectory = null;

            succeeded = true;
            return new IndexRestoreResult
            {
                Manifest = newest,
                TargetDirectoryPath = targetDirectory,
                RestoredFiles = restoredFiles,
                ValidationResult = validation
            };
        }
        catch
        {
            DeleteRestoreStagingDirectory(stagingDirectory);
            throw;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            ApplyManifestActivityTags(activity, newest);
            activity?.SetTag("index.restore.file_count", restoredFiles.Count);
            activity?.SetTag("index.restore.chain_depth", chain.Count);
            activity?.SetTag("index.restore.validate_after_restore", options.ValidateAfterRestore);
            activity?.SetTag("index.restore.restore_commit_stats", options.RestoreCommitStats);
            activity?.SetTag("index.restore.overwrite", options.OverwriteTargetDirectory);
            LeanCorpusMaintenanceMetrics.RecordBackupRestore(sw.Elapsed, succeeded, options.ValidateAfterRestore, options.RestoreCommitStats, options.OverwriteTargetDirectory);
        }
    }

    private static List<(string Directory, IndexBackupManifest Manifest)> ReadBackupChain(IReadOnlyList<string> backupDirectoryPaths)
    {
        if (backupDirectoryPaths.Count == 0)
            throw new ArgumentException("At least one backup directory is required.", nameof(backupDirectoryPaths));

        var chain = new List<(string Directory, IndexBackupManifest Manifest)>(backupDirectoryPaths.Count);
        string? previousFingerprint = null;
        for (int i = 0; i < backupDirectoryPaths.Count; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(backupDirectoryPaths[i]);
            var directory = Path.GetFullPath(backupDirectoryPaths[i]);
            var manifest = ReadManifest(directory);
            if (i == 0)
            {
                if (manifest.Kind != IndexBackupKind.Full || manifest.ParentManifestSha256 is not null || manifest.ChainDepth != 1)
                    throw new InvalidDataException("The first backup in a chain must be a self-contained full backup.");
            }
            else
            {
                if (manifest.Kind != IndexBackupKind.Incremental
                    || !string.Equals(manifest.ParentManifestSha256, previousFingerprint, StringComparison.OrdinalIgnoreCase)
                    || manifest.ChainDepth != chain[^1].Manifest.ChainDepth + 1)
                    throw new InvalidDataException($"Backup chain link at '{directory}' does not match its immediate parent.");
            }

            chain.Add((directory, manifest));
            previousFingerprint = ComputeManifestSha256(directory);
        }

        var finalFiles = chain[^1].Manifest.Files;
        if (finalFiles.Count == 0)
            throw new InvalidDataException("The newest backup manifest contains no files.");
        return chain;
    }

    private static void ValidateManifestFiles(
        string directory,
        IndexBackupManifest manifest,
        IReadOnlyList<(string Directory, IndexBackupManifest Manifest)> chain,
        bool validateChecksums)
    {
        foreach (var entry in manifest.Files.Where(static entry => entry.PresentInBackup))
        {
            ValidateManifestFileName(entry.FileName);
            var path = Path.Combine(directory, entry.FileName);
            if (!FileOpenRetry.FileExists(path))
                throw new InvalidDataException($"Backup file '{entry.FileName}' is missing from '{directory}'.");

            long length = FileOpenRetry.GetFileLength(path);
            if (length != entry.Length)
                throw new InvalidDataException($"Backup file '{entry.FileName}' has length {length}, expected {entry.Length}.");

            if (validateChecksums)
            {
                var checksum = ComputeFileCrc32(path);
                if (checksum != entry.Crc32)
                    throw new InvalidDataException($"Backup file '{entry.FileName}' has CRC-32 {checksum:x8}, expected {entry.Crc32:x8}.");
            }
        }

        if (!ReferenceEquals(manifest, chain[^1].Manifest))
            return;

        foreach (var entry in manifest.Files)
        {
            if (FindFileDirectoryOrNull(chain, entry) is null)
                throw new InvalidDataException($"Backup chain does not contain file '{entry.FileName}'.");
        }
    }

    private static string FindFileDirectory(
        IReadOnlyList<(string Directory, IndexBackupManifest Manifest)> chain,
        IndexBackupFileEntry entry)
        => FindFileDirectoryOrNull(chain, entry)
            ?? throw new InvalidDataException($"Backup chain does not contain file '{entry.FileName}'.");

    private static string? FindFileDirectoryOrNull(
        IReadOnlyList<(string Directory, IndexBackupManifest Manifest)> chain,
        IndexBackupFileEntry entry)
    {
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var candidate = chain[i].Manifest.Files.FirstOrDefault(file =>
                file.PresentInBackup
                && string.Equals(file.FileName, entry.FileName, StringComparison.Ordinal)
                && file.Length == entry.Length
                && file.Crc32 == entry.Crc32);
            if (candidate is not null)
                return chain[i].Directory;
        }

        return null;
    }

    private static string ComputeManifestSha256(string backupDirectoryPath)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(backupDirectoryPath), ManifestFileName);
        using var stream = FileOpenRetry.OpenReadDelete(manifestPath);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
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
        foreach (var path in FileOpenRetry.EnumerateFiles(directoryPath, segmentId + ".*"))
            fileNames.Add(Path.GetFileName(path));
        foreach (var path in FileOpenRetry.EnumerateFiles(directoryPath, segmentId + "_gen_*.del"))
            fileNames.Add(Path.GetFileName(path));
        foreach (var path in FileOpenRetry.EnumerateFiles(directoryPath, segmentId + "_v_*.*"))
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
        if (!FileOpenRetry.FileExists(path))
            throw new FileNotFoundException($"Required backup file '{fileName}' was not found.", path);

        entries[fileName] = new IndexBackupFileEntry
        {
            FileName = fileName,
            Length = FileOpenRetry.GetFileLength(path),
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
            ".cfs" => "compound-segment",
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
        using var stream = FileOpenRetry.OpenReadDelete(path);
        return Crc32.Compute(stream);
    }

    private static void PrepareDirectory(string directoryPath, bool overwrite, string description)
    {
        if (FileOpenRetry.DirectoryExists(directoryPath))
        {
            if (FileOpenRetry.EnumerateFileSystemEntries(directoryPath).Any())
            {
                if (!overwrite)
                    throw new InvalidOperationException($"{description} directory '{directoryPath}' is not empty.");

                ClearDirectory(directoryPath);
            }
        }
        else
        {
            FileOpenRetry.CreateDirectory(directoryPath);
        }
    }

    private static void ClearDirectory(string directoryPath)
    {
        foreach (var file in FileOpenRetry.EnumerateFiles(directoryPath))
            FileOpenRetry.Delete(file);
        foreach (var directory in FileOpenRetry.EnumerateDirectories(directoryPath))
            FileOpenRetry.DeleteDirectory(directory, recursive: true);
    }

    private static void CopyFileAtomically(
        string sourcePath,
        string targetPath,
        long expectedLength,
        uint expectedCrc32,
        string validationError,
        bool syncDirectory)
    {
        FileOpenRetry.CreateDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
        long length = 0;
        uint crc = Crc32.Begin();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            IndexAtomicFileWriter.Write(targetPath, durable: true, syncDirectory: syncDirectory, write: stream =>
            {
                using var source = FileOpenRetry.OpenReadDelete(sourcePath);
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, read);
                    length += read;
                    crc = Crc32.Update(crc, buffer.AsSpan(0, read));
                }

                uint actualCrc32 = Crc32.Finish(crc);
                if (length != expectedLength || actualCrc32 != expectedCrc32)
                {
                    throw new InvalidDataException(
                        $"{validationError} Length {length}, expected {expectedLength}; " +
                        $"CRC-32 {actualCrc32:x8}, expected {expectedCrc32:x8}.");
                }
            });
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IEnumerable<IndexBackupFileEntry> OrderForPublication(IEnumerable<IndexBackupFileEntry> entries)
        => entries
            .OrderBy(static entry => entry.IsCommitFile ? 1 : 0)
            .ThenBy(static entry => entry.FileName, StringComparer.Ordinal);

    private static void ValidateDirectoryForRestore(string targetDirectory, bool overwrite)
    {
        if (FileOpenRetry.DirectoryExists(targetDirectory)
            && FileOpenRetry.EnumerateFileSystemEntries(targetDirectory).Any()
            && !overwrite)
        {
            throw new InvalidOperationException(
                $"Restore target directory '{targetDirectory}' is not empty.");
        }
    }

    private static string CreateRestoreStagingDirectory(string targetDirectory)
    {
        var parent = Path.GetDirectoryName(targetDirectory)
            ?? throw new InvalidOperationException("Restore target must have a parent directory.");
        FileOpenRetry.CreateDirectory(parent);
        var stagingDirectory = string.Concat(
            targetDirectory, ".restore.", Guid.NewGuid().ToString("N"), ".tmp");
        FileOpenRetry.CreateDirectory(stagingDirectory);
        return stagingDirectory;
    }

    private static void PublishRestoreDirectory(
        string stagingDirectory,
        string targetDirectory,
        bool overwrite)
    {
        PrepareDirectory(targetDirectory, overwrite, "Restore target");
        if (FileOpenRetry.DirectoryExists(targetDirectory))
            FileOpenRetry.DeleteDirectory(targetDirectory, recursive: false);

        Directory.Move(stagingDirectory, targetDirectory);
        DirectoryFsync.Sync(Path.GetDirectoryName(targetDirectory) ?? string.Empty, strict: true);
    }

    private static void DeleteRestoreStagingDirectory(string? stagingDirectory)
    {
        if (stagingDirectory is null || !FileOpenRetry.DirectoryExists(stagingDirectory))
            return;

        try
        {
            FileOpenRetry.DeleteDirectory(stagingDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            LeanCorpusActivitySource.TraceSwallowed(ex, "restore staging directory cleanup");
        }
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
