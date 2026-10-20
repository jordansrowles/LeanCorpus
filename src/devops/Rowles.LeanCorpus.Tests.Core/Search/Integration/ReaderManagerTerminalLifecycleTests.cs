namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class ReaderManagerTerminalLifecycleTests
{
    [Fact]
    public void DisposalRetiresReaderButKeepsOutstandingLeaseUsable()
    {
        var reader = new Reader();
        using var manager = new ReaderManager<Reader>(() => reader, _ => null, TimeSpan.FromHours(1));
        using var lease = manager.AcquireLease();

        manager.Dispose();

        Assert.False(reader.Disposed);
        Assert.Throws<ObjectDisposedException>(() => manager.AcquireLease());
        lease.Dispose();
        Assert.True(reader.Disposed);
        Assert.Equal(1, manager.GetDiagnostics().DisposedReaders);
    }

    [Fact]
    public void DiagnosticsCountLeasesOnRetiredGenerationsUntilFinalRelease()
    {
        var first = new Reader();
        var second = new Reader();
        using var manager = new ReaderManager<Reader>(() => first, _ => second, TimeSpan.FromHours(1));
        using var oldLease = manager.AcquireLease();
        Assert.True(manager.MaybeRefresh());
        using var newLease = manager.AcquireLease();

        Assert.Equal(2, manager.GetDiagnostics().ActiveLeases);
        manager.Dispose();
        Assert.Equal(2, manager.GetDiagnostics().ActiveLeases);
        oldLease.Dispose();
        Assert.Equal(1, manager.GetDiagnostics().ActiveLeases);
        newLease.Dispose();
        Assert.Equal(0, manager.GetDiagnostics().ActiveLeases);
        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
    }

    [Fact]
    public void CopiedLeaseCannotReleaseAnotherCallersReference()
    {
        var reader = new Reader();
        using var manager = new ReaderManager<Reader>(() => reader, _ => null, TimeSpan.FromHours(1));
        var first = manager.AcquireLease();
        var copy = first;
        var second = manager.AcquireLease();
        manager.Dispose();

        first.Dispose();
        copy.Dispose();
        Assert.False(reader.Disposed);
        second.Dispose();
        Assert.True(reader.Disposed);
        Assert.Equal(1, manager.GetDiagnostics().DisposedReaders);
    }

    private sealed class Reader : IDisposable
    {
        internal bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
