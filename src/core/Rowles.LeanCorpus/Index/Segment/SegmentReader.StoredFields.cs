namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Stored fields-related methods for SegmentReaderState.
/// </summary>
internal sealed partial class SegmentReaderState
{
    internal IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> GetStoredFieldValues(int docId)
    {
        return GetStoredFieldValues(docId, null);
    }

    internal IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> GetStoredFieldValues(int docId, ISet<string>? fieldsToLoad)
    {
        if (StoredReader is null)
            return new Dictionary<string, IReadOnlyList<StoredFieldValue>>();

        var raw = StoredReader.ReadDocumentValues(docId, fieldsToLoad);
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
        return GetStoredFields(docId, null);
    }

    /// <summary>
    /// Returns stored fields for the specified document, optionally filtering to the given set of field names.
    /// When <paramref name="fieldsToLoad"/> is null, all fields are returned.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID.</param>
    /// <param name="fieldsToLoad">Optional set of field names to load, or null to load all fields.</param>
    /// <returns>A dictionary mapping field names to their stored values.</returns>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetStoredFields(int docId, ISet<string>? fieldsToLoad)
    {
        var raw = GetStoredFieldValues(docId, fieldsToLoad);
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
        return GetStoredBinaryFields(docId, null);
    }

    /// <summary>
    /// Returns stored binary fields for the specified document, optionally filtering to the given set of field names.
    /// When <paramref name="fieldsToLoad"/> is null, all fields are returned.
    /// </summary>
    /// <param name="docId">The local (segment-relative) document ID.</param>
    /// <param name="fieldsToLoad">Optional set of field names to load, or null to load all fields.</param>
    public IReadOnlyDictionary<string, IReadOnlyList<byte[]>> GetStoredBinaryFields(int docId, ISet<string>? fieldsToLoad)
    {
        var raw = GetStoredFieldValues(docId, fieldsToLoad);
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
