using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace Rowles.Text.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        string artifactsPath = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "benchmark", "runs", "direct", "text");
        Directory.CreateDirectory(artifactsPath);
        var config = DefaultConfig.Instance.WithArtifactsPath(Path.Combine(artifactsPath, "_runner"));
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }
}
