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
}
