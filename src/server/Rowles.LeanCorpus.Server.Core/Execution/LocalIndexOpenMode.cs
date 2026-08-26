namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Specifies whether a local physical copy owns a writer.</summary>
public enum LocalIndexOpenMode
{
    /// <summary>Expose committed data for reads and installs only.</summary>
    ReadOnly,
    /// <summary>Expose reads and local writes.</summary>
    ReadWrite
}
