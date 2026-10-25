using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentReaderLeaseFailureTests
{
    [Fact]
    public void Dispose_AccountingFailureStillReleasesOperationAndDirectoryLeases()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ll-reader-lease-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            var state = new SegmentReaderState(directory, new SegmentDescriptor(new SegmentInfo
            {
                SegmentId = "seg_0",
                DocCount = 0,
                LiveDocCount = 0,
            }));
            var accountingFailure = new InvalidOperationException("resource accounting failed");
            int accountingCalls = 0;
            using var cache = new BoundedLruCache<string, SegmentReaderState>(
                1,
                StringComparer.Ordinal,
                resourceUsageSelector: _ =>
                {
                    if (Interlocked.Increment(ref accountingCalls) == 1)
                        return default;
                    throw accountingFailure;
                });
            var cacheLease = cache.Acquire("seg_0", () => state);
            var operationOwner = new CountingLifetimeOwner();
            var directoryOwner = new CountingLifetimeOwner();
            var readerLease = new SegmentReaderLease(
                cacheLease,
                new LifetimeLease(operationOwner, new object()),
                new LifetimeLease(directoryOwner, new object()));

            Exception? failure = Record.Exception(readerLease.Dispose);

            Assert.Same(accountingFailure, failure);
            Assert.Equal(1, operationOwner.ReleaseCount);
            Assert.Equal(1, directoryOwner.ReleaseCount);
            Assert.Equal(0, cache.Count);
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private sealed class CountingLifetimeOwner : ILifetimeLeaseOwner
    {
        internal int ReleaseCount { get; private set; }

        public void ReleaseLease(object token) => ReleaseCount++;
    }
}
