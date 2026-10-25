using System.Diagnostics;
using System.Reflection;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class PhysicalIndexRuntimeProcessTests
{
    private const string ProbePath = "LEANCORPUS_LIFECYCLE_PROBE_PATH";
    private const string ProbeBlocked = "LEANCORPUS_LIFECYCLE_PROBE_BLOCKED";

    [Fact]
    public async Task SeparateProcessCannotOpenTheWriterUntilTerminalClose()
    {
        if (Environment.GetEnvironmentVariable(ProbePath) is { } childPath)
        {
            if (Environment.GetEnvironmentVariable(ProbeBlocked) == "true")
                Assert.Throws<WriteLockException>(() => Open(childPath));
            else
                await using (var child = Open(childPath)) { }
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), "leancorpus-process-ownership", Guid.NewGuid().ToString("N"));
        var runtime = Open(path);
        try
        {
            await RunProbeAsync(path, blocked: true);
            await runtime.CloseAsync(TestContext.Current.CancellationToken);
            await RunProbeAsync(path, blocked: false);
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            Directory.Move(path, path + "-closed");
            Directory.Delete(path + "-closed", recursive: true);
        }
        finally
        {
            await runtime.DisposeAsync();
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    private static async Task RunProbeAsync(string path, bool blocked)
    {
        string results = Path.Combine(Path.GetTempPath(), "leancorpus-process-probe-results", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(results);
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = results,
        };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add($"FullyQualifiedName={typeof(PhysicalIndexRuntimeProcessTests).FullName}.{nameof(SeparateProcessCannotOpenTheWriterUntilTerminalClose)}");
        start.Environment[ProbePath] = path;
        start.Environment[ProbeBlocked] = blocked ? "true" : "false";
        using Process child = Process.Start(start)!;
        Task<string> output = child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.True(child.ExitCode == 0, $"Writer probe failed ({child.ExitCode}): {await output}\n{await error}");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); }
            Directory.Delete(results, recursive: true);
        }
    }

    private static PhysicalIndexRuntime Open(string path) => new(path, new IndexWriterConfig { DurableCommits = false },
        new SearcherManagerConfig { RefreshInterval = TimeSpan.FromHours(1) });
}
