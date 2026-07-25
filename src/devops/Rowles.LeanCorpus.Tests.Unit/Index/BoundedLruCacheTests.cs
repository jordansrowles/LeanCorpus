using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Tests.Unit.Index;

[Trait("Category", "Index")]
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

    private sealed class TestState : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
