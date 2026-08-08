using Rowles.LeanCorpus.Server.Core.Storage;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Associates an index registration with its live engine resources.</summary>
internal sealed class IndexRuntimeEntry(IndexRegistration registration, IndexRuntime runtime)
{
    internal IndexRegistration Registration { get; set; } = registration;

    internal IndexRuntime Runtime { get; } = runtime;
}
