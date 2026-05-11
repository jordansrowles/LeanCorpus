namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Options controlling index validation depth.
/// </summary>
public sealed class IndexCheckOptions
{
    /// <summary>Gets or sets a value indicating whether all deep validation checks should run.</summary>
    public bool Deep { get; set; }

    /// <summary>Gets or sets a value indicating whether postings should be deeply validated.</summary>
    public bool VerifyPostings { get; set; }

    /// <summary>Gets or sets a value indicating whether stored fields should be deeply validated.</summary>
    public bool VerifyStoredFields { get; set; }

    /// <summary>Gets or sets a value indicating whether DocValues should be deeply validated.</summary>
    public bool VerifyDocValues { get; set; }

    /// <summary>Gets or sets a value indicating whether vector files should be deeply validated.</summary>
    public bool VerifyVectors { get; set; }

    /// <summary>Gets or sets a value indicating whether HNSW graph files should be deeply validated.</summary>
    public bool VerifyHnsw { get; set; }

    /// <summary>Gets or sets a value indicating whether live-doc files should be deeply validated.</summary>
    public bool VerifyLiveDocs { get; set; }

    /// <summary>Gets or sets a value indicating whether optional sidecar files should be checked when present.</summary>
    public bool IncludeOptionalSidecars { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether orphan files should be reported.</summary>
    public bool IncludeOrphans { get; set; }
}
