using Rowles.LeanCorpus.Codecs.CodecKit;
using System.IO;
using System.Diagnostics;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.Bkd;
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
    private delegate void BuiltInMigrationWriter(MigrationRewriteContext context);

    // This is intentionally keyed by stable catalogue identifiers, never by physical
    // extensions. The catalogue remains the sole authority for file recognition and
    // migration behaviour; this table connects established normal writers to their
    // logical persistent role until each writer supplies a descriptor handler directly.
    private static readonly IReadOnlyDictionary<string, BuiltInMigrationWriter> BuiltInMigrationWriters =
        new Dictionary<string, BuiltInMigrationWriter>(StringComparer.Ordinal)
        {
            ["leancorpus.term-dictionary.data"] = static context => RewriteTermDictionary(context.SourcePath, context.TargetPath),
            ["leancorpus.postings.data"] = static context => RewritePostings(context.TargetDirectory, context.Action, context.SegmentIdMap, context.Catalog),
            ["leancorpus.norms.data"] = static context => RewriteNorms(context.SourcePath, context.TargetPath),
            ["leancorpus.field-lengths.data"] = static context => RewriteFieldLengths(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.numeric"] = static context => RewriteNumericDocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.sorted"] = static context => RewriteSortedDocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.sorted-set"] = static context => RewriteSortedSetDocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.sorted-numeric"] = static context => RewriteSortedNumericDocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.binary"] = static context => RewriteBinaryDocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.int64"] = static context => RewriteInt64DocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.doc-values.int64-sorted-numeric"] = static context => RewriteInt64SortedNumericDocValues(context.SourcePath, context.TargetPath),
            ["leancorpus.numeric-structures.bkd"] = static context => RewriteBkd(context.SourcePath, context.TargetPath),
            ["leancorpus.numeric-structures.int64-bkd"] = static context => RewriteInt64Bkd(context.SourcePath, context.TargetPath),
            ["leancorpus.numeric-structures.numeric-index"] = static context => RewriteNumericIndex(context.SourcePath, context.TargetPath),
            ["leancorpus.numeric-structures.int64-numeric-index"] = static context => RewriteInt64NumericIndex(context.SourcePath, context.TargetPath),
            ["leancorpus.deletes.parent-bitset"] = static context => RewriteParentBitSet(context.SourcePath, context.TargetPath),
            ["leancorpus.stored-fields.data"] = static context => RewriteStoredFields(context.TargetDirectory, context.Action, context.SegmentIdMap),
            ["leancorpus.term-vectors.data"] = static context => RewriteTermVectors(context.TargetDirectory, context.Action, context.SegmentIdMap),
        };

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

        var catalog = options.Catalog ?? throw new ArgumentException("The codec catalogue cannot be null.", nameof(options));
        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions { Catalog = catalog });
        return PlanCore(inventory, catalog);
    }

    internal static IndexCodecMigrationPlan Plan(IndexFormatInventory inventory)
        => Plan(inventory, CodecCatalog.Default);

    internal static IndexCodecMigrationPlan Plan(IndexFormatInventory inventory, CodecCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(catalog);

        return PlanCore(inventory, catalog);
    }

    private static IndexCodecMigrationPlan PlanCore(IndexFormatInventory inventory, CodecCatalog catalog)
    {
        var actions = new List<IndexCodecMigrationAction>();
        AddActions(inventory.Segments.SelectMany(static segment => segment.Files), catalog, actions);
        AddActions(inventory.OrphanFiles, catalog, actions);

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
            var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true, Catalog = options.Catalog });
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
            MaterialiseCompoundMembers(targetDirectory, plan.Actions);

            var rewrittenTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rewrittenCoordinatedFamilies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                ExecuteRewrite(
                    targetDirectory,
                    action,
                    options.Catalog,
                    rewrittenCoordinatedFamilies,
                    segmentIdMap,
                    rewrittenTargetPaths);
                executed.Add(action);
            }

            MigrateSegmentSidecars(targetDirectory, segmentIdMap, rewrittenTargetPaths);
            RepackMigratedCompoundSegments(targetDirectory, plan.Actions, segmentIdMap);

            var newCommitGeneration = sourceCommitGeneration + 1;
            WriteMigratedCommit(targetDirectory, plan, segmentIdMap, newCommitGeneration);
            CopyMigratedStats(sourceDirectory, targetDirectory, sourceCommitGeneration, newCommitGeneration);

            IndexCheckResult? validationResult = null;
            if (options.ValidateAfterMigration)
            {
                using var target = new MMapDirectory(targetDirectory);
                validationResult = IndexValidator.Check(target, new IndexCheckOptions { Deep = true, Catalog = options.Catalog });
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
                    FileOpenRetry.DirectoryExists(marker.StagingDirectory) &&
                    !PathsEqual(marker.StagingDirectory, sourceDirectory))
                {
                    TryDeleteDirectory(marker.StagingDirectory);
                }

                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(marker.StagingDirectory) &&
            FileOpenRetry.DirectoryExists(marker.StagingDirectory) &&
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
            FileOpenRetry.DeleteDirectory(stagingDirectory, recursive: true);
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
        try { FileOpenRetry.DeleteDirectory(directoryPath, recursive: true); }
        catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "migrator directory delete"); }
    }

    private static void TryDeleteFile(string path)
    {
        try { FileOpenRetry.Delete(path); }
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

        var parent = FileOpenRetry.GetParentDirectory(sourceDirectory) ?? Path.GetFullPath(sourceDirectory);
        var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceDirectory));
        return Path.Combine(parent, $"{directoryName}.migration-{Guid.NewGuid():N}");
    }

    private static void PrepareStagingDirectory(string sourceDirectory, string stagingDirectory)
    {
        if (FileOpenRetry.DirectoryExists(stagingDirectory))
            throw new IOException($"Staging directory '{stagingDirectory}' already exists.");

        FileOpenRetry.CreateDirectory(stagingDirectory);
        foreach (var file in FileOpenRetry.EnumerateFiles(sourceDirectory, "*"))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "write.lock", StringComparison.Ordinal) ||
                string.Equals(name, IndexMigrationRecovery.MarkerFileName, StringComparison.Ordinal))
            {
                continue;
            }

            FileOpenRetry.Copy(file, Path.Combine(stagingDirectory, name), overwrite: false);
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
        foreach (var tmpFile in FileOpenRetry.GetFiles(directoryPath, "*.tmp"))
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
            if (FileOpenRetry.FileExists(oldSegPath))
            {
                var info = SegmentInfo.ReadFrom(oldSegPath);
                var newInfo = new SegmentInfo
                {
                    SegmentId = newSegmentId,
                    DocCount = info.DocCount,
                    LiveDocCount = info.LiveDocCount,
                    TotalBytes = info.TotalBytes,
                    CodecBytes = new Dictionary<string, long>(info.CodecBytes, StringComparer.Ordinal),
                    CommitGeneration = info.CommitGeneration,
                    IsCompoundFile = info.IsCompoundFile,
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

                if (FileOpenRetry.FileExists(newPath) ||
                    rewrittenTargetPaths.Contains(newPath) ||
                    fileName.EndsWith(".seg", StringComparison.Ordinal))
                {
                    TryDeleteFile(oldFile);
                }
                else
                {
                    FileOpenRetry.Move(oldFile, newPath, overwrite: false);
                }
            }
        }
    }

    private static void MaterialiseCompoundMembers(string targetDirectory, IReadOnlyList<IndexCodecMigrationAction> actions)
    {
        var compoundFiles = actions
            .Where(static action => !string.IsNullOrEmpty(action.CompoundFileName))
            .Select(static action => action.CompoundFileName!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (compoundFiles.Length == 0)
            return;

        using var directory = new MMapDirectory(targetDirectory);
        foreach (var compoundFileName in compoundFiles)
        {
            using var compound = CompoundFileReader.Open(directory, compoundFileName);
            foreach (var memberName in compound.FileNames)
            {
                var destination = Path.Combine(targetDirectory, memberName);
                if (FileOpenRetry.FileExists(destination))
                    continue;

                var temporary = destination + ".tmp";
                try
                {
                    using var input = compound.OpenInput(directory, memberName);
                    using (var output = new IndexOutput(temporary, durable: true))
                    {
                        long remaining = input.Length;
                        while (remaining > 0)
                        {
                            int count = (int)Math.Min(64 * 1024, remaining);
                            output.WriteBytes(input.ReadSpan(count));
                            remaining -= count;
                        }
                    }
                    FileOpenRetry.Move(temporary, destination, overwrite: false);
                }
                catch
                {
                    TryDeleteTemporaryFile(temporary);
                    throw;
                }
            }
        }
    }

    private static void RepackMigratedCompoundSegments(
        string targetDirectory,
        IReadOnlyList<IndexCodecMigrationAction> actions,
        IReadOnlyDictionary<string, string> segmentIdMap)
    {
        foreach (var sourceSegmentId in actions
                     .Where(static action => !string.IsNullOrEmpty(action.CompoundFileName) && action.SegmentId is not null)
                     .Select(static action => action.SegmentId!)
                     .Distinct(StringComparer.Ordinal))
        {
            var targetSegmentId = segmentIdMap.TryGetValue(sourceSegmentId, out var mapped) ? mapped : sourceSegmentId;
            _ = CompoundFileWriter.Pack(targetDirectory, targetSegmentId);
        }
    }

    private static IEnumerable<string> FindSegmentFiles(string directoryPath, string segmentId)
    {
        foreach (var file in FileOpenRetry.EnumerateFiles(directoryPath, "*"))
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
        if (!FileOpenRetry.FileExists(sourceStats))
            return;

        var targetStats = Path.Combine(targetDirectory, $"stats_{newGeneration}.json");
        IndexAtomicFileWriter.Write(targetStats, durable: true, stream =>
        {
            using var source = FileOpenRetry.OpenReadDelete(sourceStats);
            source.CopyTo(stream);
        });
    }

    private static void PublishStagingFiles(string sourceDirectory, string stagingDirectory)
    {
        // Collect staging file names, excluding the recovery marker.
        var stagingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in FileOpenRetry.EnumerateFiles(stagingDirectory, "*"))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, IndexMigrationRecovery.MarkerFileName, StringComparison.Ordinal))
                continue;
            stagingFiles.Add(name);
        }

        // Delete source files absent from staging (preserve write.lock and marker).
        foreach (var file in FileOpenRetry.EnumerateFiles(sourceDirectory, "*"))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "write.lock", StringComparison.Ordinal) ||
                string.Equals(name, IndexMigrationRecovery.MarkerFileName, StringComparison.Ordinal))
                continue;
            if (!stagingFiles.Contains(name))
                TryDeleteFile(file);
        }

        // Copy all staging files to source, overwriting when content differs.
        foreach (var name in stagingFiles)
        {
            PublishFileAtomically(Path.Combine(stagingDirectory, name), Path.Combine(sourceDirectory, name));
        }
    }

    private static void PublishFileAtomically(string sourcePath, string targetPath)
    {
        IndexAtomicFileWriter.Write(targetPath, durable: true, stream =>
        {
            using var source = FileOpenRetry.OpenReadDelete(sourcePath);
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
        CodecCatalog catalog,
        HashSet<string> rewrittenCoordinatedFamilies,
        IReadOnlyDictionary<string, string> segmentIdMap,
        HashSet<string> rewrittenTargetPaths)
    {
        if (action.Kind != IndexCodecMigrationActionKind.RewriteFile)
            return;

        if (action.FormatId is null || !catalog.TryGetFile(action.FormatId, out var descriptor) || descriptor is null)
            throw new InvalidDataException($"Migration action for '{action.SourcePath}' has no registered codec descriptor.");

        var sourceFileName = action.SourcePath;
        var targetFileName = GetTargetFileName(sourceFileName, action.SegmentId, segmentIdMap);
        var sourcePath = Path.Combine(targetDirectory, sourceFileName);
        var targetPath = Path.Combine(targetDirectory, targetFileName);
        rewrittenTargetPaths.Add(targetPath);

        var behaviour = GetMigrationBehaviour(descriptor, action.FromVersion);
        if (behaviour == CodecMigrationBehaviour.CoordinatedRewrite && action.SegmentId is not null)
        {
            var segmentKey = segmentIdMap.TryGetValue(action.SegmentId, out var newId) ? newId : action.SegmentId;
            if (!rewrittenCoordinatedFamilies.Add($"{descriptor.FamilyId}:{segmentKey}"))
                return;
        }

        var context = new MigrationRewriteContext(
            targetDirectory,
            sourcePath,
            targetPath,
            action,
            descriptor,
            catalog,
            segmentIdMap,
            rewrittenTargetPaths);
        if (behaviour == CodecMigrationBehaviour.CoordinatedRewrite &&
            catalog.TryGetFamily(descriptor.FamilyId, out var family) &&
            family?.MigrationCoordinator is not null)
        {
            RewriteWithFamilyCoordinator(context, family);
            return;
        }

        if (descriptor.MigrationHandler is not null)
        {
            RewriteWithDescriptorHandler(context);
            return;
        }

        if (behaviour == CodecMigrationBehaviour.Reframe)
        {
            ReframeCodecFile(context.SourcePath, context.TargetPath, context.Descriptor);
            return;
        }

        if (BuiltInMigrationWriters.TryGetValue(descriptor.FormatId, out var writer))
        {
            writer(context);
            return;
        }

        throw new InvalidDataException($"No migration writer is registered for codec format '{descriptor.FormatId}'.");
    }

    private static void RewriteTermDictionary(string sourcePath, string targetPath)
    {
        List<(string Term, long Offset)> allTerms;
        using (var reader = TermDictionaryReader.Open(sourcePath))
            allTerms = reader.EnumerateAllTerms();

        var offsets = new Dictionary<string, long>(allTerms.Count, StringComparer.Ordinal);
        foreach (var (term, offset) in allTerms)
            offsets[term] = offset;

        var sorted = new List<string>(offsets.Keys);
        sorted.Sort(StringComparer.Ordinal);
        try
        {
            TermDictionaryWriter.Write(targetPath, sorted, offsets, durable: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            throw new InvalidDataException($"Cannot rewrite term dictionary '{sourcePath}': {ex.Message}", ex);
        }
    }

    private static void RewritePostings(string targetDirectory, IndexCodecMigrationAction action, IReadOnlyDictionary<string, string> segmentIdMap, CodecCatalog catalog)
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
            using (var dictionary = TermDictionaryReader.Open(sourceBase + ".dic"))
            using (var input = new IndexInput(sourceBase + ".pos"))
            {
                _ = PostingsEnum.ValidateFileHeader(input);

                using var output = new IndexOutput(temporaryPosPath, durable: true);
                var descriptor = catalog.GetFile("leancorpus.postings.data");
                using var frame = CodecFileWriter.Begin(output, descriptor);
                var bodyOutput = frame.Output;
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

                    long bodyOffset = bodyOutput.Position;
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

                    long metadataOffset = bodyOutput.Position;
                    postingsOffsets[term] = metadataOffset;
                    bodyOutput.WriteInt64(bodyOffset);
                    bodyOutput.WriteInt32(metadata.DocFreq);
                    bodyOutput.WriteInt64(metadata.SkipOffset);
                    bodyOutput.WriteBoolean(hasFreqs);
                    bodyOutput.WriteBoolean(hasPositions);
                    bodyOutput.WriteBoolean(hasPayloads);
                }
                frame.Complete();
            }
            // dictionary and input are now disposed, MMF handles released.

            // Terms are in FST sorted byte order; TermDictionaryWriter re-encodes + re-sorts.
            TermDictionaryWriter.Write(temporaryDicPath, termList, postingsOffsets);
            FileOpenRetry.Move(temporaryPosPath, posPath, overwrite: true);
            FileOpenRetry.Move(temporaryDicPath, dicPath, overwrite: true);
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
        // Single pass: enumerate once into memory, so the MMF handle releases
        // before the Move. Two-pass enumeration opens IndexInput twice on the
        // same file, causing LLIDX040 on Windows.
        var allFields = NumericDocValuesReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        int maxDocCount = 0;
        foreach (var (_, values, _) in allFields)
            if (values.Length > maxDocCount) maxDocCount = values.Length;

        var fields = new Dictionary<string, double[]>(allFields.Count, StringComparer.Ordinal);
        var presence = new Dictionary<string, IReadOnlySet<int>>(allFields.Count, StringComparer.Ordinal);
        foreach (var (fieldName, fieldValues, fieldPresence) in allFields)
        {
            fields.Add(fieldName, fieldValues);
            if (fieldPresence is not null)
                presence.Add(fieldName, fieldPresence.ToHashSet());
        }

        NumericDocValuesWriter.Write(targetPath, fields, maxDocCount, presence, durable: true);
    }

    private static void RewriteSortedDocValues(string sourcePath, string targetPath)
    {
        var allFields = SortedDocValuesReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        int maxDocCount = 0;
        foreach (var (_, values) in allFields)
            if (values.Length > maxDocCount) maxDocCount = values.Length;

        var fields = allFields.ToDictionary(
            static field => field.Name,
            static field => field.Values,
            StringComparer.Ordinal);
        SortedDocValuesWriter.Write(targetPath, fields, maxDocCount, durable: true);
    }

    private static void RewriteNorms(string sourcePath, string targetPath)
    {
        // Single pass: enumerate once into memory, so the MMF handle releases
        // before the Move. Two-pass enumeration opens IndexInput twice on the
        // same file, causing LLIDX040 on Windows.
        var allFields = NormsReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        int maxDocCount = 0;
        foreach (var (_, normBytes, _) in allFields)
            if (normBytes.Length > maxDocCount) maxDocCount = normBytes.Length;

        var fieldNorms = new Dictionary<string, float[]>(allFields.Count, StringComparer.Ordinal);
        var fieldBoosts = new Dictionary<string, float[]>(allFields.Count, StringComparer.Ordinal);
        foreach (var (fieldName, normBytes, boosts) in allFields)
        {
            var norms = new float[normBytes.Length];
            for (int i = 0; i < normBytes.Length; i++)
                norms[i] = normBytes[i] / 255f;
            fieldNorms.Add(fieldName, norms);
            if (boosts is not null)
                fieldBoosts.Add(fieldName, boosts);
        }

        NormsWriter.Write(
            targetPath,
            fieldNorms,
            fieldBoosts.Count == 0 ? null : fieldBoosts,
            docCount: maxDocCount,
            durable: true);
    }

    private static void RewriteSortedSetDocValues(string sourcePath, string targetPath)
    {
        var allFields = SortedSetDocValuesReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        int maxDocCount = 0;
        foreach (var (_, values) in allFields)
            if (values.Length > maxDocCount) maxDocCount = values.Length;

        var fields = allFields.ToDictionary(
            static field => field.Name,
            static field => field.Values,
            StringComparer.Ordinal);
        SortedSetDocValuesWriter.Write(targetPath, fields, maxDocCount, durable: true);
    }

    private static void RewriteSortedNumericDocValues(string sourcePath, string targetPath)
    {
        var allFields = SortedNumericDocValuesReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        int maxDocCount = 0;
        foreach (var (_, values) in allFields)
            if (values.Length > maxDocCount) maxDocCount = values.Length;

        var fields = allFields.ToDictionary(
            static field => field.Name,
            static field => field.Values,
            StringComparer.Ordinal);
        SortedNumericDocValuesWriter.Write(targetPath, fields, maxDocCount, durable: true);
    }

    private static void RewriteBinaryDocValues(string sourcePath, string targetPath)
    {
        var allFields = BinaryDocValuesReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        int maxDocCount = 0;
        foreach (var (_, values) in allFields)
            if (values.Length > maxDocCount) maxDocCount = values.Length;

        var fields = allFields.ToDictionary(
            static field => field.Name,
            static field => field.Values,
            StringComparer.Ordinal);
        BinaryDocValuesWriter.Write(targetPath, fields, maxDocCount, durable: true);
    }

    private static void RewriteInt64DocValues(string sourcePath, string targetPath)
    {
        var (fields, bitmaps) = Int64DocValuesReader.Read(sourcePath);
        if (fields.Count == 0)
            return;

        int maxDocCount = fields.Values.Max(static values => values.Length);
        var presence = new Dictionary<string, IReadOnlySet<int>>(bitmaps.Count, StringComparer.Ordinal);
        foreach (var (fieldName, bitmap) in bitmaps)
        {
            if (bitmap is not null)
                presence.Add(fieldName, bitmap.ToHashSet());
        }

        Int64DocValuesWriter.Write(targetPath, fields, maxDocCount, presence, durable: true);
    }

    private static void RewriteInt64SortedNumericDocValues(string sourcePath, string targetPath)
    {
        var values = Int64SortedNumericDocValuesReader.Read(sourcePath);
        if (values.Count == 0)
            return;

        int maxDocCount = values.Values.Max(static fieldValues => fieldValues.Length);
        var fields = values.ToDictionary(
            static field => field.Key,
            static field => field.Value.Select(static docValues => (IReadOnlyList<long>?)docValues).ToArray(),
            StringComparer.Ordinal);
        Int64SortedNumericDocValuesWriter.Write(targetPath, fields, maxDocCount, durable: true);
    }

    private static void ReframeCodecFile(
        string sourcePath,
        string targetPath,
        CodecFileDescriptor descriptor)
    {
        string temporaryPath = targetPath + ".tmp";
        try
        {
            using (var input = new IndexInput(sourcePath))
            using (var frame = CodecFileReader.OpenSupported(input, descriptor))
            {
                CodecFileWriter.WriteAtomically(temporaryPath, descriptor, durable: true, bodyOutput =>
                {
                    long remaining = frame.BodyLength;
                    while (remaining > 0)
                    {
                        int count = (int)Math.Min(64 * 1024, remaining);
                        bodyOutput.WriteBytes(input.ReadSpan(count));
                        remaining -= count;
                    }
                });
            }

            FileOpenRetry.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void RewriteFieldLengths(string sourcePath, string targetPath)
    {
        if (!FileOpenRetry.FileExists(sourcePath))
            return;

        var allFields = FieldLengthReader.EnumerateFields(sourcePath);
        if (allFields.Count == 0)
            return;

        var fields = new Dictionary<string, int[]>(allFields.Count, StringComparer.Ordinal);
        foreach (var (fieldName, lengths) in allFields)
            fields.Add(fieldName, lengths);
        FieldLengthWriter.Write(targetPath, fields, durable: true);
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

    private static void WritePositionRows(ISequentialIndexOutput output, List<PostingRow> postings, bool hasPayloads)
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

            FileOpenRetry.Move(temporaryFdtPath, fdtPath, overwrite: true);
            FileOpenRetry.Move(temporaryFdxPath, fdxPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryFdtPath);
            TryDeleteTemporaryFile(temporaryFdxPath);
            throw;
        }
    }

    private static void RewriteTermVectors(string targetDirectory, IndexCodecMigrationAction action, IReadOnlyDictionary<string, string> segmentIdMap)
    {
        if (action.SegmentId is null)
            throw new InvalidDataException($"Term vectors action for '{action.SourcePath}' has no segment ID.");

        var sourceSegmentId = action.SegmentId;
        if (!segmentIdMap.TryGetValue(sourceSegmentId, out var targetSegmentId))
            targetSegmentId = sourceSegmentId;

        var sourceBase = Path.Combine(targetDirectory, sourceSegmentId);
        var targetBase = Path.Combine(targetDirectory, targetSegmentId);
        var tvdPath = targetBase + ".tvd";
        var tvxPath = targetBase + ".tvx";
        var temporaryTvdPath = tvdPath + ".tmp";
        var temporaryTvxPath = tvxPath + ".tmp";

        try
        {
            using (var reader = TermVectorsReader.Open(sourceBase + ".tvd", sourceBase + ".tvx"))
            using (var writer = new TermVectorsStreamWriter(temporaryTvdPath, temporaryTvxPath))
            {
                for (int docId = 0; docId < reader.DocCount; docId++)
                    writer.AddDocument(reader.GetTermVector(docId));
            }

            FileOpenRetry.Move(temporaryTvdPath, tvdPath, overwrite: true);
            FileOpenRetry.Move(temporaryTvxPath, tvxPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryTvdPath);
            TryDeleteTemporaryFile(temporaryTvxPath);
            throw;
        }
    }

    private static void RewriteBkd(string sourcePath, string targetPath)
    {
        var fields = new Dictionary<string, List<(double Value, int DocId)>>(StringComparer.Ordinal);
        using (var reader = BKDReader.Open(sourcePath))
        {
            foreach (string field in reader.FieldNames)
            {
                var points = new List<(double Value, int DocId)>();
                reader.VisitRange(field, double.NegativeInfinity, double.PositiveInfinity,
                    (docId, value) => points.Add((value, docId)));
                fields.Add(field, points);
            }
        }

        string temporaryPath = targetPath + ".tmp";
        try
        {
            BKDWriter.Write(temporaryPath, fields);
            FileOpenRetry.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void RewriteInt64Bkd(string sourcePath, string targetPath)
    {
        var fields = new Dictionary<string, List<(long Value, int DocId)>>(StringComparer.Ordinal);
        using (var reader = Int64BKDReader.Open(sourcePath))
        {
            foreach (string field in reader.FieldNames)
            {
                var points = new List<(long Value, int DocId)>();
                reader.VisitRange(field, long.MinValue, long.MaxValue,
                    (docId, value) => points.Add((value, docId)));
                fields.Add(field, points);
            }
        }

        string temporaryPath = targetPath + ".tmp";
        try
        {
            Int64BKDWriter.Write(temporaryPath, fields);
            FileOpenRetry.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void RewriteNumericIndex(string sourcePath, string targetPath)
    {
        Dictionary<string, Dictionary<int, double>> fields;
        using (var input = new IndexInput(sourcePath))
            fields = NumericIndexCodec.ReadDouble(input);
        RewriteSidecar(targetPath, temporaryPath => NumericIndexCodec.WriteDouble(temporaryPath, fields));
    }

    private static void RewriteInt64NumericIndex(string sourcePath, string targetPath)
    {
        Dictionary<string, Dictionary<int, long>> fields;
        using (var input = new IndexInput(sourcePath))
            fields = NumericIndexCodec.ReadInt64(input);
        RewriteSidecar(targetPath, temporaryPath => NumericIndexCodec.WriteInt64(temporaryPath, fields));
    }

    private static void RewriteParentBitSet(string sourcePath, string targetPath)
    {
        var bitSet = ParentBitSet.ReadFrom(sourcePath);
        RewriteSidecar(targetPath, bitSet.WriteTo);
    }

    private static void RewriteSidecar(string targetPath, Action<string> write)
    {
        string temporaryPath = targetPath + ".tmp";
        try
        {
            write(temporaryPath);
            FileOpenRetry.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }


    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            FileOpenRetry.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AddActions(IEnumerable<CodecFileInventory> inventoryFiles, CodecCatalog catalog, List<IndexCodecMigrationAction> actions)
    {
        var files = inventoryFiles.ToArray();
        var coordinated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (file.FormatId is null ||
                file.FormatVersion is null ||
                file.CurrentFormatVersion is null ||
                file.IsCurrent ||
                !file.HasValidMagic ||
                !file.IsSupported ||
                !catalog.TryGetFile(file.FormatId, out var descriptor) || descriptor is null)
            {
                continue;
            }

            var behaviour = GetMigrationBehaviour(descriptor, file.FormatVersion);
            if (behaviour == CodecMigrationBehaviour.None)
                continue;

            var actionFiles = new[] { file };
            if (behaviour == CodecMigrationBehaviour.CoordinatedRewrite)
            {
                var key = $"{descriptor.FamilyId}:{file.SegmentId}:{file.CompoundFileName}";
                if (!coordinated.Add(key))
                    continue;

                actionFiles = files
                    .Where(candidate => candidate.FamilyId == descriptor.FamilyId &&
                                        candidate.SegmentId == file.SegmentId &&
                                        candidate.CompoundFileName == file.CompoundFileName)
                    .ToArray();
            }

            bool hasFamilyCoordinator = catalog.TryGetFamily(descriptor.FamilyId, out var family) &&
                                        family?.MigrationCoordinator is not null;
            bool canExecute = behaviour is not CodecMigrationBehaviour.Unsupported
                && (behaviour == CodecMigrationBehaviour.Reframe ||
                    descriptor.MigrationHandler is not null ||
                    hasFamilyCoordinator ||
                    BuiltInMigrationWriters.ContainsKey(descriptor.FormatId));
            string subject = behaviour == CodecMigrationBehaviour.CoordinatedRewrite
                ? $"{descriptor.FamilyId} files for segment '{file.SegmentId}'"
                : file.FileName;
            actions.Add(new IndexCodecMigrationAction
            {
                Kind = IndexCodecMigrationActionKind.RewriteFile,
                SourcePath = file.FileName,
                SourcePaths = actionFiles.Select(static candidate => candidate.FileName).ToArray(),
                TargetPath = null,
                Description = $"Rewrite {subject} from {file.CodecName} v{file.FormatVersion} to v{file.CurrentFormatVersion}.",
                CanExecute = canExecute,
                ReasonCannotExecute = canExecute ? null : $"No migration writer is registered for codec format '{descriptor.FormatId}'.",
                SegmentId = file.SegmentId,
                FileName = file.FileName,
                FormatId = descriptor.FormatId,
                FamilyId = descriptor.FamilyId,
                CompoundFileName = file.CompoundFileName,
                FromVersion = file.Version,
                ToVersion = file.CurrentVersion
            });
        }
    }

    private static CodecMigrationBehaviour GetMigrationBehaviour(CodecFileDescriptor descriptor, int? version)
    {
        if (version is int concreteVersion)
        {
            foreach (var supported in descriptor.SupportedVersions)
            {
                if (supported.Version == concreteVersion)
                    return supported.MigrationBehaviour;
            }
        }

        return descriptor.MigrationBehaviour;
    }

    private static void RewriteWithDescriptorHandler(MigrationRewriteContext context)
    {
        var temporaryBodyPath = context.TargetPath + ".body.tmp";
        try
        {
            using (var source = new IndexInput(context.SourcePath))
            using (var frame = CodecFileReader.OpenSupported(source, context.Descriptor))
            using (var body = new IndexInput(context.SourcePath, frame.BodyStart, frame.BodyLength))
            using (var targetBody = new IndexOutput(temporaryBodyPath, durable: true))
            {
                context.Descriptor.MigrationHandler!.Migrate(body, targetBody);
            }

            CodecFileWriter.WriteAtomically(context.TargetPath, context.Descriptor, durable: true, target =>
            {
                using var body = new IndexInput(temporaryBodyPath);
                long remaining = body.Length;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(64 * 1024, remaining);
                    target.WriteBytes(body.ReadSpan(count));
                    remaining -= count;
                }
            });
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryBodyPath);
        }
    }

    private static void RewriteWithFamilyCoordinator(
        MigrationRewriteContext context,
        CodecFamilyDescriptor family)
    {
        var sourceBodies = new Dictionary<string, IndexInput>(StringComparer.Ordinal);
        var targetBodies = new Dictionary<string, IndexOutput>(StringComparer.Ordinal);
        var targetFiles = new Dictionary<string, (string Path, string BodyPath, CodecFileDescriptor Descriptor)>(StringComparer.Ordinal);
        try
        {
            foreach (var sourceFileName in context.Action.SourcePaths)
            {
                if (!context.Catalog.TryMatchFile(sourceFileName, out var descriptor) ||
                    descriptor is null ||
                    descriptor.FamilyId != family.FamilyId)
                {
                    throw new InvalidDataException(
                        $"Coordinated migration for family '{family.FamilyId}' cannot resolve source file '{sourceFileName}'.");
                }

                var sourcePath = Path.Combine(context.TargetDirectory, sourceFileName);
                using var source = new IndexInput(sourcePath);
                using var frame = CodecFileReader.OpenSupported(source, descriptor);
                if (!sourceBodies.TryAdd(descriptor.FormatId, frame.OpenBodyInput()))
                {
                    throw new InvalidDataException(
                        $"Coordinated migration family '{family.FamilyId}' has multiple files for format '{descriptor.FormatId}'.");
                }

                var targetFileName = GetTargetFileName(sourceFileName, context.Action.SegmentId, context.SegmentIdMap);
                var targetPath = Path.Combine(context.TargetDirectory, targetFileName);
                var bodyPath = targetPath + ".family-body.tmp";
                targetFiles.Add(descriptor.FormatId, (targetPath, bodyPath, descriptor));
                targetBodies.Add(descriptor.FormatId, new IndexOutput(bodyPath, durable: true));
            }

            family.MigrationCoordinator!.Migrate(sourceBodies, targetBodies);
            foreach (var output in targetBodies.Values)
                output.Dispose();
            targetBodies.Clear();

            foreach (var target in targetFiles.Values)
            {
                CodecFileWriter.WriteAtomically(target.Path, target.Descriptor, durable: true, output =>
                {
                    using var body = new IndexInput(target.BodyPath);
                    long remaining = body.Length;
                    while (remaining > 0)
                    {
                        int count = (int)Math.Min(64 * 1024, remaining);
                        output.WriteBytes(body.ReadSpan(count));
                        remaining -= count;
                    }
                });
                context.RewrittenTargetPaths.Add(target.Path);
            }
        }
        finally
        {
            foreach (var input in sourceBodies.Values)
                input.Dispose();
            foreach (var output in targetBodies.Values)
                output.Dispose();
            foreach (var target in targetFiles.Values)
                TryDeleteTemporaryFile(target.BodyPath);
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private sealed record PostingRow(int DocId, int Frequency, int[] Positions, byte[][] Payloads);

    private sealed record MigrationRewriteContext(
        string TargetDirectory,
        string SourcePath,
        string TargetPath,
        IndexCodecMigrationAction Action,
        CodecFileDescriptor Descriptor,
        CodecCatalog Catalog,
        IReadOnlyDictionary<string, string> SegmentIdMap,
        HashSet<string> RewrittenTargetPaths);
}
