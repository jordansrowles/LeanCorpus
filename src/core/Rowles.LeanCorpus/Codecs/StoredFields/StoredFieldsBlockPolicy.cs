namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Shared limits for the stored-fields v4-and-later block layout. Keep these values stable
/// for a body version because they define which blocks its reader accepts.
/// </summary>
internal static class StoredFieldsBlockPolicy
{
    internal const int TargetRawBytes = 1024 * 1024;
    internal const int MaximumRawBytes = 256 * 1024 * 1024;
    internal const int MaximumDocumentCount = 100_000;

    internal static void ValidateRawLength(long rawLength)
    {
        if (rawLength < 0 || rawLength > MaximumRawBytes)
            throw new InvalidDataException(
                $"Stored fields raw length {rawLength} exceeds the maximum block size {MaximumRawBytes}.");
    }

    internal static bool IsValidMaximumDocumentCount(int maximumDocumentCount)
        => maximumDocumentCount is >= 1 and <= MaximumDocumentCount;

    internal static void ValidateMaximumDocumentCount(int maximumDocumentCount, string? parameterName = null)
    {
        if (!IsValidMaximumDocumentCount(maximumDocumentCount))
            throw new ArgumentOutOfRangeException(
                parameterName ?? nameof(maximumDocumentCount), maximumDocumentCount,
                $"Stored fields block document limit must be in the range [1, {MaximumDocumentCount}].");
    }

    internal static bool ShouldFlushBeforeAdd(
        int documentCount,
        int rawBytes,
        long nextDocumentRawBytes,
        int maximumDocumentCount)
        => documentCount > 0 &&
            (documentCount >= maximumDocumentCount || nextDocumentRawBytes > TargetRawBytes - (long)rawBytes);

    internal static bool ShouldFlushAfterAdd(int documentCount, int rawBytes, int maximumDocumentCount)
        => documentCount >= maximumDocumentCount || rawBytes >= TargetRawBytes;
}
