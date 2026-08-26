namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Creates stream views over already materialised local payloads.</summary>
public static class LocalStreamAdapters
{
    /// <summary>Creates a non-copying read-only stream over memory-backed data.</summary>
    public static Stream ReadOnly(ReadOnlyMemory<byte> data) => new ReadOnlyMemoryStream(data);
}
