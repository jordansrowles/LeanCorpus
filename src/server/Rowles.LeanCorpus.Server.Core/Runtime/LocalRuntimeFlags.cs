namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Orthogonal local runtime properties maintained atomically.</summary>
[Flags]
internal enum LocalRuntimeFlags
{
    None = 0,
    Draining = 1,
    Degraded = 2,
    Installing = 4
}
