using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackageBuildRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public string Configuration { get; init; } = "Release";

    public bool CompressionOnly { get; init; }

    public bool NoBuild { get; init; }

    public bool Clean { get; init; }
}

internal sealed record PackageBuildResult(
    Rowles.LeanCorpus.DevOps.Terminal.CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    Rowles.LeanCorpus.DevOps.Artifacts.RunManifest? Manifest);
