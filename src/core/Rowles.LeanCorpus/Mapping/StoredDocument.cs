namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// Snapshot of stored-field data for a single document, as returned by
/// <see cref="Search.Searcher.IndexSearcher.GetStoredFields(int)"/> and
/// <see cref="Search.Searcher.IndexSearcher.GetStoredBinaryFields(int)"/>.
/// </summary>
/// <param name="Fields">String stored-field values indexed by field name.</param>
/// <param name="BinaryFields">Binary stored-field values indexed by field name.</param>
public readonly record struct StoredDocument(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<byte[]>> BinaryFields)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyStrings =
        new Dictionary<string, IReadOnlyList<string>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<byte[]>> EmptyBinaries =
        new Dictionary<string, IReadOnlyList<byte[]>>();

    /// <summary>An empty <see cref="StoredDocument"/> with no fields.</summary>
    public static StoredDocument Empty { get; } = new(EmptyStrings, EmptyBinaries);

    /// <summary>
    /// Builds a <see cref="StoredDocument"/> from the supplied dictionaries.
    /// Null arguments are replaced with empty read-only dictionaries.
    /// </summary>
    /// <param name="fields">String stored-field values, or <c>null</c>.</param>
    /// <param name="binaryFields">Binary stored-field values, or <c>null</c>.</param>
    /// <returns>A populated <see cref="StoredDocument"/>.</returns>
    public static StoredDocument Create(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? fields,
        IReadOnlyDictionary<string, IReadOnlyList<byte[]>>? binaryFields)
        => new(fields ?? EmptyStrings, binaryFields ?? EmptyBinaries);

    /// <summary>
    /// Returns the first string value for the named field, or <c>null</c> when the field is missing or empty.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <returns>The first stored string value, or <c>null</c>.</returns>
    public string? GetFirst(string name)
    {
        if (Fields.TryGetValue(name, out var values) && values.Count > 0)
            return values[0];
        return null;
    }

    /// <summary>
    /// Returns the first binary value for the named field, or <c>null</c> when the field is missing or empty.
    /// </summary>
    /// <param name="name">The field name.</param>
    /// <returns>The first stored binary value, or <c>null</c>.</returns>
    public byte[]? GetFirstBinary(string name)
    {
        if (BinaryFields.TryGetValue(name, out var values) && values.Count > 0)
            return values[0];
        return null;
    }

    /// <summary>Returns all string values for the named field, or an empty list.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The stored string values.</returns>
    public IReadOnlyList<string> GetValues(string name)
        => Fields.TryGetValue(name, out var values) ? values : Array.Empty<string>();
}
