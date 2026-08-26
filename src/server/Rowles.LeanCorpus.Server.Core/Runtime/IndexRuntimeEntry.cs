using Rowles.LeanCorpus.Server.Core.Storage;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Associates an index registration with its live engine resources.</summary>
internal sealed class IndexRuntimeEntry(IndexRegistration registration, LocalIndexHandle handle)
{
    internal IndexRegistration Registration { get; set; } = registration;

    internal LocalIndexHandle Handle { get; } = handle;

    internal IndexRuntime Runtime => Handle.Runtime;
}
