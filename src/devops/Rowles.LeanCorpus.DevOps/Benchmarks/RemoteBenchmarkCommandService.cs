using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record RemoteBenchmarkCommandResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory);

internal sealed record RemoteBenchmarkRunRecord
{
    public required int SchemaVersion { get; init; }
    public required Guid RunId { get; init; }
    public required string RemoteHost { get; init; }
    public required string RemotePath { get; init; }
    public required string Branch { get; init; }
    public required string SessionName { get; init; }
    public required string State { get; init; }
    public required DateTimeOffset UpdatedUtc { get; init; }
    public string? RemoteSourceCommit { get; init; }
    public bool RemoteTerminationConfirmed { get; init; }
    public bool CancellationRequested { get; init; }
    public string? Failure { get; init; }
}

internal sealed class RemoteBenchmarkCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress = null,
    string sshExecutable = "ssh")
{
    private const int SupportedSchemaVersion = 1;
    private static readonly TimeSpan SshLimit = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex HostPattern = new("^[A-Za-z0-9_.@:-]+$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SessionPattern = new("^[A-Za-z0-9_-]{1,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<RemoteBenchmarkCommandResult> RunAsync(
        CheckoutIdentity checkout,
        BenchmarkRemoteCommandOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(options);
        var action = options.Action.Trim().ToLowerInvariant();
        if (action == "start")
            return await StartAsync(checkout, options, cancellationToken).ConfigureAwait(false);
        if (action is "status" or "cancel")
            return await ControlAsync(checkout, options, action, cancellationToken).ConfigureAwait(false);
        return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure,
            $"Unknown remote benchmark action '{options.Action}'. Valid actions are start, status and cancel."), null, null);
    }

    private async Task<RemoteBenchmarkCommandResult> StartAsync(
        CheckoutIdentity checkout,
        BenchmarkRemoteCommandOptions options,
        CancellationToken cancellationToken)
    {
        var host = options.Host ?? "jordan@debian";
        var remotePath = options.RemotePath ?? "/home/jordan/code/leancorpus";
        var branch = options.Branch ?? "main";
        var sessionName = options.SessionName ?? "lc-bench-" + Guid.NewGuid().ToString("N");
        var validation = ValidateStartOptions(host, remotePath, branch, sessionName, options.BenchmarkArguments);
        if (validation is not null)
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, validation), null, null);

        var manifest = await runStore.StartAsync("benchmark remote start", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["remoteHost"] = host,
            ["remotePath"] = remotePath,
            ["remoteBranch"] = branch,
            ["remoteSession"] = sessionName,
            ["benchmarkArguments"] = JsonSerializer.Serialize(options.BenchmarkArguments)
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("benchmark", manifest.RunId);
        var statePath = Path.Combine(runDirectory, "remote-run.json");
        var record = new RemoteBenchmarkRunRecord
        {
            SchemaVersion = SupportedSchemaVersion,
            RunId = manifest.RunId,
            RemoteHost = host,
            RemotePath = remotePath,
            Branch = branch,
            SessionName = sessionName,
            State = RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Starting),
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        await WriteRecordAsync(statePath, record, CancellationToken.None).ConfigureAwait(false);
        var stdout = Path.Combine(runDirectory, "ssh-start.stdout.log");
        var stderr = Path.Combine(runDirectory, "ssh-start.stderr.log");
        var artifacts = new List<string> { "remote-run.json", "ssh-start.stdout.log", "ssh-start.stderr.log" };
        var childExitCodes = new List<int>();
        try
        {
            var provenance = await provenanceReader.ReadAsync(checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success)
                throw new InvalidOperationException(provenance.Failure ?? "Could not capture local Git provenance.");
            manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ssh-executable"] = sshExecutable },
                cancellationToken).ConfigureAwait(false);

            progress?.Invoke($"Connecting to remote benchmark host {host}.");
            var process = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = sshExecutable,
                Arguments = ["-T", host, "bash", "-s"],
                WorkingDirectory = artifactPaths.RepositoryRoot,
                StandardInputText = CreateStartScript(manifest.RunId, remotePath, branch, sessionName, options.BenchmarkArguments),
                StandardOutputLogPath = stdout,
                StandardErrorLogPath = stderr,
                Timeout = SshLimit,
                ProgressInterval = TimeSpan.FromSeconds(10),
                Progress = value => progress?.Invoke($"Remote benchmark setup is still running ({value.Elapsed:mm\\:ss}).")
            }, cancellationToken).ConfigureAwait(false);
            if (process.RawExitCode is { } childExitCode)
                childExitCodes.Add(childExitCode);

            var response = process.Outcome == OutcomeKind.Success ? ReadRemoteResponse(stdout) : null;
            if (process.Outcome == OutcomeKind.Success && response is not null && response.RunId == manifest.RunId &&
                RemoteBenchmarkStateCatalog.IsStartResponse(response.State))
            {
                record = record with
                {
                    State = response.State,
                    RemoteSourceCommit = response.SourceCommit,
                    RemoteTerminationConfirmed = RemoteBenchmarkStateCatalog.IsTerminal(response.State),
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                await WriteRecordAsync(statePath, record, CancellationToken.None).ConfigureAwait(false);
                var finished = await runStore.FinishAsync(manifest, RunState.Succeeded,
                    childExitCode: process.RawExitCode, artifacts: artifacts, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.Success,
                    $"Remote benchmark {finished.RunId:D} {record.State}; session '{sessionName}' on {host}. " +
                    (record.RemoteTerminationConfirmed ? "Remote completion is confirmed." : "Use 'benchmark remote status' to check completion."),
                    Details: runDirectory), finished.RunId, runDirectory);
            }

            record = record with
            {
                // SSH can disconnect after tmux accepted the command. Without a
                // valid response the remote worker's lifetime is unknown.
                State = RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Unknown),
                RemoteTerminationConfirmed = false,
                Failure = process.Failure ?? string.Join(Environment.NewLine, process.StandardErrorTail),
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            await WriteRecordAsync(statePath, record, CancellationToken.None).ConfigureAwait(false);
            var outcome = process.Outcome == OutcomeKind.Success ? OutcomeKind.InfrastructureFailure : process.Outcome;
            var state = ToRunState(outcome);
            var terminal = await runStore.FinishAsync(manifest, state, childExitCode: process.RawExitCode,
                artifacts: artifacts, failure: "Remote setup did not return a confirmed run identity; remote state is unknown and the task may still be running. " +
                    (record.Failure ?? "No remote status response was received."), cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new RemoteBenchmarkCommandResult(new CommandResult(outcome,
                $"SSH ended before remote state was confirmed. Run {terminal.RunId:D} may still be running remotely.",
                Details: runDirectory), terminal.RunId, runDirectory);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            record = record with { State = RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Unknown), RemoteTerminationConfirmed = false, Failure = exception.Message, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteRecordAsync(statePath, record, CancellationToken.None).ConfigureAwait(false);
            await runStore.FinishAsync(manifest, RunState.Cancelled, artifacts: artifacts, failure:
                "Local SSH orchestration was cancelled; remote state is unknown and the task may still be running.",
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.Cancelled,
                $"Remote run {manifest.RunId:D} may still be active. Check it with 'benchmark remote status --run-id {manifest.RunId:D}'.",
                Details: runDirectory), manifest.RunId, runDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            record = record with { State = RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Unknown), RemoteTerminationConfirmed = false, Failure = exception.Message, UpdatedUtc = DateTimeOffset.UtcNow };
            await WriteRecordAsync(statePath, record, CancellationToken.None).ConfigureAwait(false);
            var terminal = await runStore.FinishAsync(manifest, RunState.Failed, artifacts: artifacts,
                failure: exception.Message, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                $"Could not start the remote benchmark. Run {terminal.RunId:D} has remote state unknown.", Details: runDirectory), terminal.RunId, runDirectory);
        }
    }

    private async Task<RemoteBenchmarkCommandResult> ControlAsync(
        CheckoutIdentity checkout,
        BenchmarkRemoteCommandOptions options,
        string action,
        CancellationToken cancellationToken)
    {
        if (options.RunId is not { } targetRunId || targetRunId == Guid.Empty)
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure,
                $"'benchmark remote {action}' requires --run-id."), null, null);
        if (options.Host is not null || options.RemotePath is not null || options.Branch is not null || options.SessionName is not null || options.BenchmarkArguments.Count != 0)
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure,
                $"'benchmark remote {action}' uses the remote identity recorded for run {targetRunId:D}; start-only options are not valid."), null, null);

        var targetDirectory = artifactPaths.GetRunDirectory("benchmark", targetRunId);
        var targetStatePath = Path.Combine(targetDirectory, "remote-run.json");
        var target = await ReadRecordAsync(targetStatePath, targetRunId, cancellationToken).ConfigureAwait(false);
        if (target is null)
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure,
                $"Remote benchmark run '{targetRunId:D}' was not found in this checkout."), null, null);

        var manifest = await runStore.StartAsync($"benchmark remote {action}", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["remoteRunId"] = targetRunId.ToString("D"),
            ["remoteHost"] = target.RemoteHost,
            ["remoteSession"] = target.SessionName
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("benchmark", manifest.RunId);
        var stdout = Path.Combine(runDirectory, $"ssh-{action}.stdout.log");
        var stderr = Path.Combine(runDirectory, $"ssh-{action}.stderr.log");
        var artifacts = new List<string> { $"ssh-{action}.stdout.log", $"ssh-{action}.stderr.log" };
        var script = action == "status" ? CreateStatusScript(target) : CreateCancelScript(target);
        var provenance = await provenanceReader.ReadAsync(checkout, runDirectory, cancellationToken).ConfigureAwait(false);
        if (provenance.Outcome != OutcomeKind.Success)
        {
            await runStore.FinishAsync(manifest, RunState.Failed, failure: provenance.Failure,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                provenance.Failure ?? "Could not capture local Git provenance."), manifest.RunId, runDirectory);
        }
        manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var process = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = sshExecutable,
            Arguments = ["-T", target.RemoteHost, "bash", "-s"],
            WorkingDirectory = artifactPaths.RepositoryRoot,
            StandardInputText = script,
            StandardOutputLogPath = stdout,
            StandardErrorLogPath = stderr,
            Timeout = SshLimit,
            ProgressInterval = TimeSpan.FromSeconds(10),
            Progress = value => progress?.Invoke($"Remote {action} check is still running ({value.Elapsed:mm\\:ss}).")
        }, cancellationToken).ConfigureAwait(false);
        if (process.RawExitCode is { } childExitCode)
        {
            var response = process.Outcome == OutcomeKind.Success ? ReadRemoteResponse(stdout) : null;
            if (response is not null && response.RunId == targetRunId)
            {
                var terminal = RemoteBenchmarkStateCatalog.IsTerminal(response.State);
                target = target with
                {
                    State = response.State,
                    RemoteSourceCommit = response.SourceCommit ?? target.RemoteSourceCommit,
                    RemoteTerminationConfirmed = terminal,
                    CancellationRequested = (action == "cancel" && response.State == RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.CancelRequested)) || target.CancellationRequested,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Failure = null
                };
                await WriteRecordAsync(targetStatePath, target, CancellationToken.None).ConfigureAwait(false);
                await WriteRecordAsync(Path.Combine(runDirectory, "remote-run-state.json"), target, CancellationToken.None).ConfigureAwait(false);
                artifacts.Add("remote-run-state.json");
                var finished = await runStore.FinishAsync(manifest, RunState.Succeeded, childExitCode: childExitCode,
                    artifacts: artifacts, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                var message = action == "cancel"
                    ? target.RemoteTerminationConfirmed
                        ? $"Remote benchmark {targetRunId:D} is {target.State}; termination is confirmed."
                        : target.State == RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.CancelRequested)
                            ? $"Cancellation was requested for remote benchmark {targetRunId:D}; remote termination is not confirmed."
                            : $"Remote benchmark {targetRunId:D} is {target.State}; no terminal state is confirmed."
                    : $"Remote benchmark {targetRunId:D} is {target.State}.";
                return new RemoteBenchmarkCommandResult(new CommandResult(OutcomeKind.Success, message,
                    Details: runDirectory), finished.RunId, runDirectory);
            }
        }

        target = target with
        {
            State = RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Unknown),
            RemoteTerminationConfirmed = false,
            CancellationRequested = (action == "cancel" && target.State == RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.CancelRequested)) || target.CancellationRequested,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Failure = process.Failure ?? string.Join(Environment.NewLine, process.StandardErrorTail)
        };
        await WriteRecordAsync(targetStatePath, target, CancellationToken.None).ConfigureAwait(false);
        await WriteRecordAsync(Path.Combine(runDirectory, "remote-run-state.json"), target, CancellationToken.None).ConfigureAwait(false);
        artifacts.Add("remote-run-state.json");
        var commandOutcome = process.Outcome == OutcomeKind.Success ? OutcomeKind.InfrastructureFailure : process.Outcome;
        var runState = ToRunState(commandOutcome);
        var finishedAction = await runStore.FinishAsync(manifest, runState, childExitCode: process.RawExitCode,
            artifacts: artifacts, failure: action == "cancel"
                ? "Cancellation could not be confirmed; remote state is unknown."
                : "Remote status could not be confirmed.", cancellationToken: CancellationToken.None).ConfigureAwait(false);
        return new RemoteBenchmarkCommandResult(new CommandResult(commandOutcome,
            $"Could not confirm remote state for benchmark {targetRunId:D}; it may still be active.", Details: runDirectory),
            finishedAction.RunId, runDirectory);
    }

    private static string? ValidateStartOptions(string host, string remotePath, string branch, string sessionName, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(host) || !HostPattern.IsMatch(host) || host.StartsWith("-", StringComparison.Ordinal))
            return "Remote SSH host must be a host alias or user@host without whitespace or option prefixes.";
        if (string.IsNullOrWhiteSpace(remotePath) || !remotePath.StartsWith("/", StringComparison.Ordinal) || remotePath.Any(char.IsControl))
            return "Remote repository path must be an absolute POSIX path without control characters.";
        if (string.IsNullOrWhiteSpace(branch) || branch.StartsWith("-", StringComparison.Ordinal) || branch.Any(char.IsControl))
            return "Remote branch must be a non-empty Git branch name and cannot begin with '-'.";
        if (!SessionPattern.IsMatch(sessionName))
            return "Remote session name may contain only letters, digits, underscores and hyphens (up to 80 characters).";
        if (arguments.Any(static argument => argument is null || argument.Contains('\0')))
            return "Benchmark arguments cannot contain null values or NUL characters.";
        if (arguments.Any(static argument => argument.Equals("--repo", StringComparison.OrdinalIgnoreCase) || argument.Equals("-Repo", StringComparison.OrdinalIgnoreCase)))
            return "Remote benchmark arguments cannot override the selected repository.";
        return null;
    }

    private static string CreateStartScript(Guid runId, string remotePath, string branch, string sessionName, IReadOnlyList<string> benchmarkArguments)
    {
        var runIdText = runId.ToString("D");
        var quotedArguments = string.Join(' ', benchmarkArguments.Select(QuotePosix));
        var worker = $$"""
            #!/usr/bin/env bash
            set -uo pipefail
            repo={{QuotePosix(remotePath)}}
            state_dir="$repo/artifacts/benchmark/runs/{{runIdText}}"
            state_file="$state_dir/remote-run.json"
            run_id={{QuotePosix(runIdText)}}
            session={{QuotePosix(sessionName)}}
            source_commit="$1"
            shift
            cancel_requested=false
            write_state() {
              local state="$1"
              local exit_code="$2"
              local temporary="$state_file.tmp"
              printf '{"schemaVersion":1,"runId":"%s","state":"%s","session":"%s","sourceCommit":"%s","exitCode":%s,"cancelRequested":%s}\n' \
                "$run_id" "$state" "$session" "$source_commit" "$exit_code" "$cancel_requested" > "$temporary"
              mv -f "$temporary" "$state_file"
            }
            on_signal() {
              cancel_requested=true
              if [[ -n "${child_pid:-}" ]]; then kill -INT "$child_pid" 2>/dev/null || true; fi
            }
            trap on_signal INT TERM
            write_state {{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Running)}} null
            cd "$repo"
            benchmark_arguments=( {{quotedArguments}} )
            "$repo/devops" benchmark "${benchmark_arguments[@]}" > "$state_dir/benchmark.stdout.log" 2> "$state_dir/benchmark.stderr.log" &
            child_pid=$!
            wait "$child_pid"
            exit_code=$?
            if [[ "$cancel_requested" == true || "$exit_code" -eq 130 || "$exit_code" -eq 143 ]]; then
              write_state {{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Cancelled)}} "$exit_code"
            elif [[ "$exit_code" -eq 0 ]]; then
              write_state {{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Succeeded)}} 0
            else
              write_state {{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Failed)}} "$exit_code"
            fi
            exit 0
            """;
        var sessionQuoted = QuotePosix(sessionName);
        var branchQuoted = QuotePosix(branch);
        return $$"""
            set -euo pipefail
            repo={{QuotePosix(remotePath)}}
            branch={{branchQuoted}}
            run_id={{QuotePosix(runIdText)}}
            session={{sessionQuoted}}
            state_dir="$repo/artifacts/benchmark/runs/$run_id"
            state_file="$state_dir/remote-run.json"
            if ! command -v tmux >/dev/null 2>&1; then echo 'tmux is required on the remote host.' >&2; exit 3; fi
            if [[ ! -d "$repo/.git" && ! -f "$repo/.git" ]]; then echo 'Remote repository path is not a Git checkout.' >&2; exit 2; fi
            if [[ -n "$(git -C "$repo" status --porcelain=v1 --untracked-files=all)" ]]; then echo 'Remote checkout has local changes; refusing to update it.' >&2; exit 2; fi
            git check-ref-format --branch "$branch" >/dev/null
            if tmux has-session -t "$session" 2>/dev/null; then echo 'Remote tmux session already exists.' >&2; exit 2; fi
            git -C "$repo" fetch --quiet origin "$branch"
            if git -C "$repo" show-ref --verify --quiet "refs/heads/$branch"; then
              git -C "$repo" checkout --quiet "$branch"
            else
              git -C "$repo" checkout --quiet -b "$branch" --track "origin/$branch"
            fi
            git -C "$repo" merge --ff-only "origin/$branch" >/dev/null
            source_commit="$(git -C "$repo" rev-parse HEAD)"
            origin_commit="$(git -C "$repo" rev-parse "origin/$branch")"
            if [[ "$source_commit" != "$origin_commit" ]]; then echo 'Remote branch contains commits not present on origin; refusing to benchmark an unpublished revision.' >&2; exit 2; fi
            if [[ ! -x "$repo/devops" ]]; then echo 'Managed ./devops apphost is missing; run the repository setup workflow on the remote host.' >&2; exit 3; fi
            if ! "$repo/devops" benchmark --list >/dev/null 2>&1; then echo 'Managed ./devops apphost is stale or cannot run; refresh it with the repository setup workflow.' >&2; exit 3; fi
            mkdir -p "$state_dir"
            worker="$state_dir/remote-worker.sh"
            worker_tmp="$worker.tmp"
            cat > "$worker_tmp" <<'LEAN_CORPUS_WORKER'
            {{worker}}
            LEAN_CORPUS_WORKER
            sed -i 's/^            //' "$worker_tmp"
            chmod 700 "$worker_tmp"
            mv -f "$worker_tmp" "$worker"
            printf '{"schemaVersion":1,"runId":"%s","state":"{{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Starting)}}","session":"%s","sourceCommit":"%s","exitCode":null,"cancelRequested":false}\n' "$run_id" "$session" "$source_commit" > "$state_file.tmp"
            mv -f "$state_file.tmp" "$state_file"
            tmux new-session -d -s "$session" bash "$worker" "$source_commit"
            response="$(cat "$state_file")"
            printf 'LC_REMOTE_STATE:%s\n' "$response"
            """;
    }

    private static string CreateStatusScript(RemoteBenchmarkRunRecord record)
    {
        var remoteStatePath = $"{record.RemotePath.TrimEnd('/')}/artifacts/benchmark/runs/{record.RunId:D}/remote-run.json";
        return $$"""
            set -euo pipefail
            state_file={{QuotePosix(remoteStatePath)}}
            session={{QuotePosix(record.SessionName)}}
            run_id={{QuotePosix(record.RunId.ToString("D"))}}
            if [[ -r "$state_file" ]]; then
              response="$(cat "$state_file")"
            elif tmux has-session -t "$session" 2>/dev/null; then
              response="{\"schemaVersion\":1,\"runId\":\"$run_id\",\"state\":\"{{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Running)}}\",\"session\":\"$session\",\"sourceCommit\":null}"
            else
              response="{\"schemaVersion\":1,\"runId\":\"$run_id\",\"state\":\"{{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Unknown)}}\",\"session\":\"$session\",\"sourceCommit\":null}"
            fi
            printf 'LC_REMOTE_STATE:%s\n' "$response"
            """;
    }

    private static string CreateCancelScript(RemoteBenchmarkRunRecord record)
    {
        var remoteStatePath = $"{record.RemotePath.TrimEnd('/')}/artifacts/benchmark/runs/{record.RunId:D}/remote-run.json";
        return $$"""
            set -euo pipefail
            state_file={{QuotePosix(remoteStatePath)}}
            session={{QuotePosix(record.SessionName)}}
            run_id={{QuotePosix(record.RunId.ToString("D"))}}
            if [[ -r "$state_file" ]] && grep -Eq '"state":"({{RemoteBenchmarkStateCatalog.TerminalPattern}})"' "$state_file"; then
              response="$(cat "$state_file")"
            elif tmux has-session -t "$session" 2>/dev/null; then
              tmux send-keys -t "$session" C-c
              response="{\"schemaVersion\":1,\"runId\":\"$run_id\",\"state\":\"{{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.CancelRequested)}}\",\"session\":\"$session\",\"sourceCommit\":null}"
            else
              response="{\"schemaVersion\":1,\"runId\":\"$run_id\",\"state\":\"{{RemoteBenchmarkStateCatalog.GetName(RemoteBenchmarkState.Unknown)}}\",\"session\":\"$session\",\"sourceCommit\":null}"
            fi
            printf 'LC_REMOTE_STATE:%s\n' "$response"
            """;
    }

    private static string QuotePosix(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static RemoteStateResponse? ReadRemoteResponse(string path)
    {
        if (!File.Exists(path))
            return null;
        foreach (var line in File.ReadLines(path).Reverse())
        {
            const string prefix = "LC_REMOTE_STATE:";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            try
            {
                var response = JsonSerializer.Deserialize<RemoteStateResponse>(line[prefix.Length..], JsonOptions);
                if (response is null || response.SchemaVersion != SupportedSchemaVersion || response.RunId == Guid.Empty ||
                    !RemoteBenchmarkStateCatalog.IsKnown(response.State))
                    return null;
                return response;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return null;
    }

    private static RunState ToRunState(OutcomeKind outcome) => outcome switch
    {
        OutcomeKind.Success => RunState.Succeeded,
        OutcomeKind.Cancelled => RunState.Cancelled,
        OutcomeKind.Timeout => RunState.TimedOut,
        _ => RunState.Failed
    };

    private static async Task WriteRecordAsync(string path, RemoteBenchmarkRunRecord record, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, JsonOptions), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<RemoteBenchmarkRunRecord?> ReadRecordAsync(string path, Guid expectedRunId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        var record = await JsonSerializer.DeserializeAsync<RemoteBenchmarkRunRecord>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        if (record is null || record.SchemaVersion != SupportedSchemaVersion || record.RunId != expectedRunId ||
            string.IsNullOrWhiteSpace(record.RemoteHost) || string.IsNullOrWhiteSpace(record.RemotePath) ||
            string.IsNullOrWhiteSpace(record.SessionName))
            throw new InvalidDataException($"Remote benchmark run record '{path}' is invalid.");
        return record;
    }

    private sealed record RemoteStateResponse
    {
        public int SchemaVersion { get; init; }
        public Guid RunId { get; init; }
        public string State { get; init; } = string.Empty;
        public string? Session { get; init; }
        public string? SourceCommit { get; init; }
    }
}
