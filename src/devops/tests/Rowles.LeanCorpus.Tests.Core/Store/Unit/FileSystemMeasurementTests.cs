using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

namespace Rowles.LeanCorpus.Tests.Core.Store.Unit;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class FileSystemMeasurementTests
{
    [Fact]
    public void Durable_atomic_write_reports_bytes_flush_publication_read_open_and_delete()
    {
        using var fixture = new TestDirectoryFixture();
        using var root = new Activity("filesystem-measurement-test").Start();
        var events = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Rowles.LeanCorpus.FileSystem",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == root.TraceId)
                    events.Add(activity);
            }
        };
        ActivitySource.AddActivityListener(listener);
        string path = Path.Combine(fixture.Path, "measured.bin");
        IndexAtomicFileWriter.Write(path, durable: true, stream => stream.Write(new byte[137]));
        DirectoryFsync.SyncFile(path, strict: true);
        using (var input = FileOpenRetry.OpenReadDelete(path))
            Assert.Equal(137, input.Length);
        Assert.True(File.Exists(path));
        FileOpenRetry.Delete(path);

        Assert.Contains(events, activity => activity.OperationName == "file.written" &&
            Equals(activity.GetTagItem("bytes"), 137L));
        Assert.Single(events, activity => activity.OperationName == "file.flush");
        Assert.Single(events, activity => activity.OperationName == "file.sync");
        Assert.Single(events, activity => activity.OperationName == "directory.sync");
        Assert.Single(events, activity => activity.OperationName == "file.atomic_write" &&
            Equals(activity.GetTagItem("durable"), true));
        Assert.Single(events, activity => activity.OperationName == "file.read_open");
        Assert.Single(events, activity => activity.OperationName == "file.delete");
    }
    [Fact]
    public void Measurement_excludes_another_trace_and_retains_failed_write_bytes()
    {
        using var fixture = new TestDirectoryFixture();
        string reportPath = Path.Combine(fixture.Path, "measurement.json");
        using (var measurement = new BackupRestoreMeasurement(reportPath, 1, 3))
        {
            using (var unrelated = new Activity("another-test").SetIdFormat(ActivityIdFormat.W3C)
                .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom()).Start())
            {
                IndexAtomicFileWriter.Write(Path.Combine(fixture.Path, "unrelated.bin"), false,
                    stream => stream.Write(new byte[999]));
            }
            string failedPath = Path.Combine(fixture.Path, "failed.bin");
            Assert.Throws<InvalidDataException>(() => IndexAtomicFileWriter.Write(failedPath, true, stream =>
            {
                stream.Write(new byte[5]);
                throw new InvalidDataException("deliberate failed write");
            }));
            Assert.False(File.Exists(failedPath));
            Assert.Empty(Directory.GetFiles(fixture.Path, "*.tmp"));
        }
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var categories = report.RootElement.GetProperty("Categories");
        Assert.Equal(1, categories.GetProperty("file.written").GetProperty("Count").GetInt64());
        Assert.Equal(5, categories.GetProperty("file.written").GetProperty("Bytes").GetInt64());
        Assert.False(categories.TryGetProperty("file.flush", out _));
        Assert.Equal(1, categories.GetProperty("file.delete").GetProperty("Count").GetInt64());
        Assert.False(report.RootElement.GetProperty("Passed").GetBoolean());
    }
    [Fact]
    public void Native_sync_retry_is_included_in_the_measurement()
    {
        using var fixture = new TestDirectoryFixture();
        string reportPath = Path.Combine(fixture.Path, "retry.json");
        using (var measurement = new BackupRestoreMeasurement(reportPath, 1, 3))
        {
            var fileSystem = new TransientSyncFileSystem();
            PlatformFileSystem.SyncFile(fileSystem, "injected-sync", static _ => { });
            Assert.Equal(2, fileSystem.Attempts);
        }
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var retries = report.RootElement.GetProperty("Categories").GetProperty("retry");
        Assert.Equal(1, retries.GetProperty("Count").GetInt64());
        Assert.Equal(200, retries.GetProperty("RetryDelayMilliseconds").GetInt64());
    }

    private sealed class TransientSyncFileSystem : IPlatformFileSystem
    {
        public int Attempts { get; private set; }
        public void SyncFile(string path)
        {
            if (++Attempts == 1)
                throw new IOException("injected sharing violation", unchecked((int)0x80070020));
        }
        public DirectorySyncResult SyncDirectory(string path) => DirectorySyncResult.Succeeded;
        public bool IsTransient(Exception exception) => WindowsFileSystem.Instance.IsTransient(exception);
    }
    [Fact]
    public void Physical_mapping_open_close_and_drain_are_measured()
    {
        using var fixture = new TestDirectoryFixture();
        string reportPath = Path.Combine(fixture.Path, "mapping.json");
        string inputPath = Path.Combine(fixture.Path, "input.bin");
        File.WriteAllBytes(inputPath, [42]);
        using (var measurement = new BackupRestoreMeasurement(reportPath, 1, 3))
        {
            using var input = new IndexInput(inputPath);
            Assert.Equal(42, input.ReadByte());
        }
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var categories = report.RootElement.GetProperty("Categories");
        Assert.Equal(1, categories.GetProperty("mmap.open").GetProperty("Count").GetInt64());
        Assert.Equal(1, categories.GetProperty("mmap.close").GetProperty("Count").GetInt64());
        Assert.Equal(1, categories.GetProperty("input.drain").GetProperty("Count").GetInt64());
    }
    [Fact]
    public void Finaliser_mapping_close_keeps_its_opening_trace()
    {
        using var fixture = new TestDirectoryFixture();
        string reportPath = Path.Combine(fixture.Path, "finaliser.json");
        string inputPath = Path.Combine(fixture.Path, "input.bin");
        File.WriteAllBytes(inputPath, [42]);
        using (var measurement = new BackupRestoreMeasurement(reportPath, 1, 3))
        {
            var reference = CreateUnclosedInput(inputPath);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(reference.IsAlive);
        }
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var categories = report.RootElement.GetProperty("Categories");
        Assert.Equal(1, categories.GetProperty("mmap.finaliser_close").GetProperty("Count").GetInt64());
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnclosedInput(string path)
    {
        var input = new IndexInput(path);
        Assert.Equal(42, input.ReadByte());
        return new WeakReference(input);
    }
    [Fact]
    public void Queued_finaliser_deletion_keeps_the_test_trace()
    {
        using var fixture = new TestDirectoryFixture();
        string inputPath = Path.Combine(fixture.Path, "input.bin");
        File.WriteAllBytes(inputPath, [42]);
        using var directory = new MMapDirectory(fixture.Path);
        using var measurement = new BackupRestoreMeasurement(Path.Combine(fixture.Path, "queued-delete.json"), 1, 3);
        var traceId = Activity.Current!.TraceId;
        using var completed = new ManualResetEventSlim();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Rowles.LeanCorpus.FileSystem",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Parent.TraceId == traceId ? ActivitySamplingResult.AllData : ActivitySamplingResult.None,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "delete.finaliser" && activity.TraceId == traceId)
                    completed.Set();
            }
        };
        ActivitySource.AddActivityListener(listener);
        var reference = CreateUnclosedDirectoryInput(directory);
        directory.DeleteFile("input.bin");
        Assert.True(File.Exists(inputPath));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
        Assert.True(SpinWait.SpinUntil(() => !File.Exists(inputPath), TimeSpan.FromSeconds(3)));
        Assert.True(completed.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken),
            "Queued finaliser deletion did not retain its test trace.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateUnclosedDirectoryInput(MMapDirectory directory)
    {
        var input = directory.OpenInput("input.bin");
        Assert.Equal(42, input.ReadByte());
        return new WeakReference(input);
    }
}
