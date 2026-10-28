using System.Diagnostics;
using CliWrap;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Infrastructure;

internal sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan MinimumProgressInterval = TimeSpan.FromMilliseconds(50);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string workingDirectory;
        string stdoutPath;
        string stderrPath;
        string? validationError;
        try
        {
            validationError = Validate(request);
            workingDirectory = Path.GetFullPath(request.WorkingDirectory);
            stdoutPath = Path.GetFullPath(request.StandardOutputLogPath);
            stderrPath = Path.GetFullPath(request.StandardErrorLogPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(OutcomeKind.ValidationFailure, exception.Message, request.StandardOutputLogPath, request.StandardErrorLogPath, TimeSpan.Zero);
        }

        if (validationError is not null)
        {
            return Failure(OutcomeKind.ValidationFailure, validationError, request.StandardOutputLogPath, request.StandardErrorLogPath, TimeSpan.Zero);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(stderrPath)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, exception.Message, stdoutPath, stderrPath, TimeSpan.Zero);
        }

        var stopwatch = Stopwatch.StartNew();
        var stdout = new OutputTail(request.MaxTailLines, request.MaxTailCharacters);
        var stderr = new OutputTail(request.MaxTailLines, request.MaxTailCharacters);
        using var timeoutSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var stoppedByCondition = 0;
        if (request.Timeout is { } timeout)
        {
            timeoutSource.CancelAfter(timeout);
        }

        using var progressSource = new CancellationTokenSource();
        Exception? progressFailure = null;
        var progressTask = PublishProgressAsync(request, stdout, stderr, stopwatch, progressSource.Token, exception =>
            Interlocked.CompareExchange(ref progressFailure, exception, null));
        try
        {
            var command = Cli.Wrap(request.ExecutablePath)
                .WithArguments(request.Arguments)
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.Merge(
                    PipeTarget.ToDelegate(line =>
                    {
                        stdout.Add(line);
                        if (request.StopOnStandardOutputPrefix is { Length: > 0 } prefix &&
                            line.AsSpan().TrimStart().StartsWith(prefix, StringComparison.Ordinal))
                        {
                            Interlocked.Exchange(ref stoppedByCondition, 1);
                            linkedSource.Cancel();
                        }
                    }),
                    PipeTarget.ToFile(stdoutPath)))
                .WithStandardErrorPipe(PipeTarget.Merge(
                    PipeTarget.ToDelegate(stderr.Add),
                    PipeTarget.ToFile(stderrPath)));
            if (request.StandardInputText is not null)
            {
                command = command.WithStandardInputPipe(PipeSource.FromString(request.StandardInputText));
            }
            if (request.EnvironmentVariables is { Count: > 0 } environmentVariables)
            {
                command = command.WithEnvironmentVariables(environmentVariables);
            }

            var child = await command.ExecuteAsync(linkedSource.Token).ConfigureAwait(false);
            stopwatch.Stop();
            var outcome = child.ExitCode == 0
                ? progressFailure is null ? OutcomeKind.Success : OutcomeKind.InfrastructureFailure
                : OutcomeKind.CommandFailure;
            return new ProcessResult(
                outcome,
                child.ExitCode,
                stdout.Snapshot(),
                stderr.Snapshot(),
                stdout.LineCount,
                stderr.LineCount,
                stopwatch.Elapsed,
                stdoutPath,
                stderrPath,
                progressFailure?.Message,
                false);
        }
        catch (OperationCanceledException exception) when (linkedSource.IsCancellationRequested)
        {
            stopwatch.Stop();
            var stoppedByOutput = Volatile.Read(ref stoppedByCondition) != 0 && !cancellationToken.IsCancellationRequested && !timeoutSource.IsCancellationRequested;
            var outcome = stoppedByOutput ? OutcomeKind.Success : cancellationToken.IsCancellationRequested ? OutcomeKind.Cancelled : OutcomeKind.Timeout;
            return new ProcessResult(
                outcome,
                null,
                stdout.Snapshot(),
                stderr.Snapshot(),
                stdout.LineCount,
                stderr.LineCount,
                stopwatch.Elapsed,
                stdoutPath,
                stderrPath,
                stoppedByOutput ? null : exception.Message,
                stoppedByOutput);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new ProcessResult(
                OutcomeKind.InfrastructureFailure,
                null,
                stdout.Snapshot(),
                stderr.Snapshot(),
                stdout.LineCount,
                stderr.LineCount,
                stopwatch.Elapsed,
                stdoutPath,
                stderrPath,
                exception.Message);
        }
        finally
        {
            await progressSource.CancelAsync().ConfigureAwait(false);
            try
            {
                await progressTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (progressSource.IsCancellationRequested)
            {
            }
        }
    }

    private static string? Validate(ProcessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            return "An executable path is required.";
        }

        if (request.Arguments is null || request.Arguments.Any(argument => argument is null))
        {
            return "Process arguments cannot contain null values.";
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory) || !Directory.Exists(request.WorkingDirectory))
        {
            return "The process working directory must exist.";
        }

        if (string.IsNullOrWhiteSpace(request.StandardOutputLogPath) || string.IsNullOrWhiteSpace(request.StandardErrorLogPath))
        {
            return "Standard output and error log paths are required.";
        }

        if (string.Equals(Path.GetFullPath(request.StandardOutputLogPath), Path.GetFullPath(request.StandardErrorLogPath), PathComparison))
        {
            return "Standard output and error logs must use separate files.";
        }

        if (request.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            return "Process timeout must be greater than zero.";
        }

        if (request.ProgressInterval < MinimumProgressInterval)
        {
            return $"Progress interval must be at least {MinimumProgressInterval.TotalMilliseconds:0} milliseconds.";
        }

        if (request.MaxTailLines < 1 || request.MaxTailCharacters < 1)
        {
            return "Output tail limits must be positive.";
        }

        return null;
    }

    private static async Task PublishProgressAsync(
        ProcessRequest request,
        OutputTail stdout,
        OutputTail stderr,
        Stopwatch stopwatch,
        CancellationToken cancellationToken,
        Action<Exception> reportFailure)
    {
        if (request.Progress is null)
        {
            return;
        }

        using var timer = new PeriodicTimer(request.ProgressInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                request.Progress(new ProcessProgressEvent(
                    stopwatch.Elapsed,
                    stdout.LineCount,
                    stderr.LineCount,
                    stdout.LastLine,
                    stderr.LastLine));
            }
            catch (Exception exception)
            {
                reportFailure(exception);
                return;
            }
        }
    }

    private static ProcessResult Failure(OutcomeKind outcome, string failure, string? stdoutPath, string? stderrPath, TimeSpan duration)
        => new(outcome, null, [], [], 0, 0, duration,
            string.IsNullOrWhiteSpace(stdoutPath) ? string.Empty : stdoutPath,
            string.IsNullOrWhiteSpace(stderrPath) ? string.Empty : stderrPath,
            failure);

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class OutputTail(int maxLines, int maxCharacters)
    {
        private readonly object _gate = new();
        private readonly Queue<string> _lines = new();
        private int _characters;
        private int _lineCount;

        public int LineCount => Volatile.Read(ref _lineCount);

        public string? LastLine
        {
            get
            {
                lock (_gate)
                    return _lines.Count == 0 ? null : _lines.Last();
            }
        }

        public void Add(string value)
        {
            Interlocked.Increment(ref _lineCount);
            var line = value.Length > maxCharacters ? value[^maxCharacters..] : value;
            lock (_gate)
            {
                _lines.Enqueue(line);
                _characters += line.Length;
                while (_lines.Count > maxLines || _characters > maxCharacters)
                {
                    _characters -= _lines.Dequeue().Length;
                }
            }
        }

        public IReadOnlyList<string> Snapshot()
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }
}
