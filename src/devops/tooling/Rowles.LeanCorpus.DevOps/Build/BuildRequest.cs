using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed record BuildLoggingPolicy(bool BinaryLog = true, bool TextLog = true);

internal sealed record BuildRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public string? ProjectPath { get; init; }

    public string Configuration { get; init; } = "Release";

    public string? Framework { get; init; }

    public string? RuntimeIdentifier { get; init; }

    public bool NoRestore { get; init; }

    public BuildLoggingPolicy LoggingPolicy { get; init; } = new();
}

internal sealed record BuildResult(
    Rowles.LeanCorpus.DevOps.Terminal.CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    IReadOnlyList<string> Arguments,
    Rowles.LeanCorpus.DevOps.Artifacts.RunManifest? Manifest);
