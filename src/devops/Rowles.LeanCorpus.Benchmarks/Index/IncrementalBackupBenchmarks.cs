using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Backup;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures full and incremental backup creation, validation and chain restoration.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[InvocationCount(1)]
public class IncrementalBackupBenchmarks
{
    private const string FixtureRootEnvironmentVariable = "LEAN_CORPUS_BACKUP_BENCHMARK_FIXTURE_ROOT";

    private string _sourcePath = string.Empty;
    private string _baseBackupPath = string.Empty;
    private string _deltaBackupPath = string.Empty;
    private string _fullBackupPath = string.Empty;
    private readonly List<string> _iterationPaths = [];

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(false, true)]
    public bool UseCompoundFile { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var fixtureRoot = Environment.GetEnvironmentVariable(FixtureRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(fixtureRoot))
        {
            throw new InvalidOperationException(
                "The incremental-backup suite fixture was not prepared by the benchmark runner.");
        }

        var fixturePath = GetFixturePath(fixtureRoot, DocumentCount, UseCompoundFile);
        _sourcePath = Path.Combine(fixturePath, "source");
        _baseBackupPath = Path.Combine(fixturePath, "base");
        _deltaBackupPath = Path.Combine(fixturePath, "delta");

        // The base backup is already a complete full backup. Reusing it avoids
        // creating an identical third durable fixture solely for restore cases.
        _fullBackupPath = _baseBackupPath;

        if (!Directory.Exists(_sourcePath)
            || !Directory.Exists(_baseBackupPath)
            || !Directory.Exists(_deltaBackupPath))
        {
            throw new InvalidOperationException(
                $"The incremental-backup fixture '{fixturePath}' is incomplete.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupIterationPaths();
    }

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        foreach (var path in _iterationPaths)
            RecentFeatureBenchmarkIndex.Delete(path);
        _iterationPaths.Clear();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FullBackup() => CreateBackup(previousPath: null).CopiedFiles.Count;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int IncrementalBackup_SmallDelta() => CreateBackup(_baseBackupPath).CopiedFiles.Count;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int IncrementalBackup_Unchanged() => CreateBackup(_deltaBackupPath).CopiedFiles.Count;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ValidateFullBackup() => IndexBackup.ValidateBackup(_fullBackupPath).Files.Count;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ValidateBackupChain() => IndexBackup.ValidateBackup([_baseBackupPath, _deltaBackupPath]).Files.Count;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int RestoreFullBackup() => Restore([_fullBackupPath]);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int RestoreBackupChain() => Restore([_baseBackupPath, _deltaBackupPath]);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int RestoreFullBackup_WithValidation() => Restore([_fullBackupPath], validateAfterRestore: true);

    private IndexBackupResult CreateBackup(string? previousPath)
    {
        string target = Path.Combine(BenchmarkHelpers.TempRoot, $"backup-run-{Guid.NewGuid():N}");
        _iterationPaths.Add(target);
        return IndexBackup.Backup(_sourcePath, target,
            new IndexBackupOptions { PreviousBackupDirectoryPath = previousPath });
    }

    private int Restore(IReadOnlyList<string> backups, bool validateAfterRestore = false)
    {
        string target = Path.Combine(BenchmarkHelpers.TempRoot, $"restore-run-{Guid.NewGuid():N}");
        _iterationPaths.Add(target);
        return IndexBackup.Restore(
            backups,
            target,
            new IndexRestoreOptions { ValidateAfterRestore = validateAfterRestore })
            .RestoredFiles.Count;
    }

    internal static IDisposable PrepareSharedFixtures()
    {
        var fixtureRoot = Path.Combine(
            BenchmarkHelpers.TempRoot,
            $"incremental-backup-fixtures-{Guid.NewGuid():N}");

        try
        {
            foreach (int documentCount in DocCounts)
            {
                foreach (bool useCompoundFile in new[] { false, true })
                    PrepareFixture(fixtureRoot, documentCount, useCompoundFile);
            }

            Environment.SetEnvironmentVariable(
                FixtureRootEnvironmentVariable,
                fixtureRoot,
                EnvironmentVariableTarget.Process);
            return new SharedFixtureLease(fixtureRoot);
        }
        catch
        {
            RecentFeatureBenchmarkIndex.Delete(fixtureRoot);
            throw;
        }
    }

    private static void PrepareFixture(
        string fixtureRoot,
        int documentCount,
        bool useCompoundFile)
    {
        var fixturePath = GetFixturePath(fixtureRoot, documentCount, useCompoundFile);
        var sourcePath = Path.Combine(fixturePath, "source");
        var baseBackupPath = Path.Combine(fixturePath, "base");
        var deltaBackupPath = Path.Combine(fixturePath, "delta");

        var documents = BenchmarkData.BuildDocuments(documentCount);
        RecentFeatureBenchmarkIndex.Build(sourcePath, documents, useCompoundFile);
        IndexBackup.Backup(sourcePath, baseBackupPath);

        var delta = BenchmarkData.BuildDocuments(Math.Max(1, documentCount / 100));
        RecentFeatureBenchmarkIndex.Append(
            sourcePath,
            delta,
            documentCount,
            useCompoundFile);
        IndexBackup.Backup(
            sourcePath,
            deltaBackupPath,
            new IndexBackupOptions { PreviousBackupDirectoryPath = baseBackupPath });
    }

    private static string GetFixturePath(
        string fixtureRoot,
        int documentCount,
        bool useCompoundFile)
        => Path.Combine(
            fixtureRoot,
            documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            useCompoundFile ? "compound" : "loose");

    private sealed class SharedFixtureLease(string fixtureRoot) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (string.Equals(
                    Environment.GetEnvironmentVariable(FixtureRootEnvironmentVariable),
                    fixtureRoot,
                    StringComparison.Ordinal))
            {
                Environment.SetEnvironmentVariable(
                    FixtureRootEnvironmentVariable,
                    null,
                    EnvironmentVariableTarget.Process);
            }

            RecentFeatureBenchmarkIndex.Delete(fixtureRoot);
        }
    }
}
