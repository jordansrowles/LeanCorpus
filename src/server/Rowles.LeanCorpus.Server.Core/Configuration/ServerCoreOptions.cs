namespace Rowles.LeanCorpus.Server.Core.Configuration;

/// <summary>Configures local Server Core storage and request limits.</summary>
public sealed class ServerCoreOptions
{
    /// <summary>Gets or sets the directory that contains the server registry and index data.</summary>
    public string DataRoot { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum number of bulk operations accepted in one request.</summary>
    public int MaximumBulkOperations { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum number of results returned by a search.</summary>
    public int MaximumSearchResults { get; set; } = 1_000;

    /// <summary>Gets or sets the maximum serialised document size accepted by the write path.</summary>
    public long MaximumDocumentBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Gets or sets the maximum nesting depth accepted in a query.</summary>
    public int MaximumQueryDepth { get; set; } = 32;

    /// <summary>Gets or sets the maximum number of Boolean clauses accepted in one query.</summary>
    public int MaximumBooleanClauses { get; set; } = 1_024;

    /// <summary>Gets or sets the maximum wildcard expansions allowed for one query.</summary>
    public int MaximumWildcardExpansions { get; set; } = 1_024;

    /// <summary>Gets or sets the maximum regular-expression complexity estimate.</summary>
    public int MaximumRegexpComplexity { get; set; } = 4_096;

    /// <summary>Gets or sets the maximum inspection items returned by one request.</summary>
    public int MaximumInspectionItems { get; set; } = 1_000;

    /// <summary>Gets or sets the maximum length of an inspected value.</summary>
    public int MaximumInspectionValueLength { get; set; } = 4_096;

    /// <summary>Gets or sets the default background commit interval.</summary>
    public TimeSpan CommitInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the number of pending operations that triggers an early commit.</summary>
    public int MaximumUncommittedOperations { get; set; } = 1_000;

    /// <summary>Gets or sets the default visibility refresh interval.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets or sets the maximum number of idempotency records retained per index.</summary>
    public int MaximumIdempotencyEntries { get; set; } = 10_000;

    /// <summary>Gets or sets how long shutdown waits for active operations to drain.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Validates the Core-only configuration before opening storage.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DataRoot);
        ValidatePositive(MaximumBulkOperations, nameof(MaximumBulkOperations));
        ValidatePositive(MaximumSearchResults, nameof(MaximumSearchResults));
        ValidatePositive(MaximumDocumentBytes, nameof(MaximumDocumentBytes));
        ValidatePositive(MaximumQueryDepth, nameof(MaximumQueryDepth));
        ValidatePositive(MaximumBooleanClauses, nameof(MaximumBooleanClauses));
        ValidatePositive(MaximumWildcardExpansions, nameof(MaximumWildcardExpansions));
        ValidatePositive(MaximumRegexpComplexity, nameof(MaximumRegexpComplexity));
        ValidatePositive(MaximumInspectionItems, nameof(MaximumInspectionItems));
        ValidatePositive(MaximumInspectionValueLength, nameof(MaximumInspectionValueLength));
        ValidatePositive(MaximumIdempotencyEntries, nameof(MaximumIdempotencyEntries));
        ValidatePositive(MaximumUncommittedOperations, nameof(MaximumUncommittedOperations));
        if (CommitInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CommitInterval));
        if (RefreshInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(RefreshInterval));
        if (ShutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout));
    }

    private static void ValidatePositive(long value, string name)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }
}
