using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Rowles.LeanCorpus.Tests.Unit.Infrastructure;

/// <summary>
/// Contains unit tests for <see cref="RetryFactAttribute"/>.
/// </summary>
[Trait("Category", "UnitTest")]
public sealed class RetryFactAttributeTests
{
    [Fact(DisplayName = "RetryFactAttribute: default constructor sets MaxRetries to 3")]
    public void DefaultCtor_MaxRetriesIsThree()
    {
        var attr = new RetryFactAttribute();

        Assert.Equal(3, attr.MaxRetries);
    }

    [Fact(DisplayName = "RetryFactAttribute: custom MaxRetries value is stored")]
    public void CustomCtor_MaxRetriesIsStored()
    {
        var attr = new RetryFactAttribute(7);

        Assert.Equal(7, attr.MaxRetries);
    }

    [Fact(DisplayName = "RetryFactAttribute: targets only methods")]
    public void AttributeUsage_TargetsMethod()
    {
        var usage = (AttributeUsageAttribute)typeof(RetryFactAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)[0];

        Assert.Equal(AttributeTargets.Method, usage.ValidOn);
    }

    [Fact(DisplayName = "RetryFactAttribute: AllowMultiple is false")]
    public void AttributeUsage_AllowMultipleIsFalse()
    {
        var usage = (AttributeUsageAttribute)typeof(RetryFactAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)[0];

        Assert.False(usage.AllowMultiple);
    }
}

/// <summary>
/// Contains unit tests for <see cref="RetryFactDiscoverer"/> and <see cref="RetryTestCase"/>,
/// including a functional meta-test that exercises <see cref="RetryTestCase.RunAsync"/> end-to-end.
/// </summary>
[Trait("Category", "UnitTest")]
public sealed class RetryFactDiscovererTests
{
    // ── Stubs for xunit v2 interfaces ─────────────────────────────────────────

    private sealed class NullMessageSink : LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }

    private sealed class DictionarySerialiser : IXunitSerializationInfo
    {
        private readonly Dictionary<string, object?> _store = new();

        public void AddValue(string key, object value, Type? type = null) => _store[key] = value;

        public T GetValue<T>(string key)
            => _store.TryGetValue(key, out var v) && v is T t ? t : default!;

        public object? GetValue(string key, Type type)
            => _store.TryGetValue(key, out var v) ? v : null;

        public string ToSerializedString()
            => string.Join(";", _store.Select(p => $"{p.Key}={p.Value}"));
    }

    private sealed class StubDiscoveryOptions : ITestFrameworkDiscoveryOptions
    {
        public TValue GetValue<TValue>(string name) => default!;
        public void SetValue<TValue>(string name, TValue value) { }
    }

    private sealed class StubAttributeInfo(int maxRetries) : LongLivedMarshalByRefObject, IAttributeInfo
    {
        public TValue GetNamedArgument<TValue>(string argumentName)
            => argumentName == "MaxRetries" ? (TValue)(object)maxRetries : default!;

        public IEnumerable<object> GetConstructorArguments() => [];

        public IEnumerable<IAttributeInfo> GetCustomAttributes(string assemblyQualifiedAttributeTypeName)
            => [];
    }

    private sealed class StubAssemblyInfo : LongLivedMarshalByRefObject, IAssemblyInfo
    {
        public string AssemblyPath => string.Empty;
        public string Name => "StubAssembly";
        public IEnumerable<IAttributeInfo> GetCustomAttributes(string name) => [];
        public ITypeInfo GetType(string typeName) => null!;
        public IEnumerable<ITypeInfo> GetTypes(bool includePrivate) => [];
    }

    private sealed class StubTypeInfo : LongLivedMarshalByRefObject, ITypeInfo
    {
        public IAssemblyInfo Assembly { get; } = new StubAssemblyInfo();
        public ITypeInfo BaseType => null!;
        public IEnumerable<ITypeInfo> Interfaces => [];
        public bool IsAbstract => false;
        public bool IsGenericParameter => false;
        public bool IsGenericType => false;
        public bool IsSealed => false;
        public bool IsValueType => false;
        public string Name => "StubTestClass";
        public IEnumerable<IAttributeInfo> GetCustomAttributes(string name) => [];
        public IEnumerable<ITypeInfo> GetGenericArguments() => [];
        public ITypeInfo GetInterface(string typeName) => null!;
        public IMethodInfo GetMethod(string methodName, bool includePrivate) => null!;
        public IEnumerable<IMethodInfo> GetMethods(bool includePrivate) => [];
    }

    private sealed class StubMethodInfo : LongLivedMarshalByRefObject, IMethodInfo
    {
        public bool IsAbstract => false;
        public bool IsGenericMethodDefinition => false;
        public bool IsPublic => true;
        public bool IsStatic => false;
        public string Name => "StubMethod";
        public ITypeInfo ReturnType => null!;
        public ITypeInfo Type => new StubTypeInfo();
        public IEnumerable<IAttributeInfo> GetCustomAttributes(string name) => [];
        public IEnumerable<ITypeInfo> GetGenericArguments() => [];
        public IEnumerable<IParameterInfo> GetParameters() => [];
        public IMethodInfo MakeGenericMethod(params ITypeInfo[] typeArguments) => this;
    }

    private sealed class StubTestAssembly : LongLivedMarshalByRefObject, ITestAssembly
    {
        public IAssemblyInfo Assembly { get; } = new StubAssemblyInfo();
        public string ConfigFileName => string.Empty;
        public void Serialize(IXunitSerializationInfo data) { }
        public void Deserialize(IXunitSerializationInfo data) { }
    }

    private sealed class StubTestCollection : LongLivedMarshalByRefObject, ITestCollection
    {
        public string DisplayName => "StubCollection";
        public ITypeInfo? Definition => null;
        public ITypeInfo? CollectionDefinition => null;
        public Guid UniqueID { get; } = Guid.NewGuid();
        public ITestAssembly TestAssembly { get; } = new StubTestAssembly();
        public void Serialize(IXunitSerializationInfo data) { }
        public void Deserialize(IXunitSerializationInfo data) { }
    }

    private sealed class StubTestClass : LongLivedMarshalByRefObject, ITestClass
    {
        public ITypeInfo Class { get; } = new StubTypeInfo();
        public ITestCollection TestCollection { get; } = new StubTestCollection();
        public void Serialize(IXunitSerializationInfo data) { }
        public void Deserialize(IXunitSerializationInfo data) { }
    }

    private sealed class StubTestMethod : LongLivedMarshalByRefObject, ITestMethod
    {
        public IMethodInfo Method { get; } = new StubMethodInfo();
        public ITestClass TestClass { get; } = new StubTestClass();
        public void Serialize(IXunitSerializationInfo data) { }
        public void Deserialize(IXunitSerializationInfo data) { }
    }

    private sealed class NoOpMessageBus : IMessageBus
    {
        public bool QueueMessage(IMessageSinkMessage message) => true;
        public void Dispose() { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private RetryTestCase CreateTestCase(int maxRetries) =>
        (RetryTestCase)new RetryFactDiscoverer(new NullMessageSink())
            .Discover(new StubDiscoveryOptions(), new StubTestMethod(), new StubAttributeInfo(maxRetries))
            .Single();

    // ── RetryFactDiscoverer tests ──────────────────────────────────────────────

    [Fact(DisplayName = "RetryFactDiscoverer: Discover returns a single RetryTestCase")]
    public void Discover_ReturnsSingleRetryTestCase()
    {
        var cases = new RetryFactDiscoverer(new NullMessageSink())
            .Discover(new StubDiscoveryOptions(), new StubTestMethod(), new StubAttributeInfo(3))
            .ToList();

        Assert.Single(cases);
        Assert.IsType<RetryTestCase>(cases[0]);
    }

    [Fact(DisplayName = "RetryFactDiscoverer: Discover propagates MaxRetries from attribute to RetryTestCase")]
    public void Discover_PropagatesMaxRetriesToTestCase()
    {
        var testCase = CreateTestCase(7);

        var info = new DictionarySerialiser();
        testCase.Serialize(info);

        Assert.Equal(7, info.GetValue<int>("MaxRetries"));
    }

    // ── RetryTestCase serialisation tests ─────────────────────────────────────

    [Fact(DisplayName = "RetryTestCase: Serialize stores MaxRetries in serialisation data")]
    public void Serialise_StoresMaxRetries()
    {
        var testCase = CreateTestCase(5);
        var info = new DictionarySerialiser();

        testCase.Serialize(info);

        Assert.Equal(5, info.GetValue<int>("MaxRetries"));
    }

    [Fact(DisplayName = "RetryTestCase: Deserialize restores MaxRetries from serialisation data")]
    public void Deserialise_RestoresMaxRetries()
    {
        // Serialise a known value, then deserialise into a fresh instance.
        var original = CreateTestCase(9);
        var info = new DictionarySerialiser();
        original.Serialize(info);

        var restored = CreateTestCase(1);
        restored.Deserialize(info);

        // Re-serialise to observe what Deserialize actually restored.
        var roundTripInfo = new DictionarySerialiser();
        restored.Serialize(roundTripInfo);

        Assert.Equal(9, roundTripInfo.GetValue<int>("MaxRetries"));
    }

    [Fact(DisplayName = "RetryTestCase: zero maxRetries skips loop body and returns failed summary")]
    public async Task RetryTestCase_ZeroRetries_SkipsLoopBody_ReturnsFailed()
    {
        // _maxRetries = 0 → loop condition false immediately → falls through to line 101.
        var testCase = CreateTestCase(0);
        var bus = new NoOpMessageBus();

        var summary = await testCase.RunAsync(
            new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Total);
    }

    [Fact(DisplayName = "RetryTestCase: all retries exhausted flushes buffer and returns failed summary")]
    public async Task RetryTestCase_AllRetriesExhausted_FlushesFailedSummary()
    {
        // Build real xunit metadata for AlwaysFailingHelper.AlwaysFails so that
        // xunit can instantiate the class and invoke the method (which throws).
        var assembly  = Reflector.Wrap(typeof(AlwaysFailingHelper).Assembly);
        var type      = Reflector.Wrap(typeof(AlwaysFailingHelper));
        var method    = Reflector.Wrap(typeof(AlwaysFailingHelper).GetMethod(nameof(AlwaysFailingHelper.AlwaysFails))!);

        var testAssembly = new TestAssembly(assembly);
        var collection   = new TestCollection(testAssembly, null, "AlwaysFailing");
        var testClass    = new TestClass(collection, type);
        var testMethod   = new TestMethod(testClass, method);

        var testCase = new RetryTestCase(
            new NullMessageSink(),
            TestMethodDisplay.Method,
            TestMethodDisplayOptions.None,
            testMethod,
            maxRetries: 1);

        var bus     = new NoOpMessageBus();
        var summary = await testCase.RunAsync(
            new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());

        // attempt 1 fails, attempt == maxRetries → buffer flushed, summary returned.
        Assert.Equal(1, summary.Failed);
    }
}

/// <summary>
/// Public helper used by <see cref="RetryFactDiscovererTests"/> to exercise the exhausted-retries path.
/// Must be public so xunit can instantiate it via <see cref="Activator.CreateInstance"/>.
/// </summary>
public sealed class AlwaysFailingHelper
{
    public void AlwaysFails() => throw new InvalidOperationException("deliberate failure");
}

/// <summary>
/// Functional meta-tests that exercise <see cref="RetryTestCase.RunAsync"/> end-to-end
/// by using <see cref="RetryFactAttribute"/> on real test methods.
/// </summary>
[Trait("Category", "UnitTest")]
public sealed class RetryTestCaseFunctionalTests
{
    // Static counter incremented on each attempt; starts at zero for a fresh run.
    private static int s_retryAttempts;

    /// <summary>
    /// This test fails on the first attempt and passes on the second, exercising the
    /// retry path (Discard) and the eventual success path (Flush) inside RunAsync.
    /// </summary>
    [RetryFact(3)]
    public void RetryFact_FailsOnFirstAttempt_SucceedsOnSecond()
    {
        var attempt = Interlocked.Increment(ref s_retryAttempts);
        Assert.True(attempt >= 2, $"Expected at least 2 attempts, got {attempt}");
    }
}
