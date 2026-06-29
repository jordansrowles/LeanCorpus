using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;


namespace Rowles.LeanCorpus.Index.Format;

/// <summary>
/// Inspects LeanCorpus index directories and reports the detected on-disk format.
/// </summary>
public static class IndexFormatInspector
{
    private static readonly string[] RequiredExtensions = [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm"];

    /// <summary>
    /// Inspects the latest readable commit in <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The index directory to inspect.</param>
    /// <param name="options">Inspection options.</param>
    /// <returns>The detected index format inventory.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is <c>null</c>.</exception>
    public static IndexFormatInventory Inspect(MMapDirectory directory, IndexFormatInspectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(directory);

        var sw = Stopwatch.StartNew();
        using var activity = LeanCorpusActivitySource.Source.StartActivity(LeanCorpusActivitySource.FormatInspect);
        IndexFormatInventory? inventory = null;
        var succeeded = false;
        try
        {
            inventory = InspectCore(directory, options);
            succeeded = true;
            return inventory;
        }
        finally
        {
            sw.Stop();
            activity?.SetTag("operation.succeeded", succeeded);
            if (inventory is not null)
            {
                if (inventory.CommitGeneration is int commitGeneration)
                    activity?.SetTag("index.commit_generation", commitGeneration);
                activity?.SetTag("index.segment_count", inventory.Segments.Count);
                activity?.SetTag("index.orphan_file_count", inventory.OrphanFiles.Count);
                activity?.SetTag("index.issue_count", inventory.Issues.Count);
                activity?.SetTag("index.has_unsupported_future_format", inventory.HasUnsupportedFutureFormat);
            }

            LeanCorpusMaintenanceMetrics.RecordFormatInspect(sw.Elapsed, succeeded);
        }
    }

    private static IndexFormatInventory InspectCore(MMapDirectory directory, IndexFormatInspectionOptions? options)
    {
        options ??= new IndexFormatInspectionOptions();

        var directoryPath = directory.DirectoryPath;
        var issues = new List<IndexCheckIssue>();
        var commitData = TryFindLatestReadableCommit(directoryPath, issues, out int? commitGeneration);
        if (commitData is null)
        {
            return new IndexFormatInventory
            {
                DirectoryPath = directoryPath,
                CommitGeneration = commitGeneration,
                ContentToken = null,
                SegmentIds = [],
                Segments = [],
                OrphanFiles = InspectOrphanFiles(directoryPath, [], options, issues),
                Issues = issues,
                HasUnsupportedFutureFormat = issues.Any(static issue => issue.Code == IndexCheckIssueCodes.UnsupportedFutureCodecVersion)
            };
        }

        var segmentIds = commitData.Segments;
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = new List<SegmentFormatInventory>(segmentIds.Count);
        foreach (var segmentId in segmentIds)
            segments.Add(InspectSegment(directoryPath, segmentId, options, issues, referencedFiles));

        var orphanFiles = InspectOrphanFiles(directoryPath, referencedFiles, options, issues);
        return new IndexFormatInventory
        {
            DirectoryPath = directoryPath,
            CommitGeneration = commitGeneration,
            ContentToken = commitData.ContentToken,
            SegmentIds = segmentIds,
            Segments = segments,
            OrphanFiles = orphanFiles,
            Issues = issues,
            HasUnsupportedFutureFormat = HasUnsupportedFutureFormat(segments, orphanFiles)
        };
    }

    private static CommitData? TryFindLatestReadableCommit(
        string directoryPath,
        List<IndexCheckIssue> issues,
        out int? commitGeneration)
    {
        commitGeneration = null;
        var commitFiles = IndexFileInspector.FindCommitFiles(directoryPath);
        if (commitFiles.Count == 0)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.NoCommitFile,
                "No commit file (segments_N) found in directory.",
                null,
                null,
                false));
            return null;
        }

        foreach (var (generation, filePath) in commitFiles)
        {
            var commitIssues = new IndexCheckResult();
            var commitData = IndexFileInspector.TryReadCommit(filePath, generation, commitIssues);
            foreach (var issue in commitIssues.DetailedIssues)
                issues.Add(issue);

            if (commitData is null)
                continue;

            commitGeneration = generation;
            return commitData;
        }

        commitGeneration = commitFiles[0].Generation;
        return null;
    }

    private static SegmentFormatInventory InspectSegment(
        string directoryPath,
        string segmentId,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        HashSet<string> referencedFiles)
    {
        var basePath = Path.Combine(directoryPath, segmentId);
        var missingFiles = new List<string>();
        foreach (var extension in RequiredExtensions)
        {
            var path = basePath + extension;
            referencedFiles.Add(path);
            if (!File.Exists(path))
                missingFiles.Add(Path.GetFileName(path));
        }

        SegmentInfo? segmentInfo = null;
        var warnings = new List<string>();
        var segPath = basePath + ".seg";
        if (File.Exists(segPath))
        {
            try
            {
                segmentInfo = SegmentInfo.ReadFrom(segPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
            {
                issues.Add(CreateIssue(
                    IndexCheckSeverity.Error,
                    IndexCheckIssueCodes.SegmentMetadataUnreadable,
                    $"Segment '{segmentId}' cannot read .seg metadata: {ex.Message}",
                    Path.GetFileName(segPath),
                    segmentId,
                    false));
            }
        }

        var segmentFiles = FindSegmentFiles(directoryPath, segmentId);
        var files = new List<CodecFileInventory>(segmentFiles.Count);
        foreach (var filePath in segmentFiles)
        {
            referencedFiles.Add(filePath);
            if (!options.IncludeOptionalSidecars && !IsRequiredFile(filePath, segmentId))
                continue;

            var fieldName = TryGetVectorFieldName(basePath, filePath, segmentInfo);
            if (TryInspectFile(filePath, segmentId, fieldName, options, issues, out var inventory))
                files.Add(inventory);
        }

        return new SegmentFormatInventory
        {
            SegmentId = segmentId,
            DocCount = segmentInfo?.DocCount,
            LiveDocCount = segmentInfo?.LiveDocCount,
            CommitGeneration = segmentInfo?.CommitGeneration,
            DelGeneration = segmentInfo?.DelGeneration,
            Files = files,
            MissingFiles = missingFiles,
            Warnings = warnings
        };
    }

    private static List<string> FindSegmentFiles(string directoryPath, string segmentId)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(directoryPath, segmentId + ".*"))
            files.Add(file);
        foreach (var file in Directory.GetFiles(directoryPath, segmentId + "_gen_*.del"))
            files.Add(file);
        foreach (var file in Directory.GetFiles(directoryPath, segmentId + "_v_*.*"))
            files.Add(file);

        var result = files.ToList();
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static IReadOnlyList<CodecFileInventory> InspectOrphanFiles(
        string directoryPath,
        HashSet<string> referencedFiles,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues)
    {
        if (!Directory.Exists(directoryPath))
            return [];

        var orphans = new List<CodecFileInventory>();
        foreach (var filePath in Directory.GetFiles(directoryPath))
        {
            if (referencedFiles.Contains(filePath))
                continue;

            var fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("segments_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("stats_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "write.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryInspectFile(filePath, null, null, options, issues, out var inventory))
                orphans.Add(inventory);
        }

        return orphans;
    }

    private static bool TryInspectFile(
        string filePath,
        string? segmentId,
        string? fieldName,
        IndexFormatInspectionOptions options,
        List<IndexCheckIssue> issues,
        out CodecFileInventory inventory)
    {
        var extension = GetCodecExtension(filePath);
        if (!CodecFormatTable.TryGet(extension, out var descriptor))
        {
            inventory = null!;
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        var length = options.IncludeFileSizes ? new FileInfo(filePath).Length : (long?)null;
        if (!descriptor.HasHeader)
        {
            inventory = new CodecFileInventory
            {
                FileName = fileName,
                Extension = extension,
                CodecName = descriptor.CodecName,
                Version = null,
                CurrentVersion = descriptor.CurrentVersion,
                HasValidMagic = true,
                IsSupported = true,
                IsCurrent = true,
                Length = length,
                SegmentId = segmentId,
                FieldName = fieldName
            };
            return true;
        }

        var format = descriptor.HeaderFormat;
        if (format is null)
        {
            inventory = null!;
            return false;
        }

        var hasValidMagic = false;
        byte? version = null;
        try
        {
            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream);
            version = CodecFileHeader.ReadVersion(reader, format);
            hasValidMagic = true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.InvalidCodecMagic,
                $"Cannot read {descriptor.CodecName} header from '{fileName}': {ex.Message}",
                fileName,
                segmentId,
                false));
        }

        var isSupported = hasValidMagic && version <= descriptor.CurrentVersion;
        if (hasValidMagic && !isSupported)
        {
            issues.Add(CreateIssue(
                IndexCheckSeverity.Error,
                IndexCheckIssueCodes.UnsupportedFutureCodecVersion,
                $"Unsupported {descriptor.CodecName} format version {version}; this build supports up to version {descriptor.CurrentVersion}.",
                fileName,
                segmentId,
                false));
        }

        inventory = new CodecFileInventory
        {
            FileName = fileName,
            Extension = extension,
            CodecName = descriptor.CodecName,
            Version = version,
            CurrentVersion = descriptor.CurrentVersion,
            HasValidMagic = hasValidMagic,
            IsSupported = isSupported,
            IsCurrent = hasValidMagic && version == descriptor.CurrentVersion,
            Length = length,
            SegmentId = segmentId,
            FieldName = fieldName
        };
        return true;
    }

    private static bool IsRequiredFile(string filePath, string segmentId)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var extension in RequiredExtensions)
        {
            if (string.Equals(fileName, segmentId + extension, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string GetCodecExtension(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".stats.json", StringComparison.OrdinalIgnoreCase))
            return ".stats";

        return Path.GetExtension(filePath);
    }

    private static string? TryGetVectorFieldName(string basePath, string filePath, SegmentInfo? segmentInfo)
    {
        if (segmentInfo is null)
            return null;

        foreach (var vectorField in segmentInfo.VectorFields)
        {
            if (string.Equals(filePath, VectorFilePaths.VectorFile(basePath, vectorField.FieldName), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filePath, VectorFilePaths.HnswFile(basePath, vectorField.FieldName), StringComparison.OrdinalIgnoreCase))
            {
                return vectorField.FieldName;
            }
        }

        return null;
    }

    private static bool HasUnsupportedFutureFormat(
        IReadOnlyList<SegmentFormatInventory> segments,
        IReadOnlyList<CodecFileInventory> orphanFiles)
    {
        foreach (var segment in segments)
        {
            foreach (var file in segment.Files)
            {
                if (!file.IsSupported)
                    return true;
            }
        }

        foreach (var file in orphanFiles)
        {
            if (!file.IsSupported)
                return true;
        }

        return false;
    }

    private static IndexCheckIssue CreateIssue(
        IndexCheckSeverity severity,
        string code,
        string message,
        string? fileName,
        string? segmentId,
        bool isRepairable)
        => new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            FileName = fileName,
            SegmentId = segmentId,
            IsRepairable = isRepairable,
            SuggestedActions = IndexRepairRecommendations.ForIssue(code)
        };
}
