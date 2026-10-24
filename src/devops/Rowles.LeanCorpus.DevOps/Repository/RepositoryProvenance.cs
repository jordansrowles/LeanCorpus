using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed record GitProvenance(OutcomeKind Outcome, string? Commit, string? Branch, bool Dirty, string? Failure);

internal interface IRepositoryProvenanceReader
{
    Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken);
}

internal sealed class GitRepositoryProvenanceReader(IProcessRunner processRunner, string gitExecutable = "git") : IRepositoryProvenanceReader
{
    public async Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
    {
        var commit = await RunGitAsync(checkout, runDirectory, ["rev-parse", "HEAD"], "git-commit", cancellationToken).ConfigureAwait(false);
        if (commit.Outcome != OutcomeKind.Success)
        {
            return new GitProvenance(commit.Outcome, null, null, false, commit.Failure);
        }

        var branch = await RunGitAsync(checkout, runDirectory, ["rev-parse", "--abbrev-ref", "HEAD"], "git-branch", cancellationToken).ConfigureAwait(false);
        if (branch.Outcome != OutcomeKind.Success)
        {
            return new GitProvenance(branch.Outcome, commit.StandardOutputTail.LastOrDefault()?.Trim(), null, false, branch.Failure);
        }

        var status = await RunGitAsync(checkout, runDirectory, ["status", "--porcelain", "--untracked-files=all"], "git-status", cancellationToken).ConfigureAwait(false);
        if (status.Outcome != OutcomeKind.Success)
        {
            return new GitProvenance(status.Outcome, commit.StandardOutputTail.LastOrDefault()?.Trim(), branch.StandardOutputTail.LastOrDefault()?.Trim(), false, status.Failure);
        }

        return new GitProvenance(
            OutcomeKind.Success,
            commit.StandardOutputTail.LastOrDefault()?.Trim(),
            branch.StandardOutputTail.LastOrDefault()?.Trim(),
            status.StandardOutputLineCount > 0,
            null);
    }

    private Task<ProcessResult> RunGitAsync(
        CheckoutIdentity checkout,
        string runDirectory,
        IReadOnlyList<string> gitArguments,
        string logPrefix,
        CancellationToken cancellationToken)
        => processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = gitExecutable,
            Arguments = ["-C", checkout.RepositoryRoot, .. gitArguments],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, logPrefix + ".stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, logPrefix + ".stderr.log"),
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken);
}
