using System.Diagnostics;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

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
        usesStaging = false;
        var plan = PlanCore(directory, options);
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
        usesStaging = options.UseStagingDirectory || !options.AllowInPlaceMigration;
        var targetDirectory = usesStaging
            ? ResolveStagingDirectory(sourceDirectory, options.StagingDirectory)
            : sourceDirectory;
        var now = DateTimeOffset.UtcNow;
        var marker = new IndexMigrationMarker
        {
            State = usesStaging ? IndexMigrationState.Prepared : IndexMigrationState.InProgress,
            SourceDirectory = sourceDirectory,
            StagingDirectory = targetDirectory,
            SourceCommitGeneration = plan.Inventory.CommitGeneration,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PlannedActions = plan.Actions
        };

        var executed = new List<IndexCodecMigrationAction>();
        var currentState = marker.State;
        try
        {
            IndexMigrationRecovery.WriteMarker(sourceDirectory, marker, durable: true);
            if (usesStaging)
            {
                PrepareStagingDirectory(sourceDirectory, targetDirectory);
                IndexMigrationRecovery.WriteMarker(
                    sourceDirectory,
                    marker with { State = IndexMigrationState.InProgress, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    durable: true);
                currentState = IndexMigrationState.InProgress;
            }

            var rewrittenStoredFieldSegments = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in plan.Actions)
            {
                ExecuteRewrite(targetDirectory, action, rewrittenStoredFieldSegments);
                executed.Add(action);
            }

            IndexCheckResult? validationResult = null;
            if (options.ValidateAfterMigration)
            {
                using var target = new MMapDirectory(targetDirectory);
                validationResult = IndexValidator.Check(target, new IndexCheckOptions { Deep = true });
                if (!usesStaging)
                    validationResult = RemoveMigrationMarkerIssue(validationResult);

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
                        StagingDirectory = usesStaging ? targetDirectory : null,
                        ExecutedActions = executed,
                        ValidationResult = validationResult,
                        Issues = validationResult.DetailedIssues
                    };
                }
            }

            if (usesStaging)
            {
                IndexMigrationRecovery.WriteMarker(
                    sourceDirectory,
                    marker with { State = IndexMigrationState.ReadyToPublish, UpdatedAtUtc = DateTimeOffset.UtcNow },
                    durable: true);
                currentState = IndexMigrationState.ReadyToPublish;
                PublishStagingDirectory(sourceDirectory, targetDirectory);
            }

            IndexMigrationRecovery.WriteMarker(
                sourceDirectory,
                marker with { State = IndexMigrationState.Published, UpdatedAtUtc = DateTimeOffset.UtcNow },
                durable: true);
            currentState = IndexMigrationState.Published;
            var resultIssues = plan.Issues;
            if (usesStaging && TryDeleteStagingDirectory(targetDirectory, out var cleanupIssue))
                resultIssues = [.. resultIssues, cleanupIssue];

            return new IndexCodecMigrationResult
            {
                Succeeded = true,
                DryRun = false,
                SourceDirectory = sourceDirectory,
                StagingDirectory = usesStaging ? targetDirectory : null,
                ExecutedActions = executed,
                ValidationResult = validationResult,
                Issues = resultIssues
            };
        }
        catch (Exception ex) when (IsMigrationFailure(ex))
        {
            if (currentState is not IndexMigrationState.ReadyToPublish and not IndexMigrationState.Published)
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
                StagingDirectory = usesStaging ? targetDirectory : null,
                ExecutedActions = executed,
                ValidationResult = null,
                Issues = issues
            };
        }
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

    private static IndexCheckResult RemoveMigrationMarkerIssue(IndexCheckResult result)
    {
        var filtered = new IndexCheckResult
        {
            SegmentsChecked = result.SegmentsChecked,
            DocumentsChecked = result.DocumentsChecked,
            FilesChecked = result.FilesChecked,
            CommitGeneration = result.CommitGeneration
        };

        foreach (var issue in result.DetailedIssues)
        {
            if (issue.Code == IndexCheckIssueCodes.MigrationInProgress)
                continue;

            filtered.AddIssue(
                issue.Severity,
                issue.Code,
                issue.Message,
                issue.FileName,
                issue.SegmentId,
                issue.IsRepairable,
                issue.SuggestedActions);
        }

        return filtered;
    }

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

    private static void PublishStagingDirectory(string sourceDirectory, string stagingDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(stagingDirectory))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, IndexMigrationRecovery.MarkerFileName, StringComparison.Ordinal))
                continue;

            File.Copy(file, Path.Combine(sourceDirectory, name), overwrite: true);
        }
    }

    private static void ExecuteRewrite(
        string targetDirectory,
        IndexCodecMigrationAction action,
        HashSet<string> rewrittenStoredFieldSegments)
    {
        if (action.Kind != IndexCodecMigrationActionKind.RewriteFile)
            return;

        var filePath = Path.Combine(targetDirectory, action.SourcePath);
        switch (Path.GetExtension(action.SourcePath))
        {
            case ".dic":
                RewriteTermDictionary(filePath);
                break;
            case ".pos":
                RewritePostings(targetDirectory, action);
                break;
            case ".nrm":
                RewriteNorms(filePath);
                break;
            case ".dvn":
                RewriteNumericDocValues(filePath);
                break;
            case ".dvs":
                RewriteSortedDocValues(filePath);
                break;
            case ".dss":
                RewriteSortedSetDocValues(filePath);
                break;
            case ".dsn":
                RewriteSortedNumericDocValues(filePath);
                break;
            case ".dvb":
                RewriteBinaryDocValues(filePath);
                break;
            case ".fln":
                RewriteFieldLengths(filePath);
                break;
            case ".fdt":
            case ".fdx":
                if (action.SegmentId is not null && !rewrittenStoredFieldSegments.Add(action.SegmentId))
                    return;
                RewriteStoredFields(targetDirectory, action);
                break;
            default:
                throw new InvalidDataException($"No migration writer is registered for '{action.SourcePath}'.");
        }
    }

    private static void RewriteTermDictionary(string path)
    {
        // Peek at the version byte so we can dispatch to the right legacy reader.
        // The live TermDictionaryReader.Open refuses anything older than v3.
        byte version;
        using (var probe = new IndexInput(path))
        {
            int magic = probe.ReadInt32();
            if (magic != CodecConstants.Magic)
                throw new InvalidDataException(
                    $"Invalid term dictionary file '{path}': expected magic 0x{CodecConstants.Magic:X8}, got 0x{magic:X8}.");
            version = probe.ReadByte();
        }

        if (version == CodecConstants.TermDictionaryVersion) return; // already v3

        List<(string Term, long Offset)> entries;
        using (var input = new IndexInput(path))
        {
            // Re-skip the header bytes; legacy readers expect to read straight past them.
            input.ReadInt32();
            input.ReadByte();

            if (version == 1)
            {
                var v1 = Codecs.TermDictionary.Legacy.TermDictionaryV1Reader.Open(input);
                entries = v1.EnumerateAllTerms();
            }
            else if (version == 2)
            {
                var v2 = Codecs.TermDictionary.Legacy.TermDictionaryV2Reader.Open(input);
                entries = v2.EnumerateAllTerms();
            }
            else
            {
                throw new InvalidDataException(
                    $"Unsupported term dictionary version {version}; cannot migrate to v{CodecConstants.TermDictionaryVersion}.");
            }
        }

        var sortedTerms = new List<string>(entries.Count);
        var offsets = new Dictionary<string, long>(entries.Count, StringComparer.Ordinal);
        foreach (var (term, offset) in entries)
        {
            sortedTerms.Add(term);
            offsets[term] = offset;
        }

        WriteSingleFileAtomically(path, temporaryPath => TermDictionaryWriter.Write(temporaryPath, sortedTerms, offsets));
    }

    private static void RewritePostings(string targetDirectory, IndexCodecMigrationAction action)
    {
        if (action.SegmentId is null)
            throw new InvalidDataException($"Postings action for '{action.SourcePath}' has no segment ID.");

        var basePath = Path.Combine(targetDirectory, action.SegmentId);
        List<(string Term, List<PostingRow> Postings)> terms;
        using (var dictionary = TermDictionaryReader.Open(basePath + ".dic"))
        using (var input = new IndexInput(basePath + ".pos"))
        {
            byte version = PostingsEnum.ValidateFileHeader(input);
            terms = dictionary
                .EnumerateAllTerms()
                .Select(term => (term.Term, ReadPostingRows(input, term.Offset, version)))
                .ToList();
        }

        var postingsOffsets = new Dictionary<string, long>(terms.Count, StringComparer.Ordinal);
        var headerPatches = new List<(long HeaderPos, int DocFreq, long SkipOffset)>(terms.Count);

        var posPath = basePath + ".pos";
        var dicPath = basePath + ".dic";
        var temporaryPosPath = posPath + ".tmp";
        var temporaryDicPath = dicPath + ".tmp";

        try
        {
            using (var output = new IndexOutput(temporaryPosPath))
            {
                CodecConstants.WriteHeader(output, CodecConstants.PostingsVersion);
                using var blockWriter = new BlockPostingsWriter(output);

                foreach (var (term, postings) in terms)
                {
                    bool hasFreqs = postings.Any(static posting => posting.Frequency != 1);
                    bool hasPositions = postings.Any(static posting => posting.Positions.Length > 0);
                    bool hasPayloads = postings.Any(static posting => posting.Payloads.Any(static payload => payload.Length > 0));

                    long headerPos = output.Position;
                    output.WriteInt32(0);
                    output.WriteInt64(0L);
                    output.WriteBoolean(hasFreqs);
                    output.WriteBoolean(hasPositions);
                    output.WriteBoolean(hasPayloads);

                    blockWriter.StartTerm();
                    foreach (var posting in postings)
                        blockWriter.AddPosting(posting.DocId, hasFreqs ? posting.Frequency : 1);
                    var metadata = blockWriter.FinishTerm();

                    if (hasPositions)
                        WritePositionRows(output, postings, hasPayloads);

                    headerPatches.Add((headerPos, metadata.DocFreq, metadata.SkipOffset));
                    postingsOffsets[term] = headerPos;
                }
            }

            using (var patchStream = new FileStream(temporaryPosPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Span<byte> patch = stackalloc byte[12];
                foreach (var (headerPos, docFreq, skipOffset) in headerPatches)
                {
                    patchStream.Seek(headerPos, SeekOrigin.Begin);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(patch, docFreq);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(patch[4..], skipOffset);
                    patchStream.Write(patch);
                }
            }

            TermDictionaryWriter.Write(temporaryDicPath, postingsOffsets.Keys.Order(StringComparer.Ordinal).ToList(), postingsOffsets);
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

    private static void RewriteNumericDocValues(string path)
    {
        var (values, presence) = NumericDocValuesReader.Read(path);
        var docCount = values.Count == 0 ? 0 : values.Values.Max(static field => field.Length);
        var presenceSets = new Dictionary<string, IReadOnlySet<int>>(StringComparer.Ordinal);
        foreach (var (field, bitmap) in presence)
        {
            if (bitmap is not null)
                presenceSets[field] = bitmap.ToHashSet();
        }

        WriteSingleFileAtomically(path, temporaryPath => NumericDocValuesWriter.Write(temporaryPath, values, docCount, presenceSets));
    }

    private static void RewriteSortedDocValues(string path)
    {
        var (values, _) = SortedDocValuesReader.Read(path);
        var docCount = values.Count == 0 ? 0 : values.Values.Max(static field => field.Length);
        var nullableValues = values.ToDictionary(
            static item => item.Key,
            static item => item.Value.Select(static value => (string?)value).ToArray(),
            StringComparer.Ordinal);
        WriteSingleFileAtomically(path, temporaryPath => SortedDocValuesWriter.Write(temporaryPath, nullableValues, docCount));
    }

    private static void RewriteNorms(string path)
    {
        var values = NormsReader.Read(path);
        var fieldNorms = values.Norms.ToDictionary(
            static item => item.Key,
            static item =>
            {
                var norms = new float[item.Value.Length];
                for (int i = 0; i < item.Value.Length; i++)
                    norms[i] = item.Value[i] / 255f;
                return norms;
            },
            StringComparer.Ordinal);

        IReadOnlyDictionary<string, float[]>? fieldBoosts = values.Boosts.Count > 0
            ? values.Boosts
            : null;

        var docCount = fieldNorms.Count == 0 ? 0 : fieldNorms.Values.Max(static field => field.Length);
        WriteSingleFileAtomically(path, temporaryPath => NormsWriter.Write(temporaryPath, fieldNorms, fieldBoosts, docCount));
    }

    private static void RewriteSortedSetDocValues(string path)
    {
        var values = SortedSetDocValuesReader.Read(path);
        var docCount = values.Count == 0 ? 0 : values.Values.Max(static field => field.Length);
        var columns = new Dictionary<string, IReadOnlyList<string>?[]>(values.Count, StringComparer.Ordinal);
        foreach (var (field, fieldValues) in values)
            columns[field] = fieldValues;

        WriteSingleFileAtomically(path, temporaryPath => SortedSetDocValuesWriter.Write(temporaryPath, columns, docCount));
    }

    private static void RewriteSortedNumericDocValues(string path)
    {
        var values = SortedNumericDocValuesReader.Read(path);
        var docCount = values.Count == 0 ? 0 : values.Values.Max(static field => field.Length);
        var columns = new Dictionary<string, IReadOnlyList<double>?[]>(values.Count, StringComparer.Ordinal);
        foreach (var (field, fieldValues) in values)
            columns[field] = fieldValues;

        WriteSingleFileAtomically(path, temporaryPath => SortedNumericDocValuesWriter.Write(temporaryPath, columns, docCount));
    }

    private static void RewriteBinaryDocValues(string path)
    {
        var values = BinaryDocValuesReader.Read(path);
        var docCount = values.Count == 0 ? 0 : values.Values.Max(static field => field.Length);
        var columns = new Dictionary<string, IReadOnlyList<byte[]>?[]>(values.Count, StringComparer.Ordinal);
        foreach (var (field, fieldValues) in values)
            columns[field] = fieldValues;

        WriteSingleFileAtomically(path, temporaryPath => BinaryDocValuesWriter.Write(temporaryPath, columns, docCount));
    }

    private static void RewriteFieldLengths(string path)
    {
        var values = FieldLengthReader.TryRead(path) ?? new Dictionary<string, int[]>(StringComparer.Ordinal);
        var docCount = values.Count == 0 ? 0 : values.Values.Max(static field => field.Length);
        WriteSingleFileAtomically(path, temporaryPath => FieldLengthWriter.Write(temporaryPath, values, docCount));
    }

    private static void WriteSingleFileAtomically(string path, Action<string> writeTemporary)
    {
        var temporaryPath = path + ".tmp";
        try
        {
            writeTemporary(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static List<PostingRow> ReadPostingRows(IndexInput input, long offset, byte version)
    {
        using var postings = PostingsEnum.CreateWithPositions(input, offset, version);
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

    private static void RewriteStoredFields(string targetDirectory, IndexCodecMigrationAction action)
    {
        if (action.SegmentId is null)
            throw new InvalidDataException($"Stored fields action for '{action.SourcePath}' has no segment ID.");

        var basePath = Path.Combine(targetDirectory, action.SegmentId);
        var info = SegmentInfo.ReadFrom(basePath + ".seg");
        var fdtPath = basePath + ".fdt";
        var fdxPath = basePath + ".fdx";
        var temporaryFdtPath = fdtPath + ".tmp";
        var temporaryFdxPath = fdxPath + ".tmp";

        try
        {
            using (var reader = StoredFieldsReader.Open(fdtPath, fdxPath))
            {
                StoredFieldsWriter.Write(
                    temporaryFdtPath,
                    temporaryFdxPath,
                    info.DocCount,
                    reader.ReadDocumentValues,
                    compression: FieldCompressionPolicy.Deflate);
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

    private sealed record PostingRow(int DocId, int Frequency, int[] Positions, byte[][] Payloads);
}
