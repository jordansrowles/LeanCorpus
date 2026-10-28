using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed record SdkResolution(
    OutcomeKind Outcome,
    string? RequestedVersion,
    string? SelectedVersion,
    string? RollForward,
    string? Failure,
    string? SdkDirectory = null);

internal interface IDotNetSdkResolver
{
    Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken);
}

internal sealed class DotNetSdkResolver(IProcessRunner processRunner, string dotnetExecutable = "dotnet") : IDotNetSdkResolver
{
    public async Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        var policyPath = Path.Combine(checkout.RepositoryRoot, "global.json");
        if (!File.Exists(policyPath))
        {
            return new SdkResolution(OutcomeKind.ValidationFailure, null, null, null, $"SDK policy file '{policyPath}' is missing.");
        }

        string? requestedVersion;
        string? rollForward;
        try
        {
            using var policy = JsonDocument.Parse(await File.ReadAllBytesAsync(policyPath, cancellationToken).ConfigureAwait(false));
            var sdk = policy.RootElement.GetProperty("sdk");
            requestedVersion = sdk.GetProperty("version").GetString();
            rollForward = sdk.TryGetProperty("rollForward", out var rollForwardElement) ? rollForwardElement.GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new SdkResolution(OutcomeKind.ValidationFailure, null, null, null, $"SDK policy file '{policyPath}' is invalid: {exception.Message}");
        }

        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return new SdkResolution(OutcomeKind.ValidationFailure, requestedVersion, null, rollForward, "global.json must specify sdk.version.");
        }

        var process = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["--version"],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, "sdk-version.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, "sdk-version.stderr.log"),
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken).ConfigureAwait(false);

        if (process.Outcome != OutcomeKind.Success)
        {
            return new SdkResolution(process.Outcome, requestedVersion, null, rollForward,
                $"Could not resolve the SDK selected by global.json. {process.Failure}");
        }

        var selectedVersion = process.StandardOutputTail.LastOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(selectedVersion) || !Regex.IsMatch(selectedVersion, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.-]+)?$", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout))
        {
            return new SdkResolution(OutcomeKind.InfrastructureFailure, requestedVersion, selectedVersion, rollForward,
                "The dotnet --version response was empty or did not contain a valid SDK version.");
        }

        var sdkList = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["--list-sdks"],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, "sdk-list.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, "sdk-list.stderr.log"),
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken).ConfigureAwait(false);

        if (sdkList.Outcome != OutcomeKind.Success)
        {
            return new SdkResolution(sdkList.Outcome, requestedVersion, selectedVersion, rollForward,
                $"The selected SDK could not be located for MSBuild evaluation. {sdkList.Failure}");
        }

        var sdkDirectory = ParseSdkDirectory(sdkList.StandardOutputTail, selectedVersion);
        if (sdkDirectory is null)
        {
            return new SdkResolution(OutcomeKind.InfrastructureFailure, requestedVersion, selectedVersion, rollForward,
                "dotnet --list-sdks did not identify the SDK selected by dotnet --version.");
        }

        return new SdkResolution(OutcomeKind.Success, requestedVersion, selectedVersion, rollForward, null, sdkDirectory);
    }

    private static string? ParseSdkDirectory(IEnumerable<string> lines, string version)
    {
        foreach (var line in lines)
        {
            var separator = line.IndexOf(" [", StringComparison.Ordinal);
            if (separator < 0 || !line.EndsWith(']'))
            {
                continue;
            }

            if (!string.Equals(line[..separator].Trim(), version, StringComparison.Ordinal))
            {
                continue;
            }

            var baseDirectory = line[(separator + 2)..^1];
            var sdkPath = Path.Combine(baseDirectory, version);
            return File.Exists(Path.Combine(sdkPath, "MSBuild.dll")) ? Path.GetFullPath(sdkPath) : null;
        }

        return null;
    }
}
