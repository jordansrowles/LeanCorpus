using System.Runtime.ExceptionServices;

namespace Rowles.LeanCorpus.Tests.Core.Foundation;

internal sealed class DedicatedThreadOperation : IDisposable
{
    private static readonly TimeSpan GuardTimeout = TimeSpan.FromSeconds(10);
    private readonly ManualResetEventSlim _started = new();
    private readonly ManualResetEventSlim _completed = new();
    private readonly Thread _thread;
    private Exception? _failure;

    internal DedicatedThreadOperation(Action operation, string name)
    {
        _thread = new Thread(() =>
        {
            _started.Set();
            try { operation(); }
            catch (Exception exception) { _failure = exception; }
            finally { _completed.Set(); }
        })
        {
            IsBackground = false,
            Name = name,
        };
        _thread.Start();
        WaitFor(_started, "thread started");
    }

    internal bool IsCompleted => _completed.IsSet;

    internal void Join()
    {
        WaitFor(_completed, "thread completed");
        if (!_thread.Join(GuardTimeout))
            throw new TimeoutException($"Dedicated thread '{_thread.Name}' did not join.");
        if (_failure is not null)
            ExceptionDispatchInfo.Capture(_failure).Throw();
    }

    internal static void WaitFor(ManualResetEventSlim signal, string state)
    {
        if (!signal.Wait(GuardTimeout))
            throw new TimeoutException($"Timed out waiting for expected state: {state}.");
    }

    public void Dispose()
    {
        Join();
        _started.Dispose();
        _completed.Dispose();
    }
}
