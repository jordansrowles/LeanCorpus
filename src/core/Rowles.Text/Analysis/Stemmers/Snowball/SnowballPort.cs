namespace Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

/// <summary>The official Snowball 3.2.0 algorithms used by the built-in language stemmers.</summary>
internal enum SnowballAlgorithm
{
    English,
    French,
    German,
    Spanish,
    Italian,
    Portuguese,
    Dutch,
    Russian,
    Arabic,
}

/// <summary>
/// Runs a Snowball algorithm over a span. Each thread owns one runtime instance per algorithm, so the
/// public stemmers that call this hold no mutable state and are safe to share.
/// </summary>
/// <remarks>
/// Stemming is synchronous and the pinned algorithms call no user code, so a thread never observes
/// another thread's state. A re-entrant call on the same thread, which cannot happen through the
/// built-in stemmers, uses a fresh instance rather than corrupting the one in use.
/// </remarks>
internal static class SnowballPort
{
    private static readonly int AlgorithmCount = Enum.GetValues<SnowballAlgorithm>().Length;

    [ThreadStatic] private static SnowballRuntime?[]? t_instances;
    [ThreadStatic] private static bool t_inUse;

    /// <summary>
    /// Stems <paramref name="word"/> into <paramref name="output"/>. Returns the stem's length, or -1
    /// without writing when <paramref name="output"/> is too small.
    /// </summary>
    public static int Stem(SnowballAlgorithm algorithm, ReadOnlySpan<char> word, Span<char> output)
    {
        var runtime = Acquire(algorithm, out bool shared);
        try
        {
            runtime.Load(word);
            runtime.Run();
            var result = runtime.Result;
            if (result.Length > output.Length)
                return -1;
            result.CopyTo(output);
            return result.Length;
        }
        finally
        {
            if (shared)
                t_inUse = false;
        }
    }

    /// <summary>Stems <paramref name="word"/>, returning it unchanged (the same instance) when the stem is identical.</summary>
    public static string Stem(SnowballAlgorithm algorithm, string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var runtime = Acquire(algorithm, out bool shared);
        try
        {
            runtime.Load(word);
            runtime.Run();
            var result = runtime.Result;
            return result.SequenceEqual(word) ? word : new string(result);
        }
        finally
        {
            if (shared)
                t_inUse = false;
        }
    }

    private static SnowballRuntime Acquire(SnowballAlgorithm algorithm, out bool shared)
    {
        if (t_inUse)
        {
            shared = false;
            return Create(algorithm);
        }

        var instances = t_instances ??= new SnowballRuntime?[AlgorithmCount];
        var runtime = instances[(int)algorithm] ??= Create(algorithm);
        t_inUse = true;
        shared = true;
        return runtime;
    }

    internal static SnowballRuntime Create(SnowballAlgorithm algorithm) => algorithm switch
    {
        SnowballAlgorithm.English => new EnglishSnowballAlgorithm(),
        SnowballAlgorithm.French => new FrenchSnowballAlgorithm(),
        SnowballAlgorithm.German => new GermanSnowballAlgorithm(),
        SnowballAlgorithm.Spanish => new SpanishSnowballAlgorithm(),
        SnowballAlgorithm.Italian => new ItalianSnowballAlgorithm(),
        SnowballAlgorithm.Portuguese => new PortugueseSnowballAlgorithm(),
        SnowballAlgorithm.Dutch => new DutchSnowballAlgorithm(),
        SnowballAlgorithm.Russian => new RussianSnowballAlgorithm(),
        SnowballAlgorithm.Arabic => new ArabicSnowballAlgorithm(),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown Snowball algorithm."),
    };
}
