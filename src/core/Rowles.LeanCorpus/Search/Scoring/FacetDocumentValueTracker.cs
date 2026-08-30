namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Tracks the last document in which each facet bucket was observed.
/// </summary>
/// <remarks>
/// The dictionary is shared by the accumulator for the complete search, rather
/// than allocating a set for every document. This keeps duplicate values in a
/// document from inflating a bucket while retaining the existing bucket-count
/// memory model.
/// </remarks>
internal sealed class FacetDocumentValueTracker
{
    private readonly Dictionary<string, int> _lastDocumentByValue = new(StringComparer.Ordinal);

    /// <summary>Marks a value for a document and returns whether it was not already marked.</summary>
    public bool MarkSeen(int documentId, string value)
    {
        if (_lastDocumentByValue.TryGetValue(value, out int previousDocumentId)
            && previousDocumentId == documentId)
            return false;

        _lastDocumentByValue[value] = documentId;
        return true;
    }
}
