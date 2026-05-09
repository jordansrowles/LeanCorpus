using System.Diagnostics;
using BenchmarkDotNet.Analysers;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Microsoft.Diagnostics.NETCore.Client;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Collects a GC heap dump after each benchmark run completes.
/// Tries <c>dotnet-gcdump</c> first (lightweight .gcdump files openable in VS / PerfView),
/// then falls back to <see cref="DiagnosticsClient.WriteDump"/> (full process dump with heap).
/// </summary>
internal sealed class GcDumpDiagnoser : IDiagnoser
{
    private int _currentDocCount;

    public IEnumerable<string> Ids => ["GcDump"];
    public IEnumerable<IExporter> Exporters => [];
    public IEnumerable<IAnalyser> Analysers => [];

    public RunMode GetRunMode(BenchmarkCase benchmarkCase)
    {
        var param = benchmarkCase.Parameters["DocumentCount"];
        _currentDocCount = param is int i ? i : 0;
        return RunMode.NoOverhead;
    }

    public void Handle(HostSignal signal, DiagnoserActionParameters parameters)
    {
        if (signal != HostSignal.AfterAll) return;

        var pid = parameters.Process.Id;
        var artifactsPath = parameters.Config.ArtifactsPath;
        Directory.CreateDirectory(artifactsPath);

        var stem = $"gcdump-{_currentDocCount}docs-{DateTime.UtcNow:yyyyMMdd-HHmmss}";

        if (TryCollectGcDump(pid, Path.Combine(artifactsPath, stem + ".gcdump")))
            return;

        TryWriteProcessDump(pid, Path.Combine(artifactsPath, stem + ".dmp"));
    }

    private static bool TryCollectGcDump(int pid, string outputPath)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet-gcdump",
                Arguments = $"collect -p {pid} -o \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            proc.WaitForExit(120_000);

            if (proc.ExitCode == 0 && File.Exists(outputPath))
            {
                Console.WriteLine($"[GcDump] Collected: {outputPath}");
                return true;
            }

            var err = proc.StandardError.ReadToEnd().Trim();
            if (!string.IsNullOrEmpty(err))
                Console.Error.WriteLine($"[GcDump] dotnet-gcdump failed: {err}");
            return false;
        }
        catch
        {
            // Tool not installed — fall through to process dump
            return false;
        }
    }

    private static void TryWriteProcessDump(int pid, string outputPath)
    {
        try
        {
            var client = new DiagnosticsClient(pid);
            client.WriteDump(DumpType.WithHeap, outputPath);
            Console.WriteLine($"[GcDump] Fallback heap dump written: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[GcDump] Failed to collect any dump: {ex.Message}");
        }
    }

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters) => [];

    public async IAsyncEnumerable<ValidationError> ValidateAsync(ValidationParameters validationParameters)
    {
        await ValueTask.CompletedTask;
        yield break;
    }

    public ValueTask HandleAsync(HostSignal signal, DiagnoserActionParameters parameters, CancellationToken cancellationToken)
    {
        Handle(signal, parameters);
        return ValueTask.CompletedTask;
    }

    public IEnumerable<Metric> ProcessResults(DiagnoserResults results) => [];
    public void DisplayResults(ILogger logger) { }
}
