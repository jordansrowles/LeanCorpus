using System.Diagnostics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Profiling;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
[InvocationCount(1)]
public class DeletionPhasedBenchmarks
{
    private static readonly string[] PhaseNames =
    [
        Diagnostics.LeanCorpusActivitySource.DeleteQueue,
        Diagnostics.LeanCorpusActivitySource.DeleteApply,
        Diagnostics.LeanCorpusActivitySource.Flush,
        Diagnostics.LeanCorpusActivitySource.Commit,
        Diagnostics.LeanCorpusActivitySource.Merge,
    ];

    [Params(20_000)]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private int _deleteCount;
    private ActivityListener? _listener;
    private readonly Lock _phaseLock = new();
    private Dictionary<string, TimeSpan> _phaseTimings = new(StringComparer.Ordinal);
    private Dictionary<string, int> _phaseCounts = new(StringComparer.Ordinal);

    // These are set in IterationSetup and cleaned up in IterationCleanup.
    private string _indexPath = string.Empty;
    private MMapDirectory? _directory;
    private IndexWriter? _writer;

    [GlobalSetup]
    public void Setup()
    {
        _documents = Benchmarks.BenchmarkData.BuildDocuments(DocumentCount);
        _deleteCount = Math.Max(1, DocumentCount / 10);

        _listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Rowles.LeanCorpus",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (_phaseLock)
                {
                    var name = activity.OperationName;
                    _phaseTimings.TryGetValue(name, out var existing);
                    _phaseTimings[name] = existing + activity.Duration;
                    _phaseCounts[name] = _phaseCounts.GetValueOrDefault(name) + 1;
                }
            }
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _phaseTimings.Clear();
        _phaseCounts.Clear();

        _indexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-prof-del-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);
        _directory = new MMapDirectory(_indexPath);
        _writer = new IndexWriter(_directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
        });

        // Pre-populate the index
        for (int i = 0; i < _documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new TextField("body", _documents[i]));
            _writer.AddDocument(doc);
        }
        _writer.Commit();

        // Reset timings to exclude pre-population from the benchmark phase
        _phaseTimings.Clear();
        _phaseCounts.Clear();
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int DeleteAndCommit()
    {
        // Queue deletes for 10% of documents
        for (int i = 0; i < _deleteCount; i++)
        {
            _writer!.DeleteDocuments(new TermQuery(
                "id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        _writer!.Commit();
        return _deleteCount;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        DumpPhaseBreakdown();

        _writer?.Dispose();
        _directory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_indexPath) && Directory.Exists(_indexPath))
            Directory.Delete(_indexPath, recursive: true);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _listener?.Dispose();
    }

    private void DumpPhaseBreakdown()
    {
        if (_phaseTimings.Count == 0)
        {
            Console.Error.WriteLine("  [profiling] No activity spans captured.");
            return;
        }

        long totalTicks = _phaseTimings.Values.Sum(t => t.Ticks);
        if (totalTicks == 0) return;

        Console.Error.WriteLine();
        Console.Error.WriteLine($"  == Deletion Phase Breakdown ({_deleteCount:N0} deletes, {DocumentCount:N0} docs) ==");
        Console.Error.WriteLine($"  {"Phase",-38} {"Time",10} {"%",7}  {"Calls",8}");

        foreach (var phaseName in PhaseNames)
        {
            if (!_phaseTimings.TryGetValue(phaseName, out var duration))
                continue;

            double pct = (double)duration.Ticks / totalTicks * 100;
            int calls = _phaseCounts.GetValueOrDefault(phaseName);
            Console.Error.WriteLine(
                $"  {phaseName,-38} {duration.TotalMilliseconds,9:F2} ms {pct,6:F1}%  {calls,8:N0}");
        }

        foreach (var (name, duration) in _phaseTimings.OrderByDescending(kv => kv.Value))
        {
            if (PhaseNames.Contains(name)) continue;
            double pct = (double)duration.Ticks / totalTicks * 100;
            int calls = _phaseCounts.GetValueOrDefault(name);
            Console.Error.WriteLine(
                $"  {name,-38} {duration.TotalMilliseconds,9:F2} ms {pct,6:F1}%  {calls,8:N0}");
        }

        Console.Error.WriteLine($"  {"Total",-38} {new TimeSpan(totalTicks).TotalMilliseconds,9:F2} ms {100,6:F1}%");
        Console.Error.WriteLine();
        Console.Error.Flush();
    }
}
