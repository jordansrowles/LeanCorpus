namespace Rowles.LeanCorpus.Analysis.Stemmers;

/// <summary>
/// Abstract base for Snowball-inspired stemmers that apply ordered suffix-removal
/// steps. Each language stemmer supplies its own suffix lists and may override
/// pre-processing, post-processing, and minimum-length thresholds.
/// </summary>
public abstract class SnowballStemmer : IShareableSpanStemmer
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

        var steps = Steps;
        var index = GetRuleIndex(steps);
        for (int step = 0; step < steps.Length; step++)
            len = ApplySuffixRules(output, len, steps[step], index.Steps[step]);

        return Postprocess(output, len);
    }

    /// <summary>Maximum output length needed (overridden by stemmers that expand input).</summary>
    protected virtual int MaxOutputLength(string word) => word.Length;

    private RuleIndex GetRuleIndex((string Suffix, string Replacement)[][] steps)
    {
        // Built once per Steps array. A subclass that returns a different array gets a new
        // index; built-in stemmers return one immutable array per instance.
        var index = Volatile.Read(ref _ruleIndex);
        if (index is null || !ReferenceEquals(index.Source, steps))
        {
            index = RuleIndex.Build(steps);
            Volatile.Write(ref _ruleIndex, index);
        }
        return index;
    }

    private int ApplySuffixRules(
        Span<char> buf, int len,
        (string Suffix, string Replacement)[] rules,
        StepIndex index)
    {
        // Only rules whose suffix ends with the word's final character (or is empty) can
        // match. They are visited in their declared order, so the first applicable rule
        // is the same one a full scan would find.
        foreach (int rule in index.Candidates(len == 0 ? null : buf[len - 1]))
        {
            var (suffix, replacement) = rules[rule];
            int? result = RemoveSuffix(buf, len, suffix, replacement);
            if (result.HasValue)
                return result.Value;
        }
        return len;
    }

    private RuleIndex? _ruleIndex;

    private sealed class RuleIndex
    {
        private RuleIndex((string Suffix, string Replacement)[][] source, StepIndex[] steps)
        {
            Source = source;
            Steps = steps;
        }

        public (string Suffix, string Replacement)[][] Source { get; }

        public StepIndex[] Steps { get; }

        public static RuleIndex Build((string Suffix, string Replacement)[][] source)
        {
            var steps = new StepIndex[source.Length];
            for (int step = 0; step < source.Length; step++)
                steps[step] = StepIndex.Build(source[step]);
            return new RuleIndex(source, steps);
        }
    }

    private sealed class StepIndex
    {
        private const int MaxTableRange = 1024;

        private readonly int[] _emptySuffixRules;
        private readonly char _first;
        private readonly int[][]? _table;
        private readonly Dictionary<char, int[]>? _byFinal;

        private StepIndex(int[] emptySuffixRules, char first, int[][]? table, Dictionary<char, int[]>? byFinal)
        {
            _emptySuffixRules = emptySuffixRules;
            _first = first;
            _table = table;
            _byFinal = byFinal;
        }

        public ReadOnlySpan<int> Candidates(char? final)
        {
            if (final is not char c)
                return _emptySuffixRules;
            if (_table is not null)
            {
                uint offset = (uint)(c - _first);
                return offset < (uint)_table.Length ? _table[offset] : _emptySuffixRules;
            }
            return _byFinal!.TryGetValue(c, out var rules) ? rules : _emptySuffixRules;
        }

        public static StepIndex Build((string Suffix, string Replacement)[] rules)
        {
            var emptySuffixRules = new List<int>();
            var byFinal = new Dictionary<char, List<int>>();
            for (int rule = 0; rule < rules.Length; rule++)
            {
                string suffix = rules[rule].Suffix ?? string.Empty;
                if (suffix.Length == 0)
                {
                    // An empty suffix matches every word, so it joins every bucket in order.
                    emptySuffixRules.Add(rule);
                    foreach (var bucket in byFinal.Values)
                        bucket.Add(rule);
                    continue;
                }

                char final = suffix[^1];
                if (!byFinal.TryGetValue(final, out var list))
                {
                    list = new List<int>(emptySuffixRules);
                    byFinal[final] = list;
                }
                list.Add(rule);
            }

            int[] empty = emptySuffixRules.ToArray();
            if (byFinal.Count == 0)
                return new StepIndex(empty, '\0', [], null);

            char first = byFinal.Keys.Min();
            char last = byFinal.Keys.Max();
            if (last - first < MaxTableRange)
            {
                var table = new int[last - first + 1][];
                Array.Fill(table, empty);
                foreach (var (final, list) in byFinal)
                    table[final - first] = list.ToArray();
                return new StepIndex(empty, first, table, null);
            }

            return new StepIndex(empty, '\0', null, byFinal.ToDictionary(static pair => pair.Key, static pair => pair.Value.ToArray()));
        }
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
