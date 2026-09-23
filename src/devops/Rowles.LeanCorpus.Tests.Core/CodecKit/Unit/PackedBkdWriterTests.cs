using System.Diagnostics;
using System.Collections.Concurrent;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class PackedBkdWriterTests
{
    [Fact(DisplayName = "DWPT packed BKD capacity is tracked without metadata enumeration")]
    public void DocumentsWriterTracksPackedBkdCapacity()
    {
        var dwpt = new DocumentsWriterPerThread(
            new WhitespaceAnalyser(),
            new Dictionary<string, IAnalyser>(),
            new IndexWriterConfig { DurableCommits = false });
        try
        {
            long before = dwpt.EstimatedRamBytes;
            Span<byte> packed = stackalloc byte[8];
            XYEncodingUtils.Encode(12, packed[..4]);
            XYEncodingUtils.Encode(34, packed[4..]);
            dwpt.AddPackedBkdValue("location", PackedBkdConfig.Point2D(), packed, 0);
            Assert.True(dwpt.EstimatedRamBytes > before);
        }
        finally
        {
            dwpt.Dispose();
        }
        Assert.Equal(0, dwpt.EstimatedRamBytes);
    }

    [Fact(DisplayName = "Packed BKD document remapping preserves the known unique count")]
    public void FieldBuffer_RemapPreservesKnownUniqueDocumentCount()
    {
        using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
        Assert.Equal(10, buffer.UniqueDocumentCount);

        buffer.RemapDocumentIds([9, 8, 7, 6, 5, 4, 3, 2, 1, 0]);

        Assert.False(buffer.DocumentIdsAreOrdered);
        Assert.Equal(10, buffer.UniqueDocumentCount);
    }

    [Fact(DisplayName = "Packed BKD writes deterministic multidimensional fields")]
    public void WriterReader_RoundTripsAndIsDeterministic()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            using var first = PackedBkdTestSupport.CreateBuffer(reverse: false);
            using var second = PackedBkdTestSupport.CreateBuffer(reverse: true);
            string firstPath = Path.Combine(directory, "first.pbkd");
            string secondPath = Path.Combine(directory, "second.pbkd");
            PackedBkdWriter.Write(firstPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = first });
            PackedBkdWriter.Write(secondPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = second });
            Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));

            using var reader = PackedBkdReader.Open(firstPath);
            var metadata = reader.GetFieldMetadata("location");
            Assert.Equal(10, metadata.PointCount);
            Assert.Equal(10, metadata.DocumentCount);
            Assert.Equal(2, metadata.Config.Dimensions);
            Assert.True(metadata.LeafCount > 1);

            var visitor = new PackedBkdTestSupport.RangeVisitor(new XYPoint(2, 2), new XYPoint(7, 7));
            Assert.True(reader.Intersect("location", ref visitor));
            Assert.Equal(Enumerable.Range(2, 6), visitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD spill files are cleaned after success")]
    public void Writer_SpillPathIsCleaned()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "spill.pbkd");
            using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
            PackedBkdWriter.Write(path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD in-memory and spill builds are byte-identical")]
    public void Writer_SpillBuildMatchesInMemoryBuild()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string memoryPath = Path.Combine(directory, "memory.pbkd");
            string spillPath = Path.Combine(directory, "spill.pbkd");
            using var memory = PackedBkdTestSupport.CreateBuffer(reverse: false);
            using var spill = PackedBkdTestSupport.CreateBuffer(reverse: false);
            PackedBkdWriter.Write(memoryPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = memory });
            PackedBkdWriter.Write(spillPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = spill },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true));

            Assert.Equal(File.ReadAllBytes(memoryPath), File.ReadAllBytes(spillPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD reports a build peak within the hard memory budget")]
    public void Writer_ReportsBuildPeakWithinBudget()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        var captured = new ConcurrentBag<Activity>();
        using var testSource = new ActivitySource("Rowles.LeanCorpus.Tests.PackedBkd");
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "Rowles.LeanCorpus" or "Rowles.LeanCorpus.Tests.PackedBkd",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => captured.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        try
        {
            const long budget = 64L * 1024;
            using var scope = testSource.StartActivity("packed-bkd-budget-test")
                ?? throw new InvalidOperationException("The Packed BKD test scope activity could not be started.");
            string path = Path.Combine(directory, "budget.pbkd");
            using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
            PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(budget, directory));

            Activity activity = Assert.Single(captured,
                candidate => candidate.RootId == scope.RootId
                    && candidate.OperationName == LeanCorpusActivitySource.PackedBkdBuild);
            long peak = Assert.IsType<long>(activity.GetTagItem("packed_bkd.build_peak_bytes"));
            Assert.InRange(peak, 0, budget);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD writes multiple fields in UTF-8 ordinal order")]
    public void Writer_OrdersMultipleFieldsDeterministically()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "multiple-fields.pbkd");
            using var zeta = PackedBkdTestSupport.CreateBuffer(reverse: false);
            using var alpha = PackedBkdTestSupport.CreateBuffer(reverse: true);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["zeta"] = zeta,
                ["alpha"] = alpha
            });

            using var input = new IndexInput(path);
            using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            using var body = session.OpenBodyInput();
            body.Seek(body.Length - 16);
            _ = body.ReadInt32();
            Assert.Equal(2, body.ReadInt32());
            long directoryOffset = body.ReadInt64();
            body.Seek(directoryOffset);
            Assert.Equal(2, body.ReadInt32());
            Assert.Equal("alpha", body.ReadLengthPrefixedString());
            _ = body.ReadInt64();
            _ = body.ReadInt64();
            Assert.Equal("zeta", body.ReadLengthPrefixedString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD cancellation removes spill artefacts")]
    public void Writer_CancellationCleansSpillArtifacts()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "cancelled.pbkd");
            using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true, CancellationToken: cancellation.Token)));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD active in-memory cancellation cleans output and preserves the source")]
    public async Task Writer_ActiveMemoryCancellationCleansOutput()
        => await AssertActiveCancellationCleansAsync(forceSpill: false);

    [Fact(DisplayName = "Packed BKD active spill cancellation cleans output and preserves the source")]
    public async Task Writer_ActiveSpillCancellationCleansOutput()
        => await AssertActiveCancellationCleansAsync(forceSpill: true);

    [Fact(DisplayName = "Packed BKD failed spill setup removes the output and preserves the source buffer")]
    public void Writer_FailedSpillSetupCleansOutput()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string blockedPath = Path.Combine(directory, "spill-blocker");
            File.WriteAllBytes(blockedPath, [1]);
            string path = Path.Combine(directory, "failed.pbkd");
            using var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false);
            Assert.ThrowsAny<IOException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, blockedPath, ForceSpill: true)));
            Assert.False(File.Exists(path));
            Assert.Equal(10, buffer.Count);
            buffer.Append(buffer.Records[..8], 10);
            Assert.Equal(11, buffer.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD supports the legal leaf size extremes")]
    public void Writer_SupportsLeafSizeExtremes()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            foreach (int leafSize in new[] { 1, 4096 })
            {
                string path = Path.Combine(directory, $"leaf-{leafSize}.pbkd");
                using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: leafSize));
                for (int document = 0; document < 3; document++)
                    PackedBkdTestSupport.AppendPoint(buffer, document, document, document);
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
                using var reader = PackedBkdReader.Open(path);
                Assert.Equal(leafSize == 1 ? 3 : 1, reader.GetFieldMetadata("location").LeafCount);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an impossible build budget without leaving spill files")]
    public void Writer_RejectsInsufficientBuildBudgetWithoutSpill()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "insufficient-budget.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 4096));
            PackedBkdTestSupport.AppendPoint(buffer, 0, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true)));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task AssertActiveCancellationCleansAsync(bool forceSpill)
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            const int pointCount = 1_000_000;
            string path = Path.Combine(directory, forceSpill ? "active-spill-cancelled.pbkd" : "active-memory-cancelled.pbkd");
            using var buffer = CreateLargeBuffer(pointCount);
            using var cancellation = new CancellationTokenSource();
            var buildStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var listener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == "Rowles.LeanCorpus",
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    if (activity.OperationName == LeanCorpusActivitySource.PackedBkdBuild)
                        buildStarted.TrySetResult();
                }
            };
            ActivitySource.AddActivityListener(listener);

            Task write = Task.Run(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(
                    MemoryBudgetBytes: forceSpill ? 64L * 1024 * 1024 : 512L * 1024 * 1024,
                    SpillDirectory: directory,
                    ForceSpill: forceSpill,
                    CancellationToken: cancellation.Token)));

            await buildStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);
            Assert.False(write.IsCompleted, "The cancellation test did not reach an active build.");
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await write.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
            Assert.Equal(pointCount, buffer.Count);
            PackedBkdTestSupport.AppendPoint(buffer, 0, 0, pointCount);
            Assert.Equal(pointCount + 1, buffer.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PackedBkdFieldBuffer CreateLargeBuffer(int pointCount)
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 512));
        Span<byte> packed = stackalloc byte[8];
        for (int point = 0; point < pointCount; point++)
        {
            XYEncodingUtils.Encode(point % 10_000, packed[..4]);
            XYEncodingUtils.Encode(point / 10_000, packed[4..]);
            buffer.Append(packed, point / 2);
        }
        return buffer;
    }
}
