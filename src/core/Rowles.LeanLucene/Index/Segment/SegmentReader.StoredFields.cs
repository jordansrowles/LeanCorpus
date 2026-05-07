namespace Rowles.LeanLucene.Index.Segment;

/// <summary>
/// Stored fields-related methods for SegmentReader.
/// </summary>
public sealed partial class SegmentReader
{
    /// <summary>
    /// Returns all stored fields for the specified document as a read-only dictionary.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID.</param>
    /// <returns>A dictionary mapping field names to their stored values.</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int docId)
    {
        if (_storedReader is null)
            return new Dictionary<string, IReadOnlyList<string>>();

        var raw = _storedReader.ReadDocument(docId);
        // Convert to read-only types
        return raw.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly());
    }
}
