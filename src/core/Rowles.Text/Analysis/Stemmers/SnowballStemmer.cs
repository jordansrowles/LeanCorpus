namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Abstract base for Snowball-inspired stemmers that apply ordered suffix-removal
/// steps. Each language stemmer supplies its own suffix lists and may override
/// pre-processing, post-processing, and minimum-length thresholds.
/// </summary>
public abstract class SnowballStemmer : ISpanStemmer
{
    /// <summary>Minimum word length required to attempt stemming.</summary>
    protected virtual int MinWordLength => 3;

    /// <summary>Minimum characters to preserve after suffix removal.</summary>
    protected virtual int MinStemGuard => 3;

    /// <summary>The suffix-removal rules, grouped by step.</summary>
    protected abstract (string Suffix, string Replacement)[][] Steps { get; }

    /// <summary>
    /// Optional pre-processing called before suffix removal.
    /// Copies <paramref name="word"/> into <paramref name="output"/> (possibly
    /// transforming it) and returns the new length. Return -1 to use the default
    /// (untransformed copy).
    /// </summary>
    protected virtual int Preprocess(ReadOnlySpan<char> word, Span<char> output) => -1;

    /// <summary>
    /// Optional post-processing called after all suffix-removal steps.
    /// Returns the adjusted length.
    /// </summary>
    protected virtual int Postprocess(Span<char> buf, int len) => len;

    /// <inheritdoc/>
    public virtual string Stem(string word)
    {
        int maxLen = MaxOutputLength(word);
        Span<char> buf = maxLen <= 64
            ? stackalloc char[maxLen]
            : new char[maxLen];
        int len = Stem(word.AsSpan(), buf);
        return len < 0 ? word : new string(buf[..len]);
    }

    /// <inheritdoc/>
    public virtual int Stem(ReadOnlySpan<char> word, Span<char> output)
    {
        if (word.Length <= MinWordLength)
        {
            if (output.Length < word.Length) return -1;
            word.CopyTo(output);
            return word.Length;
        }

        int len = Preprocess(word, output);
        if (len < 0)
        {
            if (output.Length < word.Length) return -1;
            word.CopyTo(output);
            len = word.Length;
        }

        foreach (var step in Steps)
            len = ApplySuffixRules(output, len, step);

        return Postprocess(output, len);
    }

    /// <summary>Maximum output length needed (overridden by stemmers that expand input).</summary>
    protected virtual int MaxOutputLength(string word) => word.Length;

    private int ApplySuffixRules(
        Span<char> buf, int len,
        (string Suffix, string Replacement)[] rules)
    {
        foreach (var (suffix, replacement) in rules)
        {
            int? result = RemoveSuffix(buf, len, suffix, replacement);
            if (result.HasValue)
                return result.Value;
        }
        return len;
    }

    private int? RemoveSuffix(Span<char> buf, int len, ReadOnlySpan<char> suffix, ReadOnlySpan<char> replacement)
    {
        if (len < suffix.Length + MinStemGuard) return null;
        if (!buf.Slice(len - suffix.Length, suffix.Length).SequenceEqual(suffix)) return null;
        int stemLen = len - suffix.Length;
        if (replacement.Length > 0)
        {
            replacement.CopyTo(buf[stemLen..]);
            return stemLen + replacement.Length;
        }
        return stemLen;
    }
}
