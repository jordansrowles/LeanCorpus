using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

internal enum DeletionStateValidationError
{
    None,
    MissingFile,
    UnreadableFile,
    LiveCountMismatch,
    SoftDeleteTimestampMismatch
}

internal readonly record struct DeletionStateValidationResult(
    string FilePath,
    bool FileExists,
    LiveDocs? LiveDocs,
    DeletionStateValidationError Error,
    string? Message,
    Exception? Exception)
{
    internal bool IsValid => Error == DeletionStateValidationError.None;
}

/// <summary>
/// Resolves and validates the mutable deletion sidecar selected by segment metadata.
/// Missing state is valid only for an all-live segment with no selected generation.
/// </summary>
internal static class DeletionStateValidator
{
    private readonly record struct DeletionMetadata(
        string SegmentId,
        int DocCount,
        int LiveDocCount,
        int? DelGeneration,
        long? EarliestSoftDeleteTimestamp);

    private static DeletionMetadata Capture(SegmentInfo info)
        => new(info.SegmentId, info.DocCount, info.LiveDocCount, info.DelGeneration, info.EarliestSoftDeleteTimestamp);

    private static DeletionMetadata Capture(SegmentDescriptor info)
        => new(info.SegmentId, info.DocCount, info.LiveDocCount, info.DelGeneration, info.EarliestSoftDeleteTimestamp);

    internal static string GetPath(string segmentBasePath, SegmentInfo info)
        => GetPath(segmentBasePath, Capture(info));

    internal static string GetPath(string segmentBasePath, SegmentDescriptor info)
        => GetPath(segmentBasePath, Capture(info));

    private static string GetPath(string segmentBasePath, DeletionMetadata info)
        => info.DelGeneration is int generation
            ? segmentBasePath + $"_gen_{generation}.del"
            : segmentBasePath + ".del";

    internal static string GetFileName(SegmentInfo info)
        => GetFileName(Capture(info));

    internal static string GetFileName(SegmentDescriptor info)
        => GetFileName(Capture(info));

    private static string GetFileName(DeletionMetadata info)
        => info.DelGeneration is int generation
            ? $"{info.SegmentId}_gen_{generation}.del"
            : info.SegmentId + ".del";

    internal static bool RequiresFile(SegmentInfo info)
        => RequiresFile(Capture(info));

    internal static bool RequiresFile(SegmentDescriptor info)
        => RequiresFile(Capture(info));

    private static bool RequiresFile(DeletionMetadata info)
        => info.DelGeneration.HasValue || info.LiveDocCount != info.DocCount ||
            info.EarliestSoftDeleteTimestamp.HasValue;

    internal static DeletionStateValidationResult Validate(string segmentBasePath, SegmentInfo info)
        => Validate(segmentBasePath, Capture(info));

    internal static DeletionStateValidationResult Validate(string segmentBasePath, SegmentDescriptor info)
        => Validate(segmentBasePath, Capture(info));

    private static DeletionStateValidationResult Validate(string segmentBasePath, DeletionMetadata info)
    {
        string delPath = GetPath(segmentBasePath, info);
        if (info.DocCount < 0)
        {
            string message = $"Segment '{info.SegmentId}' has invalid DocCount={info.DocCount}.";
            return new DeletionStateValidationResult(
                delPath, false, null, DeletionStateValidationError.UnreadableFile, message, null);
        }
        if (info.LiveDocCount < 0 || info.LiveDocCount > info.DocCount)
        {
            string message = $"Segment '{info.SegmentId}' has invalid LiveDocCount={info.LiveDocCount} for DocCount={info.DocCount}.";
            return new DeletionStateValidationResult(
                delPath, false, null, DeletionStateValidationError.LiveCountMismatch, message, null);
        }

        bool required = RequiresFile(info);
        bool exists;
        try
        {
            exists = FileOpenRetry.FileExists(delPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            string message = $"Cannot check deletion file '{Path.GetFileName(delPath)}' for segment '{info.SegmentId}': {ex.Message}";
            return new DeletionStateValidationResult(
                delPath, false, null, DeletionStateValidationError.UnreadableFile, message, ex);
        }

        if (!exists)
        {
            if (required)
            {
                string message = $"Segment '{info.SegmentId}' requires deletion file '{Path.GetFileName(delPath)}', but it is missing.";
                return new DeletionStateValidationResult(
                    delPath, false, null, DeletionStateValidationError.MissingFile, message, null);
            }

            return new DeletionStateValidationResult(
                delPath, false, null, DeletionStateValidationError.None, null, null);
        }

        LiveDocs liveDocs;
        try
        {
            liveDocs = LiveDocs.Deserialise(delPath, info.DocCount);
        }
        catch (FileNotFoundException ex)
        {
            if (!required)
            {
                // An all-live segment does not depend on an optional legacy sidecar.
                return new DeletionStateValidationResult(
                    delPath, false, null, DeletionStateValidationError.None, null, ex);
            }

            string message = $"Segment '{info.SegmentId}' requires deletion file '{Path.GetFileName(delPath)}', but it is missing.";
            return new DeletionStateValidationResult(
                delPath, false, null, DeletionStateValidationError.MissingFile, message, ex);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            string message = $"Deletion file '{Path.GetFileName(delPath)}' for segment '{info.SegmentId}' is unreadable: {ex.Message}";
            return new DeletionStateValidationResult(
                delPath, true, null, DeletionStateValidationError.UnreadableFile, message, ex);
        }

        if (liveDocs.LiveCount != info.LiveDocCount)
        {
            string message = $"Deletion file '{Path.GetFileName(delPath)}' records {liveDocs.LiveCount} live documents, but segment metadata records {info.LiveDocCount}.";
            return new DeletionStateValidationResult(
                delPath, true, liveDocs, DeletionStateValidationError.LiveCountMismatch, message, null);
        }

        if (liveDocs.EarliestSoftDeleteTimestamp != info.EarliestSoftDeleteTimestamp)
        {
            string message = $"Deletion file '{Path.GetFileName(delPath)}' has earliest soft-delete timestamp {liveDocs.EarliestSoftDeleteTimestamp?.ToString() ?? "none"}, but segment metadata records {info.EarliestSoftDeleteTimestamp?.ToString() ?? "none"}.";
            return new DeletionStateValidationResult(
                delPath, true, liveDocs, DeletionStateValidationError.SoftDeleteTimestampMismatch, message, null);
        }

        return new DeletionStateValidationResult(
            delPath, true, liveDocs, DeletionStateValidationError.None, null, null);
    }

    internal static LiveDocs? RequireValid(string segmentBasePath, SegmentInfo info)
        => RequireValid(Validate(segmentBasePath, info));

    internal static LiveDocs? RequireValid(string segmentBasePath, SegmentDescriptor info)
        => RequireValid(Validate(segmentBasePath, info));

    private static LiveDocs? RequireValid(DeletionStateValidationResult result)
    {
        if (!result.IsValid)
            throw result.Exception is null
                ? new InvalidDataException(result.Message)
                : new InvalidDataException(result.Message, result.Exception);

        return result.LiveDocs;
    }

    internal static void RequireFileIfSelected(SegmentInfo info, IReadOnlyCollection<string> inventory)
        => RequireFileIfSelected(Capture(info), inventory);

    internal static void RequireFileIfSelected(SegmentDescriptor info, IReadOnlyCollection<string> inventory)
        => RequireFileIfSelected(Capture(info), inventory);

    private static void RequireFileIfSelected(DeletionMetadata info, IReadOnlyCollection<string> inventory)
    {
        if (!RequiresFile(info))
            return;

        string fileName = GetFileName(info);
        if (!inventory.Contains(fileName, StringComparer.Ordinal))
            throw new FileNotFoundException(
                $"Segment '{info.SegmentId}' requires deletion file '{fileName}', but it is missing.",
                fileName);
    }
}
