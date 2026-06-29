using System.ComponentModel;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Rowles.LeanCorpus.Tests.Shared.Infrastructure;

/// <summary>
/// Retries a test up to <see cref="MaxRetries"/> times before marking it failed.
/// Use on tests suspected to be flaky to distinguish intermittent from deterministic failures.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer(
    "Rowles.LeanCorpus.Tests.Shared.Infrastructure.RetryFactDiscoverer",
    "Rowles.LeanCorpus.Tests.Shared")]
public sealed class RetryFactAttribute : FactAttribute
{
    public int MaxRetries { get; }
    public RetryFactAttribute(int maxRetries = 3) => MaxRetries = maxRetries;
}

// Discoverer

public sealed class RetryFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnostics;
    public RetryFactDiscoverer(IMessageSink diagnostics) => _diagnostics = diagnostics;

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
    {
        // MaxRetries is a constructor parameter, not a named property assignment.
        // GetNamedArgument returns 0 when the attribute user doesn't write
        // MaxRetries=… explicitly, causing the test to never execute.
        var ctorArgs = factAttribute.GetConstructorArguments().ToList();
        var maxRetries = ctorArgs is { Count: > 0 } && ctorArgs[0] is int v ? v : 3;
        yield return new RetryTestCase(
            _diagnostics,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod,
            maxRetries);
    }
}

// Test case

public sealed class RetryTestCase : XunitTestCase
{
    private int _maxRetries;

    [Obsolete("De-serializer only - do not call directly")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public RetryTestCase() { }

    public RetryTestCase(
        IMessageSink diagnostics,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        int maxRetries = 3)
        : base(diagnostics, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
        _maxRetries = maxRetries;
    }

    public override async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        for (var attempt = 1; attempt <= _maxRetries; attempt++)
        {
            var buffer = new DelayedMessageBus(messageBus);
            var summary = await base.RunAsync(
                diagnosticMessageSink, buffer, constructorArguments,
                new ExceptionAggregator(), cancellationTokenSource);

            if (summary.Failed == 0)
            {
                buffer.Flush();
                return summary;
            }

            if (attempt == _maxRetries)
            {
                // Last attempt - forward everything so the runner sees the failure.
                buffer.Flush();
                return summary;
            }

            // Failed but retries remain - discard buffered messages and try again.
            diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                "'{0}' failed (attempt {1}/{2}), retrying...",
                DisplayName, attempt, _maxRetries));

            buffer.Discard();
        }

        // Unreachable - loop always returns inside.
        return new RunSummary { Total = 1, Failed = 1 };
    }

    public override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue("MaxRetries", _maxRetries);
    }

    public override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        _maxRetries = data.GetValue<int>("MaxRetries");
    }
}

// Message bus buffer

/// <summary>Buffers xunit messages; either forwards them or discards them on demand.</summary>
internal sealed class DelayedMessageBus : IMessageBus
{
    private readonly IMessageBus _inner;
    private readonly List<IMessageSinkMessage> _messages = [];

    public DelayedMessageBus(IMessageBus inner) => _inner = inner;

    public bool QueueMessage(IMessageSinkMessage message)
    {
        lock (_messages) _messages.Add(message);
        return true; // never signal early-abort during buffering
    }

    /// <summary>Forwards all buffered messages to the real bus.</summary>
    public void Flush()
    {
        lock (_messages)
        {
            foreach (var m in _messages) _inner.QueueMessage(m);
            _messages.Clear();
        }
    }

    /// <summary>Discards all buffered messages (used when a retry will follow).</summary>
    public void Discard()
    {
        lock (_messages) _messages.Clear();
    }

    public void Dispose() => Flush();
}
