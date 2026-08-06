using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Index.Backup;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures full and incremental backup creation, validation and chain restoration.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class IncrementalBackupBenchmarks
{
    private string _sourcePath = string.Empty;
    private string _baseBackupPath = string.Empty;
    private string _deltaBackupPath = string.Empty;
    private string _fullBackupPath = string.Empty;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(false, true)]
    public bool UseCompoundFile { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _sourcePath = Path.Combine(BenchmarkHelpers.TempRoot, $"backup-source-{Guid.NewGuid():N}");
        _baseBackupPath = Path.Combine(BenchmarkHelpers.TempRoot, $"backup-base-{Guid.NewGuid():N}");
        _deltaBackupPath = Path.Combine(BenchmarkHelpers.TempRoot, $"backup-delta-{Guid.NewGuid():N}");
        _fullBackupPath = Path.Combine(BenchmarkHelpers.TempRoot, $"backup-full-{Guid.NewGuid():N}");

        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        RecentFeatureBenchmarkIndex.Build(_sourcePath, documents, UseCompoundFile);
        IndexBackup.Backup(_sourcePath, _baseBackupPath);
        var delta = BenchmarkData.BuildDocuments(Math.Max(1, DocumentCount / 100));
        RecentFeatureBenchmarkIndex.Append(_sourcePath, delta, DocumentCount, UseCompoundFile);
        IndexBackup.Backup(_sourcePath, _deltaBackupPath,
            new IndexBackupOptions { PreviousBackupDirectoryPath = _baseBackupPath });
        IndexBackup.Backup(_sourcePath, _fullBackupPath);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        RecentFeatureBenchmarkIndex.Delete(_sourcePath);
        RecentFeatureBenchmarkIndex.Delete(_baseBackupPath);
        RecentFeatureBenchmarkIndex.Delete(_deltaBackupPath);
        RecentFeatureBenchmarkIndex.Delete(_fullBackupPath);
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

    private IndexBackupResult CreateBackup(string? previousPath)
    {
        string target = Path.Combine(BenchmarkHelpers.TempRoot, $"backup-run-{Guid.NewGuid():N}");
        try
        {
            return IndexBackup.Backup(_sourcePath, target,
                new IndexBackupOptions { PreviousBackupDirectoryPath = previousPath });
        }
        finally
        {
            RecentFeatureBenchmarkIndex.Delete(target);
        }
    }

    private static int Restore(IReadOnlyList<string> backups)
    {
        string target = Path.Combine(BenchmarkHelpers.TempRoot, $"restore-run-{Guid.NewGuid():N}");
        try
        {
            return IndexBackup.Restore(backups, target).RestoredFiles.Count;
        }
        finally
        {
            RecentFeatureBenchmarkIndex.Delete(target);
        }
    }
}
