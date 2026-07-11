using Rowles.LeanCorpus.Codecs.CodecKit;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using System.Diagnostics;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;
using System.Text.Json;

namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Plans and executes LeanCorpus codec migrations.
/// </summary>
public static class IndexCodecMigrator
{
    private static readonly HashSet<string> ExecutableRewriteExtensions =
    [
        ".dic",
        ".pos",
        ".nrm",
        ".dvn",
        ".dvs",
        ".dss",
        ".dsn",
        ".dvb",
        ".fln",
        ".fdt",
        ".fdx"
    ];

    /// <summary>
    /// Builds a deterministic codec migration plan for <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The index directory.</param>
    /// <param name="options">Migration options.</param>
    /// <returns>The migration plan.</returns>
    public static IndexCodecMigrationPlan Plan(MMapDirectory directory, IndexCodecMigrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.CodecMigrationPlan);
        IndexCodecMigrationPlan? plan = null;
        var succeeded = false;
        try
        {
            plan = PlanCore(directory, options);
            succeeded = true;
            return plan;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            if (plan is not null)
            {
                activity?.SetTag("index.segment_count", plan.Inventory.Segments.Count);
                activity?.SetTag("index.migration.action_count", plan.Actions.Count);
                activity?.SetTag("index.migration.can_execute", plan.CanExecute);
                activity?.SetTag("index.issue_count", plan.Issues.Count);
            }

            LeanCorpusMaintenanceMetrics.RecordCodecMigrationPlan(sw.Elapsed, succeeded);
        }
    }

    private static IndexCodecMigrationPlan PlanCore(MMapDirectory directory, IndexCodecMigrationOptions? options)
    {
        options ??= new IndexCodecMigrationOptions();

        var inventory = IndexFormatInspector.Inspect(directory);
        return PlanCore(inventory);
    }

    internal static IndexCodecMigrationPlan Plan(IndexFormatInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        return PlanCore(inventory);
    }

    private static IndexCodecMigrationPlan PlanCore(IndexFormatInventory inventory)
    {
        var actions = new List<IndexCodecMigrationAction>();
        AddActions(inventory.Segments.SelectMany(static segment => segment.Files), actions);
        AddActions(inventory.OrphanFiles, actions);

        return new IndexCodecMigrationPlan
        {
            Inventory = inventory,
            Actions = actions,
            CanExecute = actions.All(static action => action.CanExecute),
            Issues = inventory.Issues
        };
    }

    /// <summary>
    /// Runs a codec migration or returns the dry-run plan when <see cref="IndexCodecMigrationOptions.DryRun"/> is <c>true</c>.
    /// </summary>
    /// <param name="directory">The index directory.</param>
    /// <param name="options">Migration options.</param>
    /// <returns>The migration result.</returns>
    public static IndexCodecMigrationResult Migrate(MMapDirectory directory, IndexCodecMigrationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);
        options ??= new IndexCodecMigrationOptions();

        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.CodecMigrationMigrate);
        IndexCodecMigrationResult? result = null;
        var usesStaging = false;
        try
        {
            result = MigrateCore(directory, options, out usesStaging);
            return result;
        }
        finally
        {
            sw.Stop();
            var succeeded = result?.Succeeded ?? false;
            activity?.SetTag("operation.succeeded", succeeded);
            activity?.SetTag("index.migration.dry_run", options.DryRun);
            activity?.SetTag("index.migration.action_count", result?.ExecutedActions.Count ?? 0);
            activity?.SetTag("index.migration.executed_action_count", result?.DryRun == true ? 0 : result?.ExecutedActions.Count ?? 0);
            activity?.SetTag("index.migration.succeeded", succeeded);
            activity?.SetTag("index.migration.uses_staging", usesStaging);
            activity?.SetTag("index.issue_count", result?.Issues.Count ?? 0);

            LeanCorpusMaintenanceMetrics.RecordCodecMigrationMigrate(sw.Elapsed, succeeded, options.DryRun, usesStaging);
        }
    }

    private static IndexCodecMigrationResult MigrateCore(
        MMapDirectory directory,
        IndexCodecMigrationOptions options,
        out bool usesStaging)
    {
        usesStaging = true;
        var plan = PlanCore(directory, options);
        TryRecoverInterruptedMigration(directory.DirectoryPath, plan);

        if (options.DryRun)
        {
            return new IndexCodecMigrationResult
            {
                Succeeded = plan.CanExecute,
                DryRun = true,
                SourceDirectory = directory.DirectoryPath,
                StagingDirectory = options.StagingDirectory,
                ExecutedActions = plan.Actions,
                ValidationResult = null,
                Issues = plan.Issues
            };
        }

        if (plan.Actions.Count == 0)
        {
            return new IndexCodecMigrationResult
            {
                Succeeded = true,
                DryRun = false,
                SourceDirectory = directory.DirectoryPath,
                StagingDirectory = options.StagingDirectory,
                ExecutedActions = [],
                ValidationResult = null,
                Issues = plan.Issues
            };
        }

        if (!plan.CanExecute)
        {
            var unsupportedIssues = new List<IndexCheckIssue>(plan.Issues);
            foreach (var action in plan.Actions.Where(static action => !action.CanExecute))
            {
                unsupportedIssues.Add(new IndexCheckIssue
                {
                    Severity = IndexCheckSeverity.Error,
                    Code = IndexCheckIssueCodes.UnsupportedMigrationPath,
                    Message = action.ReasonCannotExecute ?? $"Migration action for '{action.SourcePath}' is not executable.",
                    FileName = action.FileName,
                    SegmentId = action.SegmentId,
                    IsRepairable = false,
                    SuggestedActions = IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.UnsupportedMigrationPath)
                });
            }

            return new IndexCodecMigrationResult
            {
                Succeeded = false,
                DryRun = false,
                SourceDirectory = directory.DirectoryPath,
                StagingDirectory = options.StagingDirectory,
                ExecutedActions = [],
                ValidationResult = null,
                Issues = unsupportedIssues
            };
        }

        if (options.ValidateBeforeMigration)
        {
            var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
            var ignoredSegments = new HashSet<string>(
                plan.Actions
                    .Where(static action => action.SourcePath.EndsWith(".dic", StringComparison.Ordinal))
                    .Select(static action => action.SegmentId ?? string.Empty),
                StringComparer.Ordinal);
            if (HasErrors(validation, ignoredSegments))
            {
                return new IndexCodecMigrationResult
                {
                    Succeeded = false,
                    DryRun = false,
                    SourceDirectory = directory.DirectoryPath,
                    StagingDirectory = options.StagingDirectory,
                    ExecutedActions = [],
                    ValidationResult = validation,
                    Issues = validation.DetailedIssues
                };
            }
        }

        var sourceDirectory = directory.DirectoryPath;

        if (plan.Inventory.CommitGeneration is not int sourceCommitGeneration)
        {
            var issues = new List<IndexCheckIssue>(plan.Issues)
            {
                new()
                {
                    Severity = IndexCheckSeverity.Error,
                    Code = IndexCheckIssueCodes.NoCommitFile,
                    Message = "Cannot perform an atomic codec migration: no readable commit file exists.",
                    IsRepairable = false,
                    SuggestedActions = IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.NoCommitFile)
                }
            };

            return new IndexCodecMigrationResult
            {
                Succeeded = false,
                DryRun = false,
                SourceDirectory = sourceDirectory,
                StagingDirectory = options.StagingDirectory,
                ExecutedActions = [],
                ValidationResult = null,
                Issues = issues
            };
        }

        var targetDirectory = ResolveStagingDirectory(sourceDirectory, options.StagingDirectory);
        var segmentIdMap = BuildSegmentIdMap(plan.Actions, sourceCommitGeneration);
        var now = DateTimeOffset.UtcNow;
        var marker = new IndexMigrationMarker
        {
            State = IndexMigrationState.Prepared,
            SourceDirectory = sourceDirectory,
            StagingDirectory = targetDirectory,
            SourceCommitGeneration = sourceCommitGeneration,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PlannedActions = plan.Actions
        };

        var executed = new List<IndexCodecMigrationAction>();
        var currentState = marker.State;
        try
        {
            IndexMigrationRecovery.WriteMarker(sourceDirectory, marker, durable: true);
            PrepareStagingDirectory(sourceDirectory, targetDirectory);
            IndexMigrationRecovery.WriteMarker(
                sourceDirectory,
                marker with { State = IndexMigrationState.InProgress, UpdatedAtUtc = DateTimeOffset.UtcNow },
                durable: true);
            currentState = IndexMigrationState.InProgress;

            CleanupTemporaryFiles(targetDirectory);

            var rewrittenTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rewrittenStoredFieldSegments = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                ExecuteRewrite(targetDirectory, action, rewrittenStoredFieldSegments, segmentIdMap, rewrittenTargetPaths);
                executed.Add(action);
            }

            MigrateSegmentSidecars(targetDirectory, segmentIdMap, rewrittenTargetPaths);

            var newCommitGeneration = sourceCommitGeneration + 1;
            WriteMigratedCommit(targetDirectory, plan, segmentIdMap, newCommitGeneration);
            CopyMigratedStats(sourceDirectory, targetDirectory, sourceCommitGeneration, newCommitGeneration);

            IndexCheckResult? validationResult = null;
            if (options.ValidateAfterMigration)
            {
                using var target = new MMapDirectory(targetDirectory);
                validationResult = IndexValidator.Check(target, new IndexCheckOptions { Deep = true });
                if (HasErrors(validationResult))
                {
                    IndexMigrationRecovery.WriteMarker(
                        sourceDirectory,
                        marker with { State = IndexMigrationState.Failed, UpdatedAtUtc = DateTimeOffset.UtcNow },
                        durable: true);
                    return new IndexCodecMigrationResult
                    {
                        Succeeded = false,
                        DryRun = false,
                        SourceDirectory = sourceDirectory,
                        StagingDirectory = targetDirectory,
                        ExecutedActions = executed,
                        ValidationResult = validationResult,
                        Issues = validationResult.DetailedIssues
                    };
                }
            }

            PublishStagingFiles(sourceDirectory, targetDirectory);
            DirectoryFsync.Sync(sourceDirectory, strict: false);

            IndexMigrationRecovery.WriteMarker(
                sourceDirectory,
                marker with { State = IndexMigrationState.Published, UpdatedAtUtc = DateTimeOffset.UtcNow },
                durable: true);
            currentState = IndexMigrationState.Published;

            var resultIssues = new List<IndexCheckIssue>(plan.Issues);
            CleanupMigratedSourceFiles(sourceDirectory, segmentIdMap, sourceCommitGeneration, resultIssues);
            if (TryDeleteStagingDirectory(targetDirectory, out var cleanupIssue))
                resultIssues.Add(cleanupIssue);

            return new IndexCodecMigrationResult
            {
                Succeeded = true,
                DryRun = false,
                SourceDirectory = sourceDirectory,
                StagingDirectory = targetDirectory,
                ExecutedActions = executed,
                ValidationResult = validationResult,
                Issues = resultIssues
            };
        }
        catch (Exception ex) when (IsMigrationFailure(ex))
        {
            if (currentState is not IndexMigrationState.Published)
            {
                IndexMigrationRecovery.WriteMarker(
                    sourceDirectory,
                    marker with { State = IndexMigrationState.Failed, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    durable: true);
            }

            var issues = new List<IndexCheckIssue>(plan.Issues)
            {
                new()
                {
                    Severity = IndexCheckSeverity.Error,
                    Code = IndexCheckIssueCodes.UnsupportedMigrationPath,
                    Message = ex.Message,
                    IsRepairable = true,
                    SuggestedActions = IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.UnsupportedMigrationPath)
                }
            };

            return new IndexCodecMigrationResult
            {
                Succeeded = false,
                DryRun = false,
                SourceDirectory = sourceDirectory,
                StagingDirectory = targetDirectory,
                ExecutedActions = executed,
                ValidationResult = null,
                Issues = issues
            };
        }
    }

    private static void TryRecoverInterruptedMigration(string sourceDirectory, IndexCodecMigrationPlan plan)
    {
        var marker = IndexMigrationRecovery.GetState(sourceDirectory);
        if (marker.State is IndexMigrationState.None or IndexMigrationState.Published)
            return;

        if (marker.SourceCommitGeneration is int sourceGen)
        {
            var commits = IndexFileInspector.FindCommitFiles(sourceDirectory);
            var maxGen = commits.Count > 0 ? commits[0].Generation : (int?)null;
            if (maxGen > sourceGen)
            {
                IndexMigrationRecovery.WriteMarker(
                    sourceDirectory,
                    marker with { State = IndexMigrationState.Published, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    durable: true);

                if (!string.IsNullOrWhiteSpace(marker.StagingDirectory) &&
                    Directory.Exists(marker.StagingDirectory) &&
                    !PathsEqual(marker.StagingDirectory, sourceDirectory))
                {
                    TryDeleteDirectory(marker.StagingDirectory);
                }

                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(marker.StagingDirectory) &&
            Directory.Exists(marker.StagingDirectory) &&
            !PathsEqual(marker.StagingDirectory, sourceDirectory))
        {
            TryDeleteDirectory(marker.StagingDirectory);
        }

        IndexMigrationRecovery.Abandon(sourceDirectory);
    }

    private static bool TryDeleteStagingDirectory(string stagingDirectory, out IndexCheckIssue issue)
    {
        try
        {
            Directory.Delete(stagingDirectory, recursive: true);
            issue = null!;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issue = new IndexCheckIssue
            {
                Severity = IndexCheckSeverity.Warning,
                Code = IndexCheckIssueCodes.MigrationStagingCleanupFailed,
                Message = $"Migrated index was published, but staging directory '{stagingDirectory}' could not be removed: {ex.Message}",
                IsRepairable = true,
                SuggestedActions = IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.MigrationStagingCleanupFailed)
            };
            return true;
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        try { Directory.Delete(directoryPath, recursive: true); }
        catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "migrator directory delete"); }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "migrator file delete"); }
    }

    private static bool IsMigrationFailure(Exception ex)
        => ex is not OutOfMemoryException and not AccessViolationException;

    private static bool HasErrors(IndexCheckResult result)
        => result.DetailedIssues.Any(static issue => issue.Severity == IndexCheckSeverity.Error);

    private static bool HasErrors(IndexCheckResult result, HashSet<string> segmentsAwaitingTermDictionaryMigration)
        => result.DetailedIssues.Any(issue =>
            issue.Severity == IndexCheckSeverity.Error &&
            !(IsLegacyTermDictionaryReadFailure(issue) && segmentsAwaitingTermDictionaryMigration.Contains(issue.SegmentId ?? string.Empty)));

    private static bool IsLegacyTermDictionaryReadFailure(IndexCheckIssue issue)
        => (issue.Code == IndexCheckIssueCodes.PostingsReadFailure || issue.Code == IndexCheckIssueCodes.StoredFieldsReadFailure)
           && issue.Message is not null
           && issue.Message.Contains("term dictionary format", StringComparison.OrdinalIgnoreCase);

    private static string ResolveStagingDirectory(string sourceDirectory, string? requestedStagingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(requestedStagingDirectory))
            return Path.GetFullPath(requestedStagingDirectory);

        var parent = Directory.GetParent(sourceDirectory)?.FullName ?? Path.GetFullPath(sourceDirectory);
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceDirectory));
        return Path.Combine(parent, $"{directoryName}.migration-{Guid.NewGuid():N}");
    }

    private static void PrepareStagingDirectory(string sourceDirectory, string stagingDirectory)
    {
        if (Directory.Exists(stagingDirectory))
            throw new IOException($"Staging directory '{stagingDirectory}' already exists.");

        Directory.CreateDirectory(stagingDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "write.lock", StringComparison.Ordinal) ||
                string.Equals(name, IndexMigrationRecovery.MarkerFileName, StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, Path.Combine(stagingDirectory, name), overwrite: false);
        }
    }

    private static Dictionary<string, string> BuildSegmentIdMap(IReadOnlyList<IndexCodecMigrationAction> actions, int sourceCommitGeneration)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            if (action.SegmentId is null || map.ContainsKey(action.SegmentId))
                continue;

            map[action.SegmentId] = $"{action.SegmentId}_migrated_{sourceCommitGeneration}";
        }

        return map;
    }

    private static string GetTargetFileName(string sourceFileName, string? segmentId, IReadOnlyDictionary<string, string> segmentIdMap)
    {
        if (segmentId is null || !segmentIdMap.TryGetValue(segmentId, out var newSegmentId))
            return sourceFileName;

        if (!sourceFileName.StartsWith(segmentId, StringComparison.Ordinal))
            return sourceFileName;

        return newSegmentId + sourceFileName.Substring(segmentId.Length);
    }

    private static void CleanupTemporaryFiles(string directoryPath)
    {
        foreach (var tmpFile in Directory.GetFiles(directoryPath, "*.tmp"))
        {
            TryDeleteFile(tmpFile);
        }
    }

    private static void MigrateSegmentSidecars(
        string targetDirectory,
        IReadOnlyDictionary<string, string> segmentIdMap,
        HashSet<string> rewrittenTargetPaths)
    {
        foreach (var (oldSegmentId, newSegmentId) in segmentIdMap)
        {
            var oldSegPath = Path.Combine(targetDirectory, oldSegmentId + ".seg");
            if (File.Exists(oldSegPath))
            {
                var info = SegmentInfo.ReadFrom(oldSegPath);
                var newInfo = new SegmentInfo
                {
                    SegmentId = newSegmentId,
                    DocCount = info.DocCount,
                    LiveDocCount = info.LiveDocCount,
                    CommitGeneration = info.CommitGeneration,
                    FieldNames = info.FieldNames,
                    IndexSortFields = info.IndexSortFields,
                    VectorFields = info.VectorFields,
                    DelGeneration = info.DelGeneration,
                    MinSequenceNumber = info.MinSequenceNumber,
                    MaxSequenceNumber = info.MaxSequenceNumber,
                    EarliestSoftDeleteTimestamp = info.EarliestSoftDeleteTimestamp
                };
                newInfo.WriteTo(Path.Combine(targetDirectory, newSegmentId + ".seg"));
            }

            foreach (var oldFile in FindSegmentFiles(targetDirectory, oldSegmentId))
            {
                var fileName = Path.GetFileName(oldFile);
                var newFileName = newSegmentId + fileName.Substring(oldSegmentId.Length);
                var newPath = Path.Combine(targetDirectory, newFileName);

                if (File.Exists(newPath) ||
                    rewrittenTargetPaths.Contains(newPath) ||
                    fileName.EndsWith(".seg", StringComparison.Ordinal))
                {
                    TryDeleteFile(oldFile);
                }
                else
                {
                    File.Move(oldFile, newPath, overwrite: false);
                }
            }
        }
    }

    private static IEnumerable<string> FindSegmentFiles(string directoryPath, string segmentId)
    {
        foreach (var file in Directory.EnumerateFiles(directoryPath))
        {
            var name = Path.GetFileName(file);
            if (!name.StartsWith(segmentId, StringComparison.Ordinal))
                continue;

            var tail = name.Substring(segmentId.Length);
            if (tail.StartsWith(".", StringComparison.Ordinal) ||
                tail.StartsWith("_gen_", StringComparison.Ordinal) ||
                tail.StartsWith("_v_", StringComparison.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static void WriteMigratedCommit(
        string targetDirectory,
        IndexCodecMigrationPlan plan,
        IReadOnlyDictionary<string, string> segmentIdMap,
        int newGeneration)
    {
        var segmentIds = new List<string>(plan.Inventory.SegmentIds.Count);
        foreach (var segId in plan.Inventory.SegmentIds)
        {
            segmentIds.Add(segmentIdMap.TryGetValue(segId, out var newId) ? newId : segId);
        }

        var commitData = new CommitData
        {
            Segments = segmentIds,
            Generation = newGeneration,
            ContentToken = plan.Inventory.ContentToken ?? 0
        };
        var json = JsonSerializer.Serialize(commitData, LeanCorpusJsonContext.Default.CommitData);
        var content = CommitFileFormat.Wrap(json);
        var commitPath = Path.Combine(targetDirectory, $"segments_{newGeneration}");
        IndexAtomicFileWriter.WriteText(commitPath, content, durable: true);
    }

    private static void CopyMigratedStats(string sourceDirectory, string targetDirectory, int sourceGeneration, int newGeneration)
    {
        var sourceStats = Path.Combine(sourceDirectory, $"stats_{sourceGeneration}.json");
        if (!File.Exists(sourceStats))
            return;

        var targetStats = Path.Combine(targetDirectory, $"stats_{newGeneration}.json");
        IndexAtomicFileWriter.Write(targetStats, durable: true, stream =>
        {
            using var source = new FileStream(sourceStats, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            source.CopyTo(stream);
        });
    }

    private static void PublishStagingFiles(string sourceDirectory, string stagingDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(stagingDirectory))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, IndexMigrationRecovery.MarkerFileName, StringComparison.Ordinal))
                continue;

            var targetPath = Path.Combine(sourceDirectory, name);
            if (File.Exists(targetPath))
                continue;

            PublishFileAtomically(file, targetPath);
        }
    }

    private static void PublishFileAtomically(string sourcePath, string targetPath)
    {
        IndexAtomicFileWriter.Write(targetPath, durable: true, stream =>
        {
            using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            source.CopyTo(stream);
        });
    }

    private static void CleanupMigratedSourceFiles(
        string sourceDirectory,
        IReadOnlyDictionary<string, string> segmentIdMap,
        int oldGeneration,
        List<IndexCheckIssue> issues)
    {
        foreach (var oldSegmentId in segmentIdMap.Keys)
        {
            foreach (var file in FindSegmentFiles(sourceDirectory, oldSegmentId))
            {
                TryDeleteFile(file);
            }
        }

        TryDeleteFile(Path.Combine(sourceDirectory, $"segments_{oldGeneration}"));
        TryDeleteFile(Path.Combine(sourceDirectory, $"stats_{oldGeneration}.json"));
    }

    private static void ExecuteRewrite(
        string targetDirectory,
        IndexCodecMigrationAction action,
        HashSet<string> rewrittenStoredFieldSegments,
        IReadOnlyDictionary<string, string> segmentIdMap,
        HashSet<string> rewrittenTargetPaths)
    {
        if (action.Kind != IndexCodecMigrationActionKind.RewriteFile)
            return;

        var sourceFileName = action.SourcePath;
        var targetFileName = GetTargetFileName(sourceFileName, action.SegmentId, segmentIdMap);
        var sourcePath = Path.Combine(targetDirectory, sourceFileName);
        var targetPath = Path.Combine(targetDirectory, targetFileName);
        rewrittenTargetPaths.Add(targetPath);

        switch (Path.GetExtension(targetFileName))
        {
            case ".dic":
                RewriteTermDictionary(sourcePath);
                break;
            case ".pos":
                RewritePostings(targetDirectory, action, segmentIdMap);
                break;
            case ".nrm":
                RewriteNorms(sourcePath, targetPath);
                break;
            case ".dvn":
                RewriteNumericDocValues(sourcePath, targetPath);
                break;
            case ".dvs":
                RewriteSortedDocValues(sourcePath, targetPath);
                break;
            case ".dss":
                RewriteSortedSetDocValues(sourcePath, targetPath);
                break;
            case ".dsn":
                RewriteSortedNumericDocValues(sourcePath, targetPath);
                break;
            case ".dvb":
                RewriteBinaryDocValues(sourcePath, targetPath);
                break;
            case ".fln":
                RewriteFieldLengths(sourcePath, targetPath);
                break;
            case ".fdt":
            case ".fdx":
                if (action.SegmentId is not null)
                {
                    var segmentKey = segmentIdMap.TryGetValue(action.SegmentId, out var newId) ? newId : action.SegmentId;
                    if (!rewrittenStoredFieldSegments.Add(segmentKey))
                        return;
                }
                RewriteStoredFields(targetDirectory, action, segmentIdMap);
                break;
            default:
                throw new InvalidDataException($"No migration writer is registered for '{action.SourcePath}'.");
        }
    }

    private static void RewriteTermDictionary(string path)
    {
        byte version;
        using var probe = new IndexInput(path);
        try
        {
            version = CodecFileHeader.ReadVersion(probe, CodecFormats.TermDictionary);
        }
        catch (Exception ex) when (ex is InvalidDataException)
        {
            throw new InvalidDataException(
                $"Invalid term dictionary file '{path}': {ex.Message}", ex);
        }

        if (version == CodecConstants.TermDictionaryVersion) return;

        throw new InvalidDataException(
            $"Unsupported term dictionary version {version}. Only version {CodecConstants.TermDictionaryVersion} is supported. " +
            "Re-index the data with the current version of LeanCorpus.");
    }

    private static void RewritePostings(string targetDirectory, IndexCodecMigrationAction action, IReadOnlyDictionary<string, string> segmentIdMap)
    {
        if (action.SegmentId is null)
            throw new InvalidDataException($"Postings action for '{action.SourcePath}' has no segment ID.");

        var sourceSegmentId = action.SegmentId;
        if (!segmentIdMap.TryGetValue(sourceSegmentId, out var targetSegmentId))
            targetSegmentId = sourceSegmentId;

        var sourceBase = Path.Combine(targetDirectory, sourceSegmentId);
        var targetBase = Path.Combine(targetDirectory, targetSegmentId);

        var normsData = NormsReader.Read(sourceBase + ".nrm");

        var posPath = targetBase + ".pos";
        var dicPath = targetBase + ".dic";
        var temporaryPosPath = posPath + ".tmp";
        var temporaryDicPath = dicPath + ".tmp";

        var postingsOffsets = new Dictionary<string, long>(StringComparer.Ordinal);
        var termList = new List<string>();

        try
        {
            // Open input and output together so lazy enumeration can stream.
            using var dictionary = TermDictionaryReader.Open(sourceBase + ".dic");
            using var input = new IndexInput(sourceBase + ".pos");
            _ = PostingsEnum.ValidateFileHeader(input);

            using var bodyOutput = new IndexOutput(temporaryPosPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(bodyOutput, CodecConstants.PostingsVersion);
            using var blockWriter = new BlockPostingsWriter(bodyOutput);

            foreach (var (term, offset) in dictionary.EnumerateTerms())
            {
                var postings = ReadPostingRows(input, offset);
                termList.Add(term);

                bool hasFreqs = postings.Any(static p => p.Frequency != 1);
                bool hasPositions = postings.Any(static p => p.Positions.Length > 0);
                bool hasPayloads = postings.Any(static p => p.Payloads.Any(static pl => pl.Length > 0));

                string fieldName = QualifiedTermHelpers.GetFieldName(term).ToString();
                normsData.Norms.TryGetValue(fieldName, out var fieldNormBytes);

                long headerPos = bodyOutput.Position;
                postingsOffsets[term] = headerPos;
                bodyOutput.WriteInt32(0);     // docFreq placeholder
                bodyOutput.WriteInt64(0L);    // skipOffset placeholder
                bodyOutput.WriteBoolean(hasFreqs);
                bodyOutput.WriteBoolean(hasPositions);
                bodyOutput.WriteBoolean(hasPayloads);

                blockWriter.StartTerm();
                foreach (var posting in postings)
                {
                    int docId = posting.DocId;
                    byte norm = fieldNormBytes is not null && (uint)docId < (uint)fieldNormBytes.Length
                        ? fieldNormBytes[docId]
                        : (byte)0;
                    blockWriter.AddPosting(docId, hasFreqs ? posting.Frequency : 1, norm);
                }
                var metadata = blockWriter.FinishTerm();

                if (hasPositions)
                    WritePositionRows(bodyOutput, postings, hasPayloads);

                // Patch term header in place; trailer has no VarInt64, offsets are file-absolute.
                long endPos = bodyOutput.Position;
                bodyOutput.Seek(headerPos);
                bodyOutput.WriteInt32(metadata.DocFreq);
                bodyOutput.WriteInt64(metadata.SkipOffset);
                bodyOutput.Seek(endPos);
            }
            // scope.Dispose() writes 8-byte trailer here.

            // Terms are in FST sorted byte order; TermDictionaryWriter re-encodes + re-sorts.
            TermDictionaryWriter.Write(temporaryDicPath, termList, postingsOffsets);
            File.Move(temporaryPosPath, posPath, overwrite: true);
            File.Move(temporaryDicPath, dicPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPosPath);
            TryDeleteTemporaryFile(temporaryDicPath);
            throw;
        }
    }

    private static void RewriteNumericDocValues(string sourcePath, string targetPath)
    {
        // First pass: count fields, find max docCount, collect presence metadata.
        int fieldCount = 0;
        int maxDocCount = 0;
        var presenceSets = new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal);
        foreach (var (name, values, pres) in NumericDocValuesReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (values.Length > maxDocCount) maxDocCount = values.Length;
            if (pres is not null) presenceSets[name] = pres.ToHashSet();
        }

        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.NumericDocValuesVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, fieldValues, _) in NumericDocValuesReader.EnumerateFields(sourcePath))
            {
                presenceSets.TryGetValue(fieldName, out var pres);
                fieldBuf.Clear();
                NumericDocValuesWriter.WriteFieldBlock(fieldBuf, fieldName, fieldValues, maxDocCount, pres);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static void RewriteSortedDocValues(string sourcePath, string targetPath)
    {
        // First pass: count fields, find max docCount.
        int fieldCount = 0;
        int maxDocCount = 0;
        foreach (var (_, values) in SortedDocValuesReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (values.Length > maxDocCount) maxDocCount = values.Length;
        }
        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.SortedDocValuesVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, fieldValues) in SortedDocValuesReader.EnumerateFields(sourcePath))
            {
                var nullableValues = fieldValues.Select(static v => (string?)v).ToArray();
                fieldBuf.Clear();
                SortedDocValuesWriter.WriteFieldBlock(fieldBuf, fieldName, nullableValues, maxDocCount);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static void RewriteNorms(string sourcePath, string targetPath)
    {
        // First pass: count fields, find max docCount, collect boosts.
        int fieldCount = 0;
        int maxDocCount = 0;
        var boosts = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var (name, normBytes, fieldBoosts) in NormsReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (normBytes.Length > maxDocCount) maxDocCount = normBytes.Length;
            if (fieldBoosts is not null) boosts[name] = fieldBoosts;
        }
        var fieldBoostsRef = boosts.Count > 0 ? boosts : null;

        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.NormsVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, normBytes, _) in NormsReader.EnumerateFields(sourcePath))
            {
                var norms = new float[normBytes.Length];
                for (int i = 0; i < normBytes.Length; i++)
                    norms[i] = normBytes[i] / 255f;
                float[]? fb = null;
                fieldBoostsRef?.TryGetValue(fieldName, out fb);
                fieldBuf.Clear();
                NormsWriter.WriteFieldBlock(fieldBuf, fieldName, norms, fb, maxDocCount);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static void RewriteSortedSetDocValues(string sourcePath, string targetPath)
    {
        int fieldCount = 0;
        int maxDocCount = 0;
        foreach (var (_, values) in SortedSetDocValuesReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (values.Length > maxDocCount) maxDocCount = values.Length;
        }

        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.SortedSetDocValuesVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, fieldValues) in SortedSetDocValuesReader.EnumerateFields(sourcePath))
            {
                fieldBuf.Clear();
                SortedSetDocValuesWriter.WriteFieldBlock(fieldBuf, fieldName, fieldValues, maxDocCount);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static void RewriteSortedNumericDocValues(string sourcePath, string targetPath)
    {
        int fieldCount = 0;
        int maxDocCount = 0;
        foreach (var (_, values) in SortedNumericDocValuesReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (values.Length > maxDocCount) maxDocCount = values.Length;
        }

        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.SortedNumericDocValuesVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, fieldValues) in SortedNumericDocValuesReader.EnumerateFields(sourcePath))
            {
                fieldBuf.Clear();
                SortedNumericDocValuesWriter.WriteFieldBlock(fieldBuf, fieldName, fieldValues, maxDocCount);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static void RewriteBinaryDocValues(string sourcePath, string targetPath)
    {
        int fieldCount = 0;
        int maxDocCount = 0;
        foreach (var (_, values) in BinaryDocValuesReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (values.Length > maxDocCount) maxDocCount = values.Length;
        }

        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.BinaryDocValuesVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, fieldValues) in BinaryDocValuesReader.EnumerateFields(sourcePath))
            {
                fieldBuf.Clear();
                BinaryDocValuesWriter.WriteFieldBlock(fieldBuf, fieldName, fieldValues, maxDocCount);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static void RewriteFieldLengths(string sourcePath, string targetPath)
    {
        // TryRead returns null when the file does not exist; EnumerateFields would throw.
        if (!File.Exists(sourcePath))
        {
            // Nothing to rewrite; copy empty if needed.
            return;
        }

        int fieldCount = 0;
        int maxDocCount = 0;
        foreach (var (_, lengths) in FieldLengthReader.EnumerateFields(sourcePath))
        {
            fieldCount++;
            if (lengths.Length > maxDocCount) maxDocCount = lengths.Length;
        }

        var temporaryPath = targetPath + ".tmp";
        var fieldBuf = new ArrayBufferWriter<byte>(4096);
        try
        {
            using var output = new IndexOutput(temporaryPath, durable: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(output, CodecConstants.FieldLengthVersion);
            scope.Output.WriteInt32(fieldCount);
            foreach (var (fieldName, lengths) in FieldLengthReader.EnumerateFields(sourcePath))
            {
                fieldBuf.Clear();
                FieldLengthWriter.WriteFieldBlock(fieldBuf, fieldName, lengths);
                scope.Output.WriteBytes(fieldBuf.WrittenSpan);
            }
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch { TryDeleteTemporaryFile(temporaryPath); throw; }
    }

    private static List<PostingRow> ReadPostingRows(IndexInput input, long offset)
    {
        using var postings = PostingsEnum.CreateWithPositions(input, offset);
        var rows = new List<PostingRow>(postings.DocFreq);
        while (postings.MoveNext())
        {
            var positions = postings.GetCurrentPositions().ToArray();
            var payloads = new byte[positions.Length][];
            for (int i = 0; i < positions.Length; i++)
                payloads[i] = postings.GetPayload(i).ToArray();
            rows.Add(new PostingRow(postings.DocId, postings.Freq, positions, payloads));
        }

        return rows;
    }

    private static void WritePositionRows(IndexOutput output, List<PostingRow> postings, bool hasPayloads)
    {
        foreach (var posting in postings)
        {
            output.WriteVarInt(posting.Positions.Length);
            int previousPosition = 0;
            for (int i = 0; i < posting.Positions.Length; i++)
            {
                output.WriteVarInt(posting.Positions[i] - previousPosition);
                previousPosition = posting.Positions[i];

                if (hasPayloads)
                {
                    var payload = posting.Payloads[i];
                    output.WriteVarInt(payload.Length);
                    if (payload.Length > 0)
                        output.WriteBytes(payload);
                }
            }
        }
    }

    private static void RewriteStoredFields(string targetDirectory, IndexCodecMigrationAction action, IReadOnlyDictionary<string, string> segmentIdMap)
    {
        if (action.SegmentId is null)
            throw new InvalidDataException($"Stored fields action for '{action.SourcePath}' has no segment ID.");

        var sourceSegmentId = action.SegmentId;
        if (!segmentIdMap.TryGetValue(sourceSegmentId, out var targetSegmentId))
            targetSegmentId = sourceSegmentId;

        var sourceBase = Path.Combine(targetDirectory, sourceSegmentId);
        var targetBase = Path.Combine(targetDirectory, targetSegmentId);

        var info = SegmentInfo.ReadFrom(sourceBase + ".seg");
        var fdtPath = targetBase + ".fdt";
        var fdxPath = targetBase + ".fdx";
        var temporaryFdtPath = fdtPath + ".tmp";
        var temporaryFdxPath = fdxPath + ".tmp";

        try
        {
            using (var reader = StoredFieldsReader.Open(sourceBase + ".fdt", sourceBase + ".fdx"))
            {
                StoredFieldsWriter.Write(
                    temporaryFdtPath,
                    temporaryFdxPath,
                    info.DocCount,
                    reader.ReadDocumentValues,
                    compression: reader.Compression);
            }

            File.Move(temporaryFdtPath, fdtPath, overwrite: true);
            File.Move(temporaryFdxPath, fdxPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryFdtPath);
            TryDeleteTemporaryFile(temporaryFdxPath);
            throw;
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AddActions(IEnumerable<CodecFileInventory> files, List<IndexCodecMigrationAction> actions)
    {
        foreach (var file in files)
        {
            if (file.Version is null ||
                file.CurrentVersion is null ||
                file.IsCurrent ||
                !file.HasValidMagic ||
                !file.IsSupported)
            {
                continue;
            }

            bool canExecute = ExecutableRewriteExtensions.Contains(file.Extension);
            actions.Add(new IndexCodecMigrationAction
            {
                Kind = IndexCodecMigrationActionKind.RewriteFile,
                SourcePath = file.FileName,
                TargetPath = null,
                Description = $"Rewrite {file.FileName} from {file.CodecName} v{file.Version} to v{file.CurrentVersion}.",
                CanExecute = canExecute,
                ReasonCannotExecute = canExecute ? null : $"No migration writer is registered for {file.CodecName}.",
                SegmentId = file.SegmentId,
                FileName = file.FileName,
                FromVersion = file.Version,
                ToVersion = file.CurrentVersion
            });
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record PostingRow(int DocId, int Frequency, int[] Positions, byte[][] Payloads);
}
