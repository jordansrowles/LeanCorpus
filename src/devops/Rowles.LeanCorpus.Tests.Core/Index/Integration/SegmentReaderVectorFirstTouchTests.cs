using System.Reflection;
using System.Threading.Tasks;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentReaderVectorFirstTouchTests : IDisposable
{
    private const int VectorFieldCount = 4;
    private const int DocumentCount = 64;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ll-vector-first-touch-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstTouchForOneField_DoesNotWaitForAnotherFieldInitialisation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SegmentInfo info = BuildIndex();
        using var directory = new MMapDirectory(_path);
        using var reader = new SegmentReader(directory, info);
        using var lease = reader.AcquireReadLease();
        SegmentReaderState state = lease.State;

        FieldInfo? coarseLockField = typeof(SegmentReaderState).GetField(
            "_hnswLoadLock", BindingFlags.Instance | BindingFlags.NonPublic);
        object? coarseLoadLock = coarseLockField?.GetValue(state);
        if (coarseLoadLock is not null)
            Monitor.Enter(coarseLoadLock);

        using var started = new ManualResetEventSlim();
        Task<HnswGraph?>? loadTask = null;
        bool startedInTime = false;
        bool completedWhileOtherFieldWasInitialising = false;
        try
        {
            loadTask = Task.Run(() =>
            {
                started.Set();
                return state.GetHnswGraph(FieldName(1));
            }, cancellationToken);
            startedInTime = started.Wait(TimeSpan.FromSeconds(5), cancellationToken);
            if (startedInTime)
            {
                completedWhileOtherFieldWasInitialising = WaitForCompletion(loadTask, TimeSpan.FromSeconds(1));
            }
        }
        finally
        {
            if (coarseLoadLock is not null)
                Monitor.Exit(coarseLoadLock);
        }

        HnswGraph? graph = loadTask is null
            ? null
            : await loadTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        Assert.True(startedInTime, "The independent field load did not start.");
        Assert.True(completedWhileOtherFieldWasInitialising,
            "First-touch for one vector field waited on the segment-wide lock used by other fields.");
        Assert.Equal(DocumentCount, graph!.NodeCount);
    }

    [Fact]
    public async Task ParallelMultiFieldFirstTouches_AreIsolatedAcrossRepeatedReaderLifetimes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SegmentInfo info = BuildIndex();
        const int rounds = 8;
        const int documentId = 5;

        for (int round = 0; round < rounds; round++)
        {
            using var directory = new MMapDirectory(_path);
            using var reader = new SegmentReader(directory, info);
            var tasks = Enumerable.Range(0, VectorFieldCount)
                .Select(field => Task.Run(() =>
                {
                    HnswGraph? graph = reader.GetHnswGraph(FieldName(field));
                    float[]? vector = reader.GetVector(FieldName(field), documentId);
                    return (Graph: graph, Vector: vector);
                }, cancellationToken))
                .ToArray();

            var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            for (int field = 0; field < VectorFieldCount; field++)
            {
                Assert.Equal(DocumentCount, results[field].Graph!.NodeCount);
                Assert.Equal(ExpectedVector(field, documentId), results[field].Vector);
            }
        }

        System.IO.Directory.Delete(_path, recursive: true);
        Assert.False(System.IO.Directory.Exists(_path));
    }

    private SegmentInfo BuildIndex()
    {
        using (var directory = new MMapDirectory(_path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            DurableCommits = false,
            HnswBuildConfig = new HnswBuildConfig { M = 4, M0 = 8, EfConstruction = 16 },
            HnswSeed = 1L,
            MaxBufferedDocs = DocumentCount + 1,
            MergePolicy = NoMergePolicy.Instance,
            NormaliseVectors = false,
        }))
        {
            for (int docId = 0; docId < DocumentCount; docId++)
            {
                var document = new LeanDocument();
                for (int field = 0; field < VectorFieldCount; field++)
                    document.Add(new VectorField(FieldName(field), ExpectedVector(field, docId)));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        return Assert.Single(IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)!.SegmentInfos);
    }

    private static float[] ExpectedVector(int field, int docId)
        => [field + 1f, docId + 1f, (field + 1f) * (docId + 1f), field + docId + 2f];

    private static string FieldName(int field) => $"embedding-{field}";

    // Monitor locks are thread-affine, so this bounded wait must remain synchronous
    // on the test thread while it holds the simulated competing initialisation lock.
    private static bool WaitForCompletion(Task task, TimeSpan timeout) => task.Wait(timeout);

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_path))
            System.IO.Directory.Delete(_path, recursive: true);
    }
}
