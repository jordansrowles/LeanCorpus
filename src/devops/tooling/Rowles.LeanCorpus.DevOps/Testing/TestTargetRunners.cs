using Rowles.LeanCorpus.DevOps.Infrastructure;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestRunnerContext(
    Guid RunId,
    PreparedTestTarget PreparedTarget,
    TestRunnerOptions Options,
    string WorkingDirectory,
    string AttemptDirectory,
    int Iteration,
    TimeSpan? Timeout,
    Action<ProcessProgressEvent>? Progress = null);

internal sealed record TestRunnerResult(
    ProcessResult Process,
    TestResultData? Results,
    string? TrxPath,
    DateTimeOffset StartedUtc,
    DateTimeOffset EndedUtc);

internal interface ITestTargetRunner
{
    Task<TestRunnerResult> RunAsync(TestRunnerContext context, CancellationToken cancellationToken = default);
}

internal sealed class MtpExecutableTestRunner(IProcessRunner processes) : ITestTargetRunner
{
    public async Task<TestRunnerResult> RunAsync(TestRunnerContext context, CancellationToken cancellationToken = default)
    {
        TestRunnerProcess.Validate(context, "Mtp");
        var arguments = MtpTestArgumentBuilder.Build(context.PreparedTarget.Target, context.Options, context.AttemptDirectory);
        var started = DateTimeOffset.UtcNow;
        var process = await processes.RunAsync(TestRunnerProcess.CreateRequest(context, arguments), cancellationToken).ConfigureAwait(false);
        var ended = DateTimeOffset.UtcNow;
        var trxPath = Path.Combine(context.AttemptDirectory, "results.trx");
        var results = context.Options.ArtifactsEnabled
            ? await TestResultReader.ReadAsync(trxPath, process.StandardOutputLogPath, CancellationToken.None).ConfigureAwait(false)
            : null;
        return new TestRunnerResult(process, results, trxPath, started, ended);
    }
}

internal sealed class NativeAotTestRunner(IProcessRunner processes) : ITestTargetRunner
{
    public async Task<TestRunnerResult> RunAsync(TestRunnerContext context, CancellationToken cancellationToken = default)
    {
        TestRunnerProcess.Validate(context, "AotNative");
        var started = DateTimeOffset.UtcNow;
        var process = await processes.RunAsync(TestRunnerProcess.CreateRequest(context,
            context.PreparedTarget.Target.AdditionalArguments), cancellationToken).ConfigureAwait(false);
        return new TestRunnerResult(process, null, null, started, DateTimeOffset.UtcNow);
    }
}

internal static class TestRunnerProcess
{
    public static void Validate(TestRunnerContext context, string runnerKind)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.PreparedTarget.Target.RunnerKind.Equals(runnerKind, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {runnerKind} adapter cannot execute target '{context.PreparedTarget.Target.Key}' with runner '{context.PreparedTarget.Target.RunnerKind}'.");
        }
    }

    public static ProcessRequest CreateRequest(TestRunnerContext context, IReadOnlyList<string> arguments)
        => new()
        {
            ExecutablePath = context.PreparedTarget.ExecutablePath,
            Arguments = arguments,
            WorkingDirectory = context.WorkingDirectory,
            StandardOutputLogPath = Path.Combine(context.AttemptDirectory, "stdout.log"),
            StandardErrorLogPath = Path.Combine(context.AttemptDirectory, "stderr.log"),
            EnvironmentVariables = TestProcessEnvironmentBuilder.Build(context.Options, context.RunId,
                context.AttemptDirectory, context.PreparedTarget.Target, context.Iteration),
            Timeout = context.Timeout,
            ProgressInterval = TimeSpan.FromSeconds(5),
            Progress = context.Progress
        };
}
