namespace Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

/// <summary>
/// Chaos test configuration, driven by environment variables.
/// </summary>
public static class ChaosConfig
{
    /// <summary>
    /// Number of FsCheck property-test iterations.
    /// Read from <c>CHAOS_ITERATIONS</c>; defaults to 100.
    /// </summary>
    public static int FsCheckIterations { get; } = ReadEnvInt("CHAOS_ITERATIONS", 100);

    private static int ReadEnvInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (raw is not null && int.TryParse(raw, out int value) && value > 0)
            return value;
        return fallback;
    }
}
