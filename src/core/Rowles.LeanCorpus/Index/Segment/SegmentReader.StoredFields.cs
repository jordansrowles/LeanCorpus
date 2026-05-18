namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Stored fields-related methods for SegmentReader.
/// </summary>
public sealed partial class SegmentReader
{
    internal IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> GetStoredFieldValues(int docId)
    {
        if (_storedReader is null)
            return new Dictionary<string, IReadOnlyList<StoredFieldValue>>();

        var raw = _storedReader.ReadDocumentValues(docId);
        return raw.ToDictionary(
            static kvp => kvp.Key,
            static kvp => (IReadOnlyList<StoredFieldValue>)kvp.Value.AsReadOnly(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns all stored fields for the specified document as a read-only dictionary.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID.</param>
    /// <returns>A dictionary mapping field names to their stored values.</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int docId)
    {
        var raw = GetStoredFieldValues(docId);
        var result = new Dictionary<string, IReadOnlyList<string>>(raw.Count, StringComparer.Ordinal);
        foreach (var (name, values) in raw)
        {
            var strings = values
                .Where(static value => !value.IsBinary && value.StringValue is not null)
                .Select(static value => value.StringValue!)
                .ToList();
            if (strings.Count > 0)
                result[name] = strings.AsReadOnly();
        }

        return result;
    }

    /// <summary>
    /// Returns all stored binary fields for the specified document as raw byte arrays.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID.</param>
    public IReadOnlyDictionary<string, IReadOnlyList<byte[]>> GetStoredBinaryFields(int docId)
    {
        var raw = GetStoredFieldValues(docId);
        var result = new Dictionary<string, IReadOnlyList<byte[]>>(raw.Count, StringComparer.Ordinal);
        foreach (var (name, values) in raw)
        {
            var binaries = values
                .Where(static value => value.IsBinary && value.BinaryValue is not null)
                .Select(static value => value.BinaryValue!.ToArray())
                .ToList();
            if (binaries.Count > 0)
                result[name] = binaries.AsReadOnly();
        }

        return result;
    }
}
