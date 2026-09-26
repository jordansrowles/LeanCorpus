using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Tests;

[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class AnalyserExecutionContextTests
{
    [Fact(DisplayName = "Analyser: concurrent calls route tokens to their own sinks")]
    public async Task ConcurrentCalls_RouteTokensToTheirOwnSinks()
    {
        const int iterations = 100;
        var cancellationToken = TestContext.Current.CancellationToken;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            using var tokeniser = new ConcurrentRoutingTokeniser();
            var analyser = new Analyser(tokeniser, new LowercaseFilter());
            var firstSink = new MaterialisingTokenSink();
            var secondSink = new MaterialisingTokenSink();

            Task first = Task.Run(() => analyser.Analyse("first", firstSink), cancellationToken);
            await tokeniser.FirstCallWaiting.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Task second = Task.Run(() => analyser.Analyse("second", secondSink), cancellationToken);
            await Task.WhenAll(first, second);

            Assert.Equal(["alpha"], firstSink.Tokens.Select(static token => token.Text));
            Assert.Equal(["beta"], secondSink.Tokens.Select(static token => token.Text));
            Assert.Equal((0, 5, 1, 1),
                (firstSink.Tokens[0].StartOffset, firstSink.Tokens[0].EndOffset,
                    firstSink.Tokens[0].PositionIncrement, firstSink.Tokens[0].PositionLength));
            Assert.Equal((0, 4, 1, 1),
                (secondSink.Tokens[0].StartOffset, secondSink.Tokens[0].EndOffset,
                    secondSink.Tokens[0].PositionIncrement, secondSink.Tokens[0].PositionLength));
        }
    }

    [Fact(DisplayName = "Analyser: downstream sink failure does not poison the next analysis")]
    public void DownstreamSinkFailure_NextAnalysisStartsClean()
    {
        var analyser = new Analyser(
            new Tokeniser(),
            new CommonGramsFilter(["the", "quick"]));

        Assert.Throws<InvalidOperationException>(() => analyser.Analyse("the", new ThrowOnFirstAddSink()));

        var nextSink = new MaterialisingTokenSink();
        analyser.Analyse("quick", nextSink);

        AssertToken(nextSink, "quick", 0, 5, 1, 1);
    }

    [Fact(DisplayName = "Analyser: stateful filter failure does not poison the next analysis")]
    public void StatefulFilterFailure_NextAnalysisStartsClean()
    {
        var failure = new ThrowOnce();
        var analyser = new Analyser(new Tokeniser(), new ThrowingStatefulFilter(failure));

        Assert.Throws<InvalidOperationException>(() => analyser.Analyse("poison break", new MaterialisingTokenSink()));

        var nextSink = new MaterialisingTokenSink();
        analyser.Analyse("fresh", nextSink);

        AssertToken(nextSink, "fresh", 0, 5, 1, 1);
    }

    [Fact(DisplayName = "Analyser: execution context preserves token graph metadata")]
    public void ExecutionContext_PreservesGraphMetadata()
    {
        var payload = new byte[] { 7, 11 };
        var analyser = new Analyser(new GraphTokeniser(payload), new LowercaseFilter());
        var sink = new MaterialisingTokenSink();

        analyser.Analyse("Straße".AsSpan(), sink);

        var token = Assert.Single(sink.Tokens);
        Assert.Equal("straße", token.Text);
        Assert.Equal("term", token.Type);
        Assert.Equal(0, token.StartOffset);
        Assert.Equal(6, token.EndOffset);
        Assert.Equal(2, token.PositionIncrement);
        Assert.Equal(3, token.PositionLength);
        Assert.Same(payload, token.Payload);
    }

    [Fact(DisplayName = "Analyser: caching filter publishes completed execution state")]
    public void CachingFilter_PublishesCompletedExecutionState()
    {
        var cache = new CachingTokenFilter();
        Assert.Same(cache, cache.Clone());

        var analyser = new Analyser(new Tokeniser(), cache);
        var firstSink = new MaterialisingTokenSink();
        analyser.Analyse("first analysis", firstSink);

        Assert.Equal(["first", "analysis"], cache.Tokens.Select(static token => token.Text));

        cache.Reset();
        var secondSink = new MaterialisingTokenSink();
        analyser.Analyse("second", secondSink);

        Assert.Equal(["second"], cache.Tokens.Select(static token => token.Text));
    }

    private static void AssertToken(
        MaterialisingTokenSink sink,
        string text,
        int startOffset,
        int endOffset,
        int positionIncrement,
        int positionLength)
    {
        var token = Assert.Single(sink.Tokens);
        Assert.Equal(text, token.Text);
        Assert.Equal(startOffset, token.StartOffset);
        Assert.Equal(endOffset, token.EndOffset);
        Assert.Equal(positionIncrement, token.PositionIncrement);
        Assert.Equal(positionLength, token.PositionLength);
    }

    private sealed class ConcurrentRoutingTokeniser : IShareableSpanTokeniser, IDisposable
    {
        private readonly TaskCompletionSource _firstCallWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ManualResetEventSlim _secondTokenRouted = new();

        public Task FirstCallWaiting => _firstCallWaiting.Task;

        public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            if (input.SequenceEqual("first"))
            {
                _firstCallWaiting.TrySetResult();
                if (!_secondTokenRouted.Wait(TimeSpan.FromSeconds(5)))
                    throw new TimeoutException("The second analysis did not route its token.");

                sink.Add("ALPHA", 0, 5);
                return;
            }

            sink.Add("BETA", 0, 4);
            _secondTokenRouted.Set();
        }

        public void Dispose()
        {
            _secondTokenRouted.Dispose();
        }
    }

    private sealed class ThrowOnFirstAddSink : ISpanTokenSink
    {
        private bool _throw = true;

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            if (_throw)
            {
                _throw = false;
                throw new InvalidOperationException("Injected downstream failure.");
            }
        }

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            int positionLength,
            byte[]? payload)
            => Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    private sealed class ThrowOnce
    {
        private int _remaining = 1;

        public bool TryThrow() => Interlocked.Exchange(ref _remaining, 0) == 1;
    }

    private sealed class ThrowingStatefulFilter(ThrowOnce failure) : ISpanTokenFilter
    {
        private string? _pending;
        private int _pendingStart;
        private int _pendingEnd;
        private string _pendingType = Token.DefaultType;
        private int _pendingPositionIncrement;
        private byte[]? _pendingPayload;

        public void Apply(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type,
            int positionIncrement,
            byte[]? payload,
            ISpanTokenSink sink)
        {
            if (text.SequenceEqual("break") && failure.TryThrow())
                throw new InvalidOperationException("Injected stateful filter failure.");

            if (_pending is not null)
            {
                sink.Add(_pending.AsSpan(), _pendingStart, _pendingEnd, _pendingType,
                    _pendingPositionIncrement, _pendingPayload);
            }

            _pending = text.ToString();
            _pendingStart = startOffset;
            _pendingEnd = endOffset;
            _pendingType = type;
            _pendingPositionIncrement = positionIncrement;
            _pendingPayload = payload;
        }

        public void Finish(ISpanTokenSink sink)
        {
            if (_pending is null)
                return;

            sink.Add(_pending.AsSpan(), _pendingStart, _pendingEnd, _pendingType,
                _pendingPositionIncrement, _pendingPayload);
            _pending = null;
        }

        public ISpanTokenFilter Clone() => new ThrowingStatefulFilter(failure);
    }

    private sealed class GraphTokeniser(byte[] payload) : IShareableSpanTokeniser
    {
        public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
            => sink.Add(input, 0, input.Length, "term", 2, 3, payload);
    }
}
