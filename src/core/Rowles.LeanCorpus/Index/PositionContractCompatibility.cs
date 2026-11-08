namespace Rowles.LeanCorpus.Index;

/// <summary>Shared guidance for readable segments whose removed positions cannot be recovered.</summary>
internal static class PositionContractCompatibility
{
    internal static IndexCheckIssue CreateIssue(string segmentId) => new()
    {
        Severity = IndexCheckSeverity.Warning,
        Code = IndexCheckIssueCodes.PositionContractNotRecorded,
        Message = $"ReindexRecommended: Segment '{segmentId}' predates the position contract and may be affected by collapsed stop-word positions. Quoted phrases containing stop words can miss this segment, and phrases without them can produce false positives. Reindex from the original text for correct phrase behaviour. Codec migration cannot recover removed positions.",
        FileName = segmentId + ".seg",
        SegmentId = segmentId,
        IsRepairable = false,
        SuggestedActions = IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.PositionContractNotRecorded)
    };
}
