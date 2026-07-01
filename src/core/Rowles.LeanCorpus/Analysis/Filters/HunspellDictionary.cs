using System.Collections.Concurrent;
using System.Collections.Frozen;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Lightweight Hunspell dictionary that supports short-form flags and simple prefix and suffix rules.
/// </summary>
public sealed class HunspellDictionary
{
    private const int DefaultMaxGeneratedFormsPerEntry = 4096;
    private readonly FrozenDictionary<string, string[]> _surfaceToStems;

    /// <summary>
    /// Thread-safe cache of parsed dictionaries keyed by content hash.
    /// Subsequent calls with the same AFF and DIC text return the cached instance.
    /// </summary>
    private static readonly ConcurrentDictionary<string, HunspellDictionary> Cache = new(StringComparer.Ordinal);

    private HunspellDictionary(Dictionary<string, HashSet<string>> stems)
    {
        _surfaceToStems = stems.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses Hunspell affix and dictionary content from files on disk.
    /// Results are cached by content hash for subsequent calls.
    /// </summary>
    public static HunspellDictionary FromFile(string affixPath, string dictionaryPath,
        int maxGeneratedFormsPerEntry = DefaultMaxGeneratedFormsPerEntry)
    {
        var affixText = FileOpenRetry.ReadAllText(affixPath);
        var dictionaryText = FileOpenRetry.ReadAllText(dictionaryPath);
        return ParseCached(affixText, dictionaryText, maxGeneratedFormsPerEntry);
    }

    /// <summary>
    /// Parses Hunspell affix and dictionary content from streams.
    /// The streams are read to completion but not disposed.
    /// Results are cached by content hash for subsequent calls.
    /// </summary>
    public static HunspellDictionary FromStream(Stream affixStream, Stream dictionaryStream,
        int maxGeneratedFormsPerEntry = DefaultMaxGeneratedFormsPerEntry)
    {
        using var affReader = new StreamReader(affixStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var dicReader = new StreamReader(dictionaryStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        return ParseCached(affReader.ReadToEnd(), dicReader.ReadToEnd(), maxGeneratedFormsPerEntry);
    }

    /// <summary>
    /// Parses Hunspell affix and dictionary content from strings.
    /// Results are cached by content hash for subsequent calls.
    /// </summary>
    public static HunspellDictionary Parse(
        string affixText,
        string dictionaryText,
        int maxGeneratedFormsPerEntry = DefaultMaxGeneratedFormsPerEntry)
    {
        return ParseCached(affixText, dictionaryText, maxGeneratedFormsPerEntry);
    }

    private static HunspellDictionary ParseCached(
        string affixText, string dictionaryText, int maxGeneratedFormsPerEntry)
    {
        // Compute a content hash for cache key
        var key = $"{affixText.GetHashCode(StringComparison.Ordinal):X8}:{dictionaryText.GetHashCode(StringComparison.Ordinal):X8}:{maxGeneratedFormsPerEntry}";

        return Cache.GetOrAdd(key, _ => ParseInternal(affixText, dictionaryText, maxGeneratedFormsPerEntry));
    }

    private static HunspellDictionary ParseInternal(
        string affixText,
        string dictionaryText,
        int maxGeneratedFormsPerEntry)
    {
        ArgumentNullException.ThrowIfNull(affixText);
        ArgumentNullException.ThrowIfNull(dictionaryText);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGeneratedFormsPerEntry, 1);

        var (rules, aliases) = ParseAffixes(affixText);
        var stems = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Collect morphological tags if present
        var morphology = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ParseEntries(dictionaryText, aliases))
        {
            int generatedForms = 0;
            AddStem(stems, entry.Word, entry.Word);

            // Store morphological data
            if (entry.MorphologicalTags.Count > 0)
                morphology[entry.Word] = entry.MorphologicalTags;

            var entryFlags = entry.Flags;
            var prefixes = entryFlags
                .Where(flag => rules.TryGetValue((flag, AffixKind.Prefix), out _))
                .SelectMany(flag => rules[(flag, AffixKind.Prefix)])
                .ToArray();
            var suffixes = entryFlags
                .Where(flag => rules.TryGetValue((flag, AffixKind.Suffix), out _))
                .SelectMany(flag => rules[(flag, AffixKind.Suffix)])
                .ToArray();

            foreach (var prefix in prefixes)
                AddGeneratedForms(stems, entry.Word, entry.Word, prefix, suffix: null, maxGeneratedFormsPerEntry, ref generatedForms);

            foreach (var suffix in suffixes)
                AddGeneratedForms(stems, entry.Word, entry.Word, prefix: null, suffix, maxGeneratedFormsPerEntry, ref generatedForms);

            foreach (var prefix in prefixes.Where(static rule => rule.CrossProduct))
            foreach (var suffix in suffixes.Where(static rule => rule.CrossProduct))
                AddGeneratedForms(stems, entry.Word, entry.Word, prefix, suffix, maxGeneratedFormsPerEntry, ref generatedForms);
        }

        return new HunspellDictionary(stems);
    }

    /// <summary>
    /// Returns known stems for the supplied surface form.
    /// </summary>
    public IReadOnlyList<string> Stem(string surfaceForm)
        => _surfaceToStems.TryGetValue(surfaceForm, out var stems)
            ? stems
            : Array.Empty<string>();

    private static (Dictionary<(char Flag, AffixKind Kind), List<AffixRule>> Rules, Dictionary<int, string> Aliases) ParseAffixes(string affixText)
    {
        var rules = new Dictionary<(char Flag, AffixKind Kind), List<AffixRule>>();
        var aliases = new Dictionary<int, string>(); // AF alias: index → flag string
        var lines = affixText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            // Parse aliases: AF flag1 flag2 ...
            if (parts[0].Equals("AF", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
            {
                if (int.TryParse(parts[1], out var aliasIndex))
                    aliases[aliasIndex] = parts[2];
                continue;
            }

            if (parts[0].Equals("SET", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length > 1 && !parts[1].Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Only UTF-8 Hunspell dictionaries are supported.");
                continue;
            }

            if (parts[0].Equals("FLAG", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length > 1 && !parts[1].Equals("short", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("Only short-form Hunspell flags are supported.");
                continue;
            }

            if (parts[0].Equals("COMPLEXPREFIXES", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("COMPLEXPREFIXES is not supported.");

            if (!parts[0].Equals("PFX", StringComparison.OrdinalIgnoreCase) &&
                !parts[0].Equals("SFX", StringComparison.OrdinalIgnoreCase))
                continue;

            if (parts.Length == 4)
            {
                char flag = parts[1][0];
                bool crossProduct = parts[2].Equals("Y", StringComparison.OrdinalIgnoreCase);
                int count = int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
                var kind = parts[0].Equals("PFX", StringComparison.OrdinalIgnoreCase) ? AffixKind.Prefix : AffixKind.Suffix;
                var expectedDirective = kind == AffixKind.Prefix ? "PFX" : "SFX";

                for (int ruleIndex = 0; ruleIndex < count; ruleIndex++)
                {
                    i++;
                    if (i >= lines.Length)
                        throw new InvalidDataException("Unexpected end of Hunspell AFF rules.");

                    var ruleParts = lines[i].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (ruleParts.Length < 5)
                        throw new InvalidDataException($"Invalid Hunspell AFF rule: '{lines[i]}'.");
                    if (!ruleParts[0].Equals(expectedDirective, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"Invalid Hunspell AFF rule: expected {expectedDirective} rule for flag '{flag}', got '{lines[i]}'.");
                    if (ruleParts[1].Length == 0 || ruleParts[1][0] != flag)
                        throw new InvalidDataException(
                            $"Invalid Hunspell AFF rule: expected flag '{flag}', got '{lines[i]}'.");

                    var rule = new AffixRule(
                        flag,
                        kind,
                        Strip: ruleParts[2] == "0" ? string.Empty : ruleParts[2],
                        Append: ruleParts[3] == "0" ? string.Empty : ruleParts[3],
                        Condition: ruleParts[4],
                        crossProduct);

                    if (!rules.TryGetValue((flag, kind), out var bucket))
                    {
                        bucket = [];
                        rules[(flag, kind)] = bucket;
                    }

                    bucket.Add(rule);
                }
            }
        }

        // Resolve AF aliases in parsed entries (handled in ParseEntries)
        return (rules, aliases);
    }

    private static IEnumerable<DictionaryEntry> ParseEntries(string dictionaryText, Dictionary<int, string> aliases)
    {
        var lines = dictionaryText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        int start = lines.Length > 0 && int.TryParse(lines[0], out _) ? 1 : 0;

        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            int slash = line.IndexOf('/');
            if (slash < 0)
            {
                yield return new DictionaryEntry(line, [], []);
                continue;
            }

            var word = line[..slash];
            var afterFlags = line[(slash + 1)..];

            // Split flags from morphological tags
            // Flags are contiguous word characters; morphology starts after a space or tab
            int morphStart = afterFlags.IndexOfAny([' ', '\t']);
            string flagPart;
            List<string> morphologicalTags;

            if (morphStart > 0)
            {
                flagPart = afterFlags[..morphStart];
                morphologicalTags = afterFlags[(morphStart + 1)..]
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }
            else
            {
                flagPart = afterFlags;
                morphologicalTags = [];
            }

            // Resolve AF alias references (numeric indices)
            var flags = new List<char>();
            foreach (var part in flagPart.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part, out var aliasIndex) && aliases.TryGetValue(aliasIndex, out var aliasFlags))
                {
                    flags.AddRange(aliasFlags.ToCharArray());
                    continue;
                }

                flags.AddRange(part.ToCharArray());
            }

            yield return new DictionaryEntry(word, flags.ToArray(), morphologicalTags);
        }
    }

    private static void AddGeneratedForms(
        Dictionary<string, HashSet<string>> stems,
        string root,
        string baseWord,
        AffixRule? prefix,
        AffixRule? suffix,
        int maxGeneratedFormsPerEntry,
        ref int generatedForms)
    {
        string candidate = baseWord;

        if (prefix is not null)
        {
            if (!prefix.Matches(baseWord))
                return;
            candidate = prefix.Apply(candidate);
        }

        if (suffix is not null)
        {
            if (!suffix.Matches(candidate))
                return;
            candidate = suffix.Apply(candidate);
        }

        generatedForms++;
        if (generatedForms > maxGeneratedFormsPerEntry)
        {
            throw new InvalidDataException(
                $"Hunspell dictionary entry '{root}' generated more than {maxGeneratedFormsPerEntry} surface forms.");
        }

        AddStem(stems, candidate, root);
    }

    private static void AddStem(Dictionary<string, HashSet<string>> stems, string surfaceForm, string stem)
    {
        if (!stems.TryGetValue(surfaceForm, out var bucket))
        {
            bucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            stems[surfaceForm] = bucket;
        }

        bucket.Add(stem);
    }

    private sealed record DictionaryEntry(string Word, char[] Flags, List<string> MorphologicalTags);

    private enum AffixKind : byte
    {
        Prefix,
        Suffix
    }

    private sealed record AffixRule(char Flag, AffixKind Kind, string Strip, string Append, string Condition, bool CrossProduct)
    {
        public bool Matches(string baseWord)
        {
            if (Kind == AffixKind.Prefix)
            {
                if (!string.IsNullOrEmpty(Strip) && !baseWord.StartsWith(Strip, StringComparison.OrdinalIgnoreCase))
                    return false;
                return MatchesCondition(baseWord, fromStart: true);
            }

            if (!string.IsNullOrEmpty(Strip) && !baseWord.EndsWith(Strip, StringComparison.OrdinalIgnoreCase))
                return false;
            return MatchesCondition(baseWord, fromStart: false);
        }

        public string Apply(string baseWord)
        {
            if (Strip.Length > baseWord.Length)
                return baseWord;

            return Kind switch
            {
                AffixKind.Prefix => string.Concat(Append, Strip.Length == 0 ? baseWord : baseWord[Strip.Length..]),
                AffixKind.Suffix => string.Concat(
                    Strip.Length == 0 ? baseWord : baseWord[..^Strip.Length],
                    Append),
                _ => baseWord
            };
        }

        private bool MatchesCondition(string baseWord, bool fromStart)
        {
            if (Condition == "." || string.IsNullOrEmpty(Condition))
                return true;

            if (Condition.Length > 128)
                throw new NotSupportedException("Hunspell conditions longer than 128 pattern elements are not supported.");

            var elements = new ConditionElement[Condition.Length];
            int count = ParseCondition(Condition, elements);
            if (baseWord.Length < count)
                return false;

            int offset = fromStart ? 0 : baseWord.Length - count;
            for (int i = 0; i < count; i++)
            {
                if (!elements[i].Matches(baseWord[offset + i]))
                    return false;
            }

            return true;
        }

        private static int ParseCondition(string condition, ConditionElement[] elements)
        {
            int count = 0;
            for (int i = 0; i < condition.Length; i++)
            {
                char c = condition[i];
                if (c == '.')
                {
                    elements[count++] = ConditionElement.Any;
                    continue;
                }

                if (c != '[')
                {
                    elements[count++] = ConditionElement.Literal(c);
                    continue;
                }

                int end = condition.IndexOf(']', i + 1);
                if (end < 0)
                    throw new InvalidDataException($"Invalid Hunspell condition '{condition}'.");

                bool negated = i + 1 < end && condition[i + 1] == '^';
                int setStart = negated ? i + 2 : i + 1;
                if (setStart >= end)
                    throw new InvalidDataException($"Invalid Hunspell condition '{condition}'.");

                // Check for character range patterns like [a-z], [A-Z], [0-9]
                var setContent = condition[setStart..end];
                if (setContent.Length == 3 && setContent[1] == '-')
                {
                    // Expand the range into individual characters
                    var expandedSet = ExpandCharacterRange(setContent[0], setContent[2]);
                    elements[count++] = ConditionElement.Set(expandedSet, negated);
                }
                else
                {
                    elements[count++] = ConditionElement.Set(setContent, negated);
                }

                i = end;
            }

            return count;
        }

        private static string ExpandCharacterRange(char start, char end)
        {
            start = char.ToUpperInvariant(start);
            end = char.ToUpperInvariant(end);

            if (end < start)
                (start, end) = (end, start);

            var sb = new System.Text.StringBuilder(end - start + 1);
            for (char ch = start; ch <= end; ch++)
                sb.Append(ch);
            return sb.ToString();
        }
    }

    private readonly struct ConditionElement
    {
        private readonly string? _set;
        private readonly char _literal;
        private readonly byte _kind;

        private ConditionElement(byte kind, char literal, string? set, bool negated)
        {
            _kind = kind;
            _literal = literal;
            _set = set;
            Negated = negated;
        }

        public bool Negated { get; }

        public static ConditionElement Any => new(kind: 0, literal: '\0', set: null, negated: false);

        public static ConditionElement Literal(char literal) => new(kind: 1, char.ToUpperInvariant(literal), set: null, negated: false);

        public static ConditionElement Set(string set, bool negated) => new(kind: 2, literal: '\0', set: set.ToUpperInvariant(), negated);

        public bool Matches(char value)
        {
            if (_kind == 0)
                return true;
            char normalised = char.ToUpperInvariant(value);
            if (_kind == 1)
                return normalised == _literal;

            bool contains = _set!.IndexOf(normalised, StringComparison.Ordinal) >= 0;
            return Negated ? !contains : contains;
        }
    }
}
