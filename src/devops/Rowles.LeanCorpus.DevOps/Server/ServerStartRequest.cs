using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Server;

internal sealed record ServerStartRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public string Framework { get; init; } = "net11.0";

    public string Configuration { get; init; } = "Debug";

    public bool NoBuild { get; init; }

    public bool NoRestore { get; init; }

    public bool External { get; init; }

    public IReadOnlyList<string> ApplicationArguments { get; init; } = [];
}

internal sealed record ServerStartResult(
    Rowles.LeanCorpus.DevOps.Terminal.CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    IReadOnlyList<string> Arguments,
    Rowles.LeanCorpus.DevOps.Artifacts.RunManifest? Manifest);
