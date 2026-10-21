using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Diagnostics;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Diagnostics;

public sealed class DiagnosticsCommandTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("not-a-pid")]
    [InlineData("999999999999999999999")]
    public void RejectsInvalidProcessIdentifiers(string processId)
    {
        var parsed = DiagnosticsArgumentParser.Parse(["trace", "--pid", processId], Environment.CurrentDirectory);

        Assert.False(parsed.Success);
        Assert.Contains("positive process ID", parsed.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesToolArgumentsAfterDelimiterAndNormalisesDumpType()
    {
        var parsed = DiagnosticsArgumentParser.Parse(
            ["dump", "-p", "42", "--type", "heap", "--", "--diag", "--foo=bar"],
            Environment.CurrentDirectory);

        Assert.True(parsed.Success, parsed.Failure);
        Assert.Equal(42, parsed.Request!.ProcessId);
        Assert.Equal("Heap", parsed.Request.DumpType);
        Assert.Equal(["--diag", "--foo=bar"], parsed.Request.PassThrough);
    }

    [Theory]
    [InlineData("0s")]
    [InlineData("-1s")]
    [InlineData("2")]
    [InlineData("NaNms")]
    public void RejectsInvalidCaptureDurations(string duration)
    {
        var parsed = DiagnosticsArgumentParser.Parse(
            ["capture", "--pid", "42", "--duration", duration],
            Environment.CurrentDirectory);

        Assert.False(parsed.Success);
        Assert.Contains("--duration must be a positive value", parsed.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildsBoundedCaptureInvocationsWithSeparateOutputFiles()
    {
        var runDirectory = Path.Combine(Path.GetTempPath(), "managed diagnostics capture");
        var parsed = DiagnosticsArgumentParser.Parse(
            ["capture", "--pid", "42", "--duration", "1500ms"],
            Environment.CurrentDirectory);
        Assert.True(parsed.Success, parsed.Failure);

        var invocations = DiagnosticsCommandService.BuildInvocations(parsed.Request!, runDirectory);

        Assert.Equal(2, invocations.Count);
        Assert.Equal("dotnet-counters", invocations[0].ToolName);
        Assert.Equal("counters.json", invocations[0].OutputName);
        Assert.Contains("--format", invocations[0].Arguments);
        Assert.Contains("json", invocations[0].Arguments);
        Assert.Equal(TimeSpan.FromSeconds(31.5), invocations[0].Timeout);
        Assert.Equal("dotnet-trace", invocations[1].ToolName);
        Assert.Equal("trace.nettrace", invocations[1].OutputName);
        Assert.Equal(TimeSpan.FromSeconds(31.5), invocations[1].Timeout);
    }

    [Fact]
    public async Task CaptureWritesRunEvidenceForBothDiagnosticTools()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new FakeProcessRunner();
        var service = CreateService(checkout, process);
        var parsed = DiagnosticsArgumentParser.Parse(
            ["capture", "--pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--duration", "1s"],
            checkout.Root);
        Assert.True(parsed.Success, parsed.Failure);

        var result = await service.RunAsync(new DiagnosticsCommandRequest(checkout.Identity, parsed.Request!), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Equal(RunState.Succeeded, result.Manifest!.State);
        Assert.Equal([0, 0], result.Manifest.ChildExitCodes);
        Assert.Equal(2, process.Requests.Count);
        Assert.Contains("counters.json", result.Manifest.Artifacts);
        Assert.Contains("trace.nettrace", result.Manifest.Artifacts);
        Assert.Contains("dotnet-counters.stdout.log", result.Manifest.Artifacts);
        Assert.Contains("dotnet-trace.stderr.log", result.Manifest.Artifacts);
        Assert.Equal("9.0.661903", result.Manifest.ToolVersions["diagnostic-dotnet-counters"]);
        using var writtenManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(result.RunDirectory!, "manifest.json")));
        Assert.Equal("succeeded", writtenManifest.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task CaptureFailsWhenEitherToolOmitsItsExpectedResult()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new FakeProcessRunner { OmitOutputFiles = true };
        var service = CreateService(checkout, process);
        var parsed = DiagnosticsArgumentParser.Parse(
            ["capture", "--pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            checkout.Root);
        Assert.True(parsed.Success, parsed.Failure);

        var result = await service.RunAsync(new DiagnosticsCommandRequest(checkout.Identity, parsed.Request!), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Equal(RunState.Failed, result.Manifest!.State);
        Assert.Equal(2, process.Requests.Count);
        Assert.Contains("did not produce 'counters.json'", result.Manifest.Failure, StringComparison.Ordinal);
        Assert.Contains("did not produce 'trace.nettrace'", result.Manifest.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureTimeoutProducesTimedOutRunState()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new FakeProcessRunner { FirstOutcome = OutcomeKind.Timeout };
        var service = CreateService(checkout, process);
        var parsed = DiagnosticsArgumentParser.Parse(
            ["capture", "--pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            checkout.Root);
        Assert.True(parsed.Success, parsed.Failure);

        var result = await service.RunAsync(new DiagnosticsCommandRequest(checkout.Identity, parsed.Request!), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Timeout, result.CommandResult.Outcome);
        Assert.Equal(RunState.TimedOut, result.Manifest!.State);
        Assert.Equal(2, process.Requests.Count);
    }

    [Fact]
    public async Task CaptureReturnsTheFailingSecondChildExitCode()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new FakeProcessRunner { SecondOutcome = OutcomeKind.CommandFailure };
        var service = CreateService(checkout, process);
        var parsed = DiagnosticsArgumentParser.Parse(
            ["capture", "--pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            checkout.Root);
        Assert.True(parsed.Success, parsed.Failure);

        var result = await service.RunAsync(new DiagnosticsCommandRequest(checkout.Identity, parsed.Request!), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.CommandFailure, result.CommandResult.Outcome);
        Assert.Equal(2, result.CommandResult.RawChildExitCode);
        Assert.Equal([0, 2], result.Manifest!.ChildExitCodes);
    }

    private static DiagnosticsCommandService CreateService(TemporaryCheckout checkout, FakeProcessRunner process)
    {
        var paths = new ArtifactPaths(checkout.Root);
        return new DiagnosticsCommandService(paths, new ArtifactRunStore(paths, runCategory: "diagnostics"), process,
            new FakeSdkResolver(), new FakeProvenanceReader());
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public bool OmitOutputFiles { get; init; }

        public OutcomeKind FirstOutcome { get; init; } = OutcomeKind.Success;

        public OutcomeKind SecondOutcome { get; init; } = OutcomeKind.Success;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            File.WriteAllText(request.StandardOutputLogPath, string.Empty);
            File.WriteAllText(request.StandardErrorLogPath, string.Empty);
            var outputIndex = Array.IndexOf(request.Arguments.ToArray(), "--output");
            if (!OmitOutputFiles && outputIndex >= 0 && outputIndex + 1 < request.Arguments.Count)
            {
                var outputPath = request.Arguments[outputIndex + 1];
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                File.WriteAllText(outputPath, "diagnostic result");
            }

            var outcome = Requests.Count == 1 ? FirstOutcome : SecondOutcome;
            var exitCode = outcome switch
            {
                OutcomeKind.Success => 0,
                OutcomeKind.Timeout or OutcomeKind.Cancelled => (int?)null,
                _ => 2
            };
            return Task.FromResult(new ProcessResult(outcome, exitCode, [], [], 0, 0,
                TimeSpan.FromSeconds(1), request.StandardOutputLogPath, request.StandardErrorLogPath,
                outcome == OutcomeKind.Success ? null : "fake diagnostic timeout"));
        }
    }

    private sealed class FakeSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.100", "11.0.100", "latestFeature", null));
    }

    private sealed class FakeProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "abc123", "issue-97", false, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root)
        {
            Root = root;
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, ".config"));
            File.WriteAllText(Path.Combine(root, ".config", "dotnet-tools.json"), """
                {
                  "version": 1,
                  "isRoot": true,
                  "tools": {
                    "dotnet-counters": { "version": "9.0.661903", "commands": ["dotnet-counters"] },
                    "dotnet-trace": { "version": "9.0.661903", "commands": ["dotnet-trace"] }
                  }
                }
                """);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"Managed diagnostics checkout {Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TemporaryCheckout(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
