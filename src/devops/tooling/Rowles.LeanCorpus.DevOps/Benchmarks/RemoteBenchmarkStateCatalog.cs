namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal enum RemoteBenchmarkState
{
    Starting,
    Running,
    CancelRequested,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Unknown
}

internal sealed record RemoteBenchmarkStateDescriptor(RemoteBenchmarkState State, string Name);

internal static class RemoteBenchmarkStateCatalog
{
    private static readonly RemoteBenchmarkStateDescriptor[] Descriptors =
    [
        new(RemoteBenchmarkState.Starting, "starting"),
        new(RemoteBenchmarkState.Running, "running"),
        new(RemoteBenchmarkState.CancelRequested, "cancel-requested"),
        new(RemoteBenchmarkState.Succeeded, "succeeded"),
        new(RemoteBenchmarkState.Failed, "failed"),
        new(RemoteBenchmarkState.Cancelled, "cancelled"),
        new(RemoteBenchmarkState.TimedOut, "timed-out"),
        new(RemoteBenchmarkState.Unknown, "unknown")
    ];

    public static IReadOnlyList<RemoteBenchmarkStateDescriptor> All => Descriptors;

    public static string TerminalPattern => string.Join('|', Descriptors
        .Where(static descriptor => descriptor.State is RemoteBenchmarkState.Succeeded or RemoteBenchmarkState.Failed or
            RemoteBenchmarkState.Cancelled or RemoteBenchmarkState.TimedOut)
        .Select(static descriptor => descriptor.Name));

    public static string GetName(RemoteBenchmarkState state)
    {
        foreach (var descriptor in Descriptors)
        {
            if (descriptor.State == state)
            {
                return descriptor.Name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown remote benchmark state.");
    }

    public static bool TryParse(string name, out RemoteBenchmarkState state)
    {
        foreach (var descriptor in Descriptors)
        {
            if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
            {
                state = descriptor.State;
                return true;
            }
        }

        state = default;
        return false;
    }

    public static bool IsKnown(string name) => TryParse(name, out _);

    public static bool IsStartResponse(string name)
        => TryParse(name, out var state) && state is RemoteBenchmarkState.Starting or RemoteBenchmarkState.Running or
            RemoteBenchmarkState.Succeeded or RemoteBenchmarkState.Failed or RemoteBenchmarkState.Cancelled;

    public static bool IsTerminal(string name)
        => TryParse(name, out var state) && state is RemoteBenchmarkState.Succeeded or RemoteBenchmarkState.Failed or
            RemoteBenchmarkState.Cancelled or RemoteBenchmarkState.TimedOut;
}
