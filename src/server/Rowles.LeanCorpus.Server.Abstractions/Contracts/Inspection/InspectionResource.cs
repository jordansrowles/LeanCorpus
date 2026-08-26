namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

/// <summary>Identifies a bounded inspection resource.</summary>
public enum InspectionResource
{
    /// <summary>Index inventory.</summary>
    IndexInventory,
    /// <summary>Reader state.</summary>
    ReaderState,
    /// <summary>Declared field mappings.</summary>
    Fields,
    /// <summary>Bounded segment metadata.</summary>
    Segments,
    /// <summary>Term dictionary.</summary>
    Terms,
    /// <summary>Posting lists.</summary>
    Postings,
    /// <summary>Analysis output.</summary>
    Analysis,
    /// <summary>Vector graph state.</summary>
    VectorGraph,
    /// <summary>Bounded documents.</summary>
    Documents,
    /// <summary>Storage state.</summary>
    Storage,
    /// <summary>Enterprise topology.</summary>
    EnterpriseTopology
}
