using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class MMapLifetimeTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "ll_mmap_lifetime_" + Guid.NewGuid().ToString("N"));

    [Fact(DisplayName = "IndexInput: Dispose Waits For Active Mapping Lease")]
    public async Task Dispose_ActiveMappingLease_WaitsBeforeUnmapping()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "input.bin");
        File.WriteAllBytes(filePath, [42]);
        var input = new IndexInput(filePath);
        var lease = input.AcquireLifetimeLease();

        var dispose = Task.Run(input.Dispose);
        try
        {
            await WaitUntilRejectedAsync(() => input.AcquireLifetimeLease());
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            lease.Dispose();
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
    }

    [Fact(DisplayName = "MMap Directory: Dispose Waits For Active Input Mapping Lease")]
    public async Task Dispose_ActiveInputLease_WaitsBeforeClosingTrackedInput()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using (var output = directory.CreateOutput("input.bin"))
            output.WriteByte(42);
        var input = directory.OpenInput("input.bin");
        var lease = input.AcquireLifetimeLease();

        var dispose = Task.Run(directory.Dispose);
        try
        {
            await WaitUntilRejectedAsync(() => directory.AcquireOperationLease());
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            lease.Dispose();
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
        input.Dispose();
    }

    [Fact(DisplayName = "IndexInput: Public ReadSpan Remains Valid After Disposal")]
    public void ReadSpan_DisposedInput_ReturnedDataRemainsValid()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "span.bin");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        var input = new IndexInput(filePath);

        ReadOnlySpan<byte> bytes = input.ReadSpan(3);
        input.Dispose();

        Assert.Equal([1, 2, 3], bytes.ToArray());
    }

    private static async Task WaitUntilRejectedAsync(Func<IDisposable> acquire)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token, TestContext.Current.CancellationToken);
        while (true)
        {
            try
            {
                acquire().Dispose();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            await Task.Delay(1, cancellation.Token);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_path, recursive: true); }
        catch { }
    }
}
