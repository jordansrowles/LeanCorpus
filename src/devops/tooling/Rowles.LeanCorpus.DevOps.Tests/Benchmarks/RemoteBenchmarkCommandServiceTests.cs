using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class RemoteBenchmarkCommandServiceTests
{
    [Fact]
    public async Task StartCancelAndStatusRetainRemoteIdentityAndDoNotClaimUnconfirmedCancellation()
    {
        using var checkout = new TemporaryCheckout();
        var paths = new ArtifactPaths(checkout.Root);
        var processRunner = new RecordingProcessRunner(["running", "cancel-requested", "succeeded"]);
        var runStore = new ArtifactRunStore(paths, runCategory: "benchmark");
        var service = new RemoteBenchmarkCommandService(paths, runStore, processRunner, new RecordingProvenanceReader());
        var benchmarkArguments = new[] { "--suite", "query", "--strat", "fast", "--", "--filter", "*Term Query*" };

        var started = await service.RunAsync(checkout.Identity,
            new BenchmarkRemoteCommandOptions("start", "jordan@bench", "/srv/LeanCorpus's benchmarks", "issue-97",
                "nightly", null, benchmarkArguments), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, started.CommandResult.Outcome);
        var runId = Assert.IsType<Guid>(started.RunId);
        var remoteRecordPath = Path.Combine(started.RunDirectory!, "remote-run.json");
        var remoteRecord = JsonSerializer.Deserialize<RemoteBenchmarkRunRecord>(File.ReadAllText(remoteRecordPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(remoteRecord);
        Assert.Equal("running", remoteRecord.State);
        Assert.Equal("issue-97", remoteRecord.Branch);
        Assert.Equal("remote-commit", remoteRecord.RemoteSourceCommit);

        var startScript = Assert.IsType<string>(Assert.Single(processRunner.Requests).StandardInputText);
        Assert.Contains("branch='issue-97'", startScript, StringComparison.Ordinal);
        Assert.Contains("devops\" benchmark", startScript, StringComparison.Ordinal);
        Assert.Contains("'--filter' '*Term Query*'", startScript, StringComparison.Ordinal);
        Assert.Contains("'\"'\"'", startScript, StringComparison.Ordinal);
        Assert.DoesNotContain("pwsh", startScript, StringComparison.OrdinalIgnoreCase);

        var cancel = await service.RunAsync(checkout.Identity,
            new BenchmarkRemoteCommandOptions("cancel", null, null, null, null, runId, []), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, cancel.CommandResult.Outcome);
        Assert.Contains("not confirmed", cancel.CommandResult.Message, StringComparison.Ordinal);
        remoteRecord = JsonSerializer.Deserialize<RemoteBenchmarkRunRecord>(File.ReadAllText(remoteRecordPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(remoteRecord);
        Assert.True(remoteRecord.CancellationRequested);
        Assert.False(remoteRecord.RemoteTerminationConfirmed);
        Assert.Contains("tmux send-keys", processRunner.Requests[1].StandardInputText, StringComparison.Ordinal);

        var status = await service.RunAsync(checkout.Identity,
            new BenchmarkRemoteCommandOptions("status", null, null, null, null, runId, []), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, status.CommandResult.Outcome);
        Assert.Contains("succeeded", status.CommandResult.Message, StringComparison.Ordinal);
        remoteRecord = JsonSerializer.Deserialize<RemoteBenchmarkRunRecord>(File.ReadAllText(remoteRecordPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(remoteRecord);
        Assert.True(remoteRecord.RemoteTerminationConfirmed);
        Assert.Equal("succeeded", remoteRecord.State);
    }

    [Fact]
    public async Task SshDisconnectRecordsUnknownRemoteLifetime()
    {
        using var checkout = new TemporaryCheckout();
        var paths = new ArtifactPaths(checkout.Root);
        var processRunner = new RecordingProcessRunner([], OutcomeKind.InfrastructureFailure);
        var service = new RemoteBenchmarkCommandService(paths, new ArtifactRunStore(paths, runCategory: "benchmark"),
            processRunner, new RecordingProvenanceReader());

        var result = await service.RunAsync(checkout.Identity,
            new BenchmarkRemoteCommandOptions("start", "jordan@bench", "/srv/leancorpus", "issue-97", null, null, []),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Contains("may still be running", result.CommandResult.Message, StringComparison.Ordinal);
        var remoteRecord = JsonSerializer.Deserialize<RemoteBenchmarkRunRecord>(
            File.ReadAllText(Path.Combine(result.RunDirectory!, "remote-run.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(remoteRecord);
        Assert.Equal("unknown", remoteRecord.State);
        Assert.False(remoteRecord.RemoteTerminationConfirmed);
        Assert.Contains("ssh disconnected", remoteRecord.Failure, StringComparison.Ordinal);
    }

    private sealed class RecordingProcessRunner(IReadOnlyList<string> states, OutcomeKind outcome = OutcomeKind.Success) : IProcessRunner
    {
        private int _nextState;

        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            var match = Regex.Match(request.StandardInputText ?? string.Empty,
                "run_id='(?<id>[0-9a-fA-F-]{36})'", RegexOptions.CultureInvariant);
            var runId = match.Success ? Guid.Parse(match.Groups["id"].Value) : Guid.Empty;
            var state = _nextState < states.Count ? states[_nextState++] : "unknown";
            var response = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                runId,
                state,
                session = "nightly",
                sourceCommit = "remote-commit"
            });
            var stdoutLine = outcome == OutcomeKind.Success ? "LC_REMOTE_STATE:" + response : string.Empty;
            File.WriteAllText(request.StandardOutputLogPath, stdoutLine.Length == 0 ? string.Empty : stdoutLine + Environment.NewLine);
            File.WriteAllText(request.StandardErrorLogPath, outcome == OutcomeKind.Success ? string.Empty : "ssh disconnected" + Environment.NewLine);
            return Task.FromResult(new ProcessResult(outcome,
                outcome == OutcomeKind.Success ? 0 : null,
                stdoutLine.Length == 0 ? [] : [stdoutLine],
                outcome == OutcomeKind.Success ? [] : ["ssh disconnected"],
                stdoutLine.Length == 0 ? 0 : 1,
                outcome == OutcomeKind.Success ? 0 : 1,
                TimeSpan.FromMilliseconds(20),
                request.StandardOutputLogPath,
                request.StandardErrorLogPath,
                outcome == OutcomeKind.Success ? null : "ssh disconnected"));
        }
    }

    private sealed class RecordingProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "local-commit", "issue-97", true, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        public TemporaryCheckout()
        {
            Root = Path.Combine(Path.GetTempPath(), "LeanCorpus remote benchmark "+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            var gitPath = Path.Combine(Root, ".git");
            Directory.CreateDirectory(gitPath);
            Identity = new CheckoutIdentity(Root, gitPath, CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
