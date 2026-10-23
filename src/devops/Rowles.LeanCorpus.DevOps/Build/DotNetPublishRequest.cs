using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed record DotNetPublishRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public required string ProjectPath { get; init; }

    public string Configuration { get; init; } = "Release";

    public string? Framework { get; init; }

    public string? RuntimeIdentifier { get; init; }

    public required string OutputPath { get; init; }

    public bool NoRestore { get; init; }

    public bool SelfContained { get; init; }

    public bool SingleFile { get; init; }

    public bool IncludeNativeLibrariesForSelfExtract { get; init; }

    public bool SuppressDebugSymbols { get; init; }

    public bool RestoreLockedMode { get; init; }

    public bool ReleaseArchiveProfile { get; init; }

    public bool PublishAot { get; init; }

    public bool PublishTrimmed { get; init; }
}

internal sealed record DotNetPublishResult(
    Rowles.LeanCorpus.DevOps.Terminal.CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    IReadOnlyList<string> Arguments,
    Rowles.LeanCorpus.DevOps.Artifacts.RunManifest? Manifest);
