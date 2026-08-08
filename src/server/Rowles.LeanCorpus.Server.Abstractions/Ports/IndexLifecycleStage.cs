namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Identifies the index lifecycle transition stage.</summary>
public enum IndexLifecycleStage
{
    /// <summary>An index is being created.</summary>
    Creating,
    /// <summary>An index was created.</summary>
    Created,
    /// <summary>An index is being deleted.</summary>
    Deleting,
    /// <summary>An index was deleted.</summary>
    Deleted,
    /// <summary>An index is opening.</summary>
    Opening,
    /// <summary>An index is closing.</summary>
    Closing
}
