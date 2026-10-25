using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Foundation;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class MMapLifetimeTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "ll_mmap_lifetime_" + Guid.NewGuid().ToString("N"));

    [Fact(DisplayName = "IndexInput: Dispose Waits For Active Mapping Lease")]
    public void Dispose_ActiveMappingLease_WaitsBeforeUnmapping()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "input.bin");
        File.WriteAllBytes(filePath, [42]);
        var input = new IndexInput(filePath);
        var lease = input.AcquireLifetimeLease();

        using var dispose = new DedicatedThreadOperation(input.Dispose, "index-input-dispose");
        try
        {
            WaitUntilRejected(() => input.AcquireLifetimeLease());
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            lease.Dispose();
        }
        dispose.Join();
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
    }

    [Fact(DisplayName = "MMap Directory: Dispose Waits For Active Input Mapping Lease")]
    public void Dispose_ActiveInputLease_WaitsBeforeClosingTrackedInput()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using (var output = directory.CreateOutput("input.bin"))
            output.WriteByte(42);
        var input = directory.OpenInput("input.bin");
        var lease = input.AcquireLifetimeLease();

        using var dispose = new DedicatedThreadOperation(directory.Dispose, "mmap-directory-dispose");
        try
        {
            WaitUntilRejected(() => directory.AcquireOperationLease());
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            lease.Dispose();
        }
        dispose.Join();
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

    private static void WaitUntilRejected(Func<IDisposable> acquire)
    {
        bool rejected = SpinWait.SpinUntil(
            () =>
            {
                try
                {
                    acquire().Dispose();
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            },
            TimeSpan.FromSeconds(5));

        Assert.True(rejected, "Disposal did not reject new mapping leases.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_path, recursive: true); }
        catch { }
    }
}
