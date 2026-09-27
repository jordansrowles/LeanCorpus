using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class BoundedLruCacheTests
{
    [Fact(DisplayName = "Segment Reader Cache: Least Recently Used Inactive Entry Is Evicted")]
    public void Acquire_OverCapacity_EvictsLeastRecentlyUsedInactiveEntry()
    {
        using var cache = new BoundedLruCache<string, TestState>(2, StringComparer.Ordinal);
        var a = new TestState();
        var b = new TestState();
        var c = new TestState();

        using (cache.Acquire("a", () => a)) { }
        using (cache.Acquire("b", () => b)) { }
        using (cache.Acquire("a", () => throw new InvalidOperationException())) { }
        using (cache.Acquire("c", () => c)) { }

        Assert.False(a.Disposed);
        Assert.True(b.Disposed);
        Assert.False(c.Disposed);
        Assert.Equal(2, cache.Count);
    }

    [Fact(DisplayName = "Segment Reader Cache: Active Lease Is Protected Until Release")]
    public void Acquire_ActiveEntry_ProtectsEntryUntilRelease()
    {
        using var cache = new BoundedLruCache<string, TestState>(1, StringComparer.Ordinal);
        var a = new TestState();
        var b = new TestState();

        var leaseA = cache.Acquire("a", () => a);
        var leaseB = cache.Acquire("b", () => b);
        Assert.Equal(2, cache.Count);
        Assert.False(a.Disposed);

        leaseB.Dispose();
        Assert.Equal(1, cache.Count);
        Assert.True(b.Disposed);
        Assert.False(a.Disposed);
        leaseA.Dispose();
    }

    [Fact(DisplayName = "Segment Reader Cache: Copied Lease Releases Only Once")]
    public void Acquire_CopiedLease_ReleasesOnlyOnce()
    {
        using var cache = new BoundedLruCache<string, TestState>(1, StringComparer.Ordinal);
        var state = new TestState();
        var first = cache.Acquire("a", () => state);
        var second = cache.Acquire("a", () => state);
        var firstCopy = first;

        firstCopy.Dispose();
        first.Dispose();
        cache.Dispose();

        Assert.False(state.Disposed);

        second.Dispose();
        Assert.True(state.Disposed);
    }

    [Fact(DisplayName = "Segment Reader Cache: Copied Detached Lifetime Lease Releases Only Once")]
    public void DetachedLifetimeLease_CopiedLease_ReleasesOnlyOnce()
    {
        using var cache = new BoundedLruCache<string, TestState>(1, StringComparer.Ordinal);
        var state = new TestState();
        var first = cache.Acquire("a", () => state);
        var lifetimeLease = first.Detach();
        var lifetimeCopy = lifetimeLease;
        var second = cache.Acquire("a", () => state);

        first.Dispose();
        lifetimeCopy.Dispose();
        lifetimeLease.Dispose();
        cache.Dispose();

        Assert.False(state.Disposed);

        second.Dispose();
        Assert.True(state.Disposed);
    }

    [Fact(DisplayName = "Segment Reader Cache: Evicted Entry Reloads")]
    public void Acquire_EvictedEntry_Reloads()
    {
        using var cache = new BoundedLruCache<string, TestState>(1, StringComparer.Ordinal);
        using (cache.Acquire("a", static () => new TestState())) { }
        using (cache.Acquire("b", static () => new TestState())) { }
        using (cache.Acquire("a", static () => new TestState())) { }

        Assert.Equal(3, cache.LoadCount);
        Assert.Equal(1, cache.Count);
    }

    [Fact(DisplayName = "Segment Reader Cache: Weighted Entry Stays Leased Then Evicts Over Budget")]
    public void Acquire_WeightedEntry_OverBudgetStateWaitsForLastLeaseThenEvicts()
    {
        using var cache = new BoundedLruCache<string, TestState>(
            4,
            StringComparer.Ordinal,
            maxRetainedBytes: 100,
            resourceUsageSelector: static state => new SegmentReaderCacheResourceUsage(
                0, 0, 0, 0, 0, 0, 0, 0, 0, state.RetainedBytes));
        var state = new TestState(retainedBytes: 120);

        var lease = cache.Acquire("large", () => state);
        var whileLeased = cache.Metrics;
        Assert.Equal(1, whileLeased.EntryCount);
        Assert.Equal(120, whileLeased.RetainedBytes);
        Assert.False(state.Disposed);

        lease.Dispose();

        var afterRelease = cache.Metrics;
        Assert.Equal(0, afterRelease.EntryCount);
        Assert.Equal(0, afterRelease.RetainedBytes);
        Assert.Equal(1, afterRelease.EvictionCount);
        Assert.True(state.Disposed);
    }

    [Fact(DisplayName = "Segment Reader Cache: Concurrent First Load Runs Factory Once")]
    public async Task Acquire_ConcurrentFirstLoad_RunsFactoryOnce()
    {
        using var cache = new BoundedLruCache<string, TestState>(4, StringComparer.Ordinal);
        int loads = 0;
        using var gate = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            gate.Wait();
            using var lease = cache.Acquire("shared", () =>
            {
                Interlocked.Increment(ref loads);
                Thread.Sleep(10);
                return new TestState();
            });
            Assert.NotNull(lease.Value);
        })).ToArray();

        gate.Set();
        await Task.WhenAll(tasks);
        Assert.Equal(1, loads);
        Assert.Equal(1, cache.LoadCount);
    }

    [Fact(DisplayName = "Segment Reader Cache: Dispose Waits For Active Lease")]
    public void Dispose_ActiveLease_DisposesAfterRelease()
    {
        var cache = new BoundedLruCache<string, TestState>(1, StringComparer.Ordinal);
        var state = new TestState();
        var lease = cache.Acquire("a", () => state);

        cache.Dispose();
        Assert.False(state.Disposed);
        lease.Dispose();
        Assert.True(state.Disposed);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Acquire_EvictionDisposeFailure_DoesNotCancelSuccessfulAcquisition()
    {
        var reportedFailures = new List<AggregateException>();
        using var cache = new BoundedLruCache<string, TestState>(
            1, StringComparer.Ordinal, reportedFailures.Add);
        var evicted = new TestState(throwOnDispose: true, name: "evicted");
        using (cache.Acquire("evicted", () => evicted)) { }

        var loaded = new TestState();
        using var loadedLease = cache.Acquire("loaded", () => loaded);
        Assert.Same(loaded, loadedLease.Value);
        Assert.Equal(1, cache.Count);

        using var cachedLease = cache.Acquire("loaded", static () => throw new InvalidOperationException());
        Assert.Same(loaded, cachedLease.Value);

        loadedLease.Dispose();
        var cleanupFailure = Assert.Single(reportedFailures);
        Assert.Collection(cleanupFailure.InnerExceptions,
            exception => Assert.Equal("evicted", exception.Message));

        cachedLease.Dispose();
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Dispose_AttemptsEveryValueAndAggregatesFailures()
    {
        using var cache = new BoundedLruCache<string, TestState>(3, StringComparer.Ordinal);
        var first = new TestState(throwOnDispose: true, name: "first");
        var second = new TestState();
        var third = new TestState(throwOnDispose: true, name: "third");
        using (cache.Acquire("first", () => first)) { }
        using (cache.Acquire("second", () => second)) { }
        using (cache.Acquire("third", () => third)) { }

        var cleanupFailure = Assert.Throws<AggregateException>(() => cache.Dispose());

        Assert.Equal(2, cleanupFailure.InnerExceptions.Count);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        Assert.True(third.Disposed);
    }

    [Fact]
    public void Acquire_FactoryFailureLeavesUnrelatedEntryUsable()
    {
        using var cache = new BoundedLruCache<string, TestState>(2, StringComparer.Ordinal);
        var existing = new TestState();
        using (cache.Acquire("existing", () => existing)) { }

        Assert.Throws<InvalidOperationException>(() => cache.Acquire(
            "failed", static () => throw new InvalidOperationException("factory failure")));

        using var lease = cache.Acquire("existing", static () => throw new InvalidOperationException());
        Assert.Same(existing, lease.Value);
        Assert.Equal(1, cache.Count);
    }

    private sealed class TestState : IDisposable
    {
        private readonly bool _throwOnDispose;
        private readonly string _name;

        internal TestState(bool throwOnDispose = false, string name = "test state", long retainedBytes = 0)
        {
            _throwOnDispose = throwOnDispose;
            _name = name;
            RetainedBytes = retainedBytes;
        }

        internal bool Disposed { get; private set; }
        internal long RetainedBytes { get; set; }

        public void Dispose()
        {
            Disposed = true;
            if (_throwOnDispose)
                throw new InvalidOperationException(_name);
        }
    }
}
