using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Owns local physical index paths and runtime handles.</summary>
public interface ILocalIndexStore : IAsyncDisposable
{
    /// <summary>Creates a new physical index.</summary>
    ValueTask<LocalIndexHandle> CreateAsync(LocalIndexDescriptor descriptor, LocalIndexOpenMode mode = LocalIndexOpenMode.ReadWrite, CancellationToken cancellationToken = default);

    /// <summary>Opens an existing physical index.</summary>
    ValueTask<LocalIndexHandle> OpenAsync(LocalIndexDescriptor descriptor, LocalIndexOpenMode mode, CancellationToken cancellationToken = default);

    /// <summary>Deletes a physical index owned by this store.</summary>
    ValueTask DeleteAsync(PhysicalIndexId id, CancellationToken cancellationToken = default);

    /// <summary>Checks whether a physical index exists.</summary>
    bool Exists(PhysicalIndexId id);

    /// <summary>Lists locally owned physical identities.</summary>
    IReadOnlyList<PhysicalIndexId> List();
}
