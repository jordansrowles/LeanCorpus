using BenchmarkDotNet.Running;

namespace Rowles.Text.Benchmarks;

internal static class Program
{
    public static int Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
