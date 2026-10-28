using System.Net;
using System.Text;

namespace Rowles.DataForge.Workloads;

/// <summary>Applies the immutable eligibility thresholds for Wikipedia reference v1.</summary>
public static class WikipediaEligibility
{
    public static WikipediaEligibilityResult Evaluate(int namespaceId, ulong pageId, bool hasRedirect, ulong revisionId,
        DateTimeOffset? revisionTimestamp, string? rawText)
    {
        if (namespaceId != 0) return Rejected(WikipediaEligibilityRejection.NonMainNamespace);
        if (pageId == 0) return Rejected(WikipediaEligibilityRejection.MissingRevision);
        if (hasRedirect) return Rejected(WikipediaEligibilityRejection.Redirect);
        if (revisionId == 0 || revisionTimestamp is null || revisionTimestamp.Value.Offset != TimeSpan.Zero)
            return Rejected(WikipediaEligibilityRejection.MissingRevision);
        if (rawText is null) return Rejected(WikipediaEligibilityRejection.MissingText);

        int rawBytes;
        try { rawBytes = WikipediaTextNormaliserV1.StrictUtf8ByteCount(rawText); }
        catch (InvalidDataException) { return Rejected(WikipediaEligibilityRejection.NormalisationFailed); }
        if (rawBytes < 256) return Rejected(WikipediaEligibilityRejection.RawTooSmall, rawBytes);
        if (rawBytes > WikipediaTextNormaliserV1.MaximumRawUtf8Bytes) return Rejected(WikipediaEligibilityRejection.RawTooLarge, rawBytes);
        if (!WikipediaTextNormaliserV1.TryNormalise(rawText, out var text))
            return Rejected(WikipediaEligibilityRejection.NormalisationFailed, rawBytes);
        var textBytes = Encoding.UTF8.GetByteCount(text);
        if (textBytes < 256) return new(WikipediaEligibilityRejection.NormalisedTooSmall, text, rawBytes, textBytes, 0);
        if (textBytes > WikipediaTextNormaliserV1.MaximumOutputUtf8Bytes) return new(WikipediaEligibilityRejection.NormalisedTooLarge, text, rawBytes, textBytes, 0);
        var tokenCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (tokenCount < 40) return new(WikipediaEligibilityRejection.TooFewTokens, text, rawBytes, textBytes, tokenCount);
        return new(WikipediaEligibilityRejection.None, text, rawBytes, textBytes, tokenCount);
    }

    private static WikipediaEligibilityResult Rejected(WikipediaEligibilityRejection reason, int rawBytes = 0) =>
        new(reason, null, rawBytes, 0, 0);
}
