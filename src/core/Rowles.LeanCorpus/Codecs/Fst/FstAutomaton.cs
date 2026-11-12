using System.Text;
using Rowles.LeanCorpus.Codecs.TermDictionary;
namespace Rowles.LeanCorpus.Codecs.Fst;

/// <summary>
/// Interface for deterministic finite automata used with term dictionary intersection.
/// States are represented as integers. State -1 is the dead/reject state.
/// </summary>
public interface IAutomaton
{
    /// <summary>Initial state of the automaton.</summary>
    int Start { get; }

    /// <summary>Transition function: returns next state given current state and input byte, or -1 if no transition.</summary>
    int Step(int state, byte input);

    /// <summary>Returns true if the given state is an accepting state.</summary>
    bool IsAccept(int state);

    /// <summary>
    /// Returns true if the automaton could potentially accept any string
    /// starting from the given state. Used for early pruning during intersection.
    /// Returns false only if the state is dead (no path to any accept state).
    /// </summary>
    bool CanMatch(int state);

    /// <summary>
    /// Returns true if the given state accepts every possible byte sequence
    /// (i.e. all 256 transitions lead to an accept state or a self-sink).
    /// Used by the FST reader to skip automaton calls and switch to
    /// a simple output-collection traversal when the automaton is fully permissive.
    /// </summary>
    bool IsSink(int state) => false;
}

/// <summary>
/// Accepts any byte sequence that starts with a given UTF-8 prefix.
/// States: 0..prefix.Length-1 = matching prefix bytes, prefix.Length = accepted (sink).
/// </summary>
public sealed class PrefixAutomaton : IAutomaton
{
    private readonly byte[] _prefix;

    /// <summary>
    /// Initialises a new <see cref="PrefixAutomaton"/> from a string prefix, converted to UTF-8.
    /// </summary>
    /// <param name="prefix">The string prefix to match.</param>
    public PrefixAutomaton(string prefix)
    {
        _prefix = Encoding.UTF8.GetBytes(prefix);
    }

    /// <summary>
    /// Initialises a new <see cref="PrefixAutomaton"/> from a raw UTF-8 byte prefix.
    /// </summary>
    /// <param name="prefixUtf8">The UTF-8 byte prefix to match.</param>
    public PrefixAutomaton(ReadOnlySpan<byte> prefixUtf8)
    {
        _prefix = prefixUtf8.ToArray();
    }

    /// <inheritdoc/>
    public int Start => 0;

    /// <inheritdoc/>
    public int Step(int state, byte input)
    {
        if (state < 0) return -1;
        if (state >= _prefix.Length)
            return state; // Already past prefix — accept everything
        return input == _prefix[state] ? state + 1 : -1;
    }

    /// <inheritdoc/>
    public bool IsAccept(int state) => state >= _prefix.Length;

    /// <inheritdoc/>
    public bool CanMatch(int state) => state >= 0;
}

/// <summary>
/// DFA for wildcard patterns with '*' (any sequence) and '?' (any single byte).
/// Built via NFA-to-DFA subset construction at construction time.
/// Operates on individual UTF-8 bytes (not characters).
/// </summary>
public sealed class WildcardAutomaton : IAutomaton
{
    // DFA state transitions: _transitions[state * 256 + byte] = next state
    private readonly int[] _transitions;
    private readonly bool[] _accept;
    private readonly bool[] _canMatch;
    private readonly int _stateCount;

    /// <summary>
    /// Initialises a new <see cref="WildcardAutomaton"/> for the given wildcard pattern.
    /// Constructs the DFA via NFA-to-DFA subset construction at construction time.
    /// </summary>
    /// <param name="pattern">The wildcard pattern. Use '*' for any sequence of bytes and '?' for any single byte.</param>
    public WildcardAutomaton(string pattern)
    {
        // Convert pattern to UTF-8 bytes, treating '*' and '?' specially
        var patternBytes = new List<byte>();
        var isMeta = new List<bool>();
        Span<byte> buf = stackalloc byte[4];
        Span<char> charBuf = stackalloc char[1];
        foreach (char c in pattern)
        {
            if (c == '*' || c == '?')
            {
                patternBytes.Add((byte)c);
                isMeta.Add(true);
            }
            else
            {
                charBuf[0] = c;
                int len = Encoding.UTF8.GetBytes(charBuf, buf);
                for (int i = 0; i < len; i++)
                {
                    patternBytes.Add(buf[i]);
                    isMeta.Add(false);
                }
            }
        }

        int patLen = patternBytes.Count;
        byte[] pat = patternBytes.ToArray();
        bool[] meta = isMeta.ToArray();

        // Build NFA states: one per pattern position + one accept state
        // NFA state i means "need to match pat[i..]"
        // Use subset construction for DFA

        // For efficiency, we use a simplified approach:
        // Track sets of NFA states and build DFA on the fly
        var nfaStates = new Dictionary<long, int>(); // hash of NFA state set → DFA state
        var dfaTransitions = new List<int[]>(); // DFA state → 256 transitions
        var dfaAccept = new List<bool>();

        int dfaStateId = 0;

        // Epsilon closure: expand '*' states (a '*' at position i can match 0 chars → skip to i+1)
        HashSet<int> EpsilonClosure(HashSet<int> states)
        {
            var result = new HashSet<int>(states);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (int s in result.ToArray())
                {
                    if (s < patLen && meta[s] && pat[s] == (byte)'*')
                    {
                        // '*' can match zero → epsilon to s+1
                        if (result.Add(s + 1))
                            changed = true;
                    }
                }
            }
            return result;
        }

        var initialSet = EpsilonClosure([0]);
        var queue = new Queue<HashSet<int>>();
        var setToId = new Dictionary<string, int>();

        string SetKey(HashSet<int> set)
        {
            var sorted = set.Order().ToArray();
            return string.Join(",", sorted);
        }

        string initKey = SetKey(initialSet);
        setToId[initKey] = dfaStateId++;
        dfaTransitions.Add(new int[256]);
        Array.Fill(dfaTransitions[0], -1);
        dfaAccept.Add(initialSet.Contains(patLen));
        queue.Enqueue(initialSet);

        while (queue.Count > 0)
        {
            var currentSet = queue.Dequeue();
            string currentKey = SetKey(currentSet);
            int currentDfa = setToId[currentKey];

            for (int b = 0; b < 256; b++)
            {
                var nextSet = new HashSet<int>();
                foreach (int s in currentSet)
                {
                    if (s >= patLen) continue;

                    if (meta[s] && pat[s] == (byte)'*')
                    {
                        // '*' matches this byte → stay at s (greedy)
                        nextSet.Add(s);
                        // '*' also allows skipping → s+1 already in closure
                        nextSet.Add(s + 1);
                    }
                    else if (meta[s] && pat[s] == (byte)'?')
                    {
                        // '?' matches any single byte
                        nextSet.Add(s + 1);
                    }
                    else if (pat[s] == (byte)b)
                    {
                        // Literal match
                        nextSet.Add(s + 1);
                    }
                }

                if (nextSet.Count == 0)
                {
                    dfaTransitions[currentDfa][b] = -1;
                    continue;
                }

                nextSet = EpsilonClosure(nextSet);
                string nextKey = SetKey(nextSet);

                if (!setToId.TryGetValue(nextKey, out int nextDfa))
                {
                    nextDfa = dfaStateId++;
                    setToId[nextKey] = nextDfa;
                    dfaTransitions.Add(new int[256]);
                    Array.Fill(dfaTransitions[^1], -1);
                    dfaAccept.Add(nextSet.Contains(patLen));
                    queue.Enqueue(nextSet);
                }

                dfaTransitions[currentDfa][b] = nextDfa;
            }
        }

        _stateCount = dfaStateId;
        _transitions = new int[_stateCount * 256];
        _accept = dfaAccept.ToArray();
        _canMatch = new bool[_stateCount];

        for (int s = 0; s < _stateCount; s++)
        {
            Array.Copy(dfaTransitions[s], 0, _transitions, s * 256, 256);
        }

        // Compute canMatch via backward reachability from accept states
        ComputeCanMatch();
    }

    private void ComputeCanMatch()
    {
        // Start with accept states
        for (int s = 0; s < _stateCount; s++)
            _canMatch[s] = _accept[s];

        // Fixed-point: if any transition from s leads to a canMatch state, s canMatch
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int s = 0; s < _stateCount; s++)
            {
                if (_canMatch[s]) continue;
                for (int b = 0; b < 256; b++)
                {
                    int next = _transitions[s * 256 + b];
                    if (next >= 0 && _canMatch[next])
                    {
                        _canMatch[s] = true;
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public int Start => 0;

    /// <inheritdoc/>
    public int Step(int state, byte input)
    {
        if (state < 0 || state >= _stateCount) return -1;
        return _transitions[state * 256 + input];
    }

    /// <inheritdoc/>
    public bool IsAccept(int state) => state >= 0 && state < _stateCount && _accept[state];

    /// <inheritdoc/>
    public bool CanMatch(int state) => state >= 0 && state < _stateCount && _canMatch[state];

    /// <inheritdoc/>
    public bool IsSink(int state)
    {
        if (state < 0 || state >= _stateCount)
            return false;

        // A sink state transitions to itself on every byte and is accepting.
        if (!_accept[state]) return false;

        int baseOffset = state * 256;
        for (int b = 0; b < 256; b++)
        {
            int next = _transitions[baseOffset + b];
            if (next != state && next >= 0 && !_accept[next])
                return false;
        }

        return true;
    }
}

/// <summary>
/// Levenshtein DFA accepting strings within edit distance <c>maxEdits</c>
/// of a reference term. Built via NFA-to-DFA subset construction at construction time
/// to correctly handle deletions (ε-transitions that advance position without consuming input).
/// Operates on UTF-8 bytes.
/// </summary>
public sealed class LevenshteinAutomaton : IAutomaton
{
    private readonly int[] _transitions;
    private readonly bool[] _accept;
    private readonly bool[] _canMatch;
    private readonly int[] _minDistance;
    private readonly int _stateCount;
    private readonly int _maxEdits;
    private readonly bool _usesCompactCompiler;
    private readonly int[]? _compactDefaultTransitions;
    private readonly int[]? _compactExceptionOffsets;
    private readonly byte[]? _compactExceptionBytes;
    private readonly int[]? _compactExceptionTargets;

    private sealed class StateSetComparer : IEqualityComparer<ulong[]>
    {
        internal static readonly StateSetComparer Instance = new();

        public bool Equals(ulong[]? first, ulong[]? second)
            => ReferenceEquals(first, second)
                || (first is not null && second is not null && first.AsSpan().SequenceEqual(second));

        public int GetHashCode(ulong[] states)
        {
            var hash = new HashCode();
            foreach (ulong word in states)
                hash.Add(word);
            return hash.ToHashCode();
        }
    }

    private readonly record struct CompactAutomatonData(
        int[] DefaultTransitions,
        int[] ExceptionOffsets,
        byte[] ExceptionBytes,
        int[] ExceptionTargets,
        bool[] Accept,
        bool[] CanMatch,
        int[] MinDistance);

    /// <summary>
    /// Initialises a new <see cref="LevenshteinAutomaton"/> for the given term and maximum edit distance.
    /// </summary>
    /// <param name="term">The reference term string.</param>
    /// <param name="maxEdits">The maximum allowed Levenshtein distance (typically 1 or 2).</param>
    public LevenshteinAutomaton(string term, int maxEdits)
        : this(Encoding.UTF8.GetBytes(term), maxEdits) { }

    /// <summary>
    /// Initialises a new <see cref="LevenshteinAutomaton"/> from a raw UTF-8 byte representation of the term.
    /// </summary>
    /// <param name="termUtf8">The UTF-8 bytes of the reference term.</param>
    /// <param name="maxEdits">The maximum allowed Levenshtein distance (typically 1 or 2).</param>
    public LevenshteinAutomaton(ReadOnlySpan<byte> termUtf8, int maxEdits)
    {
        _maxEdits = maxEdits;
        byte[] term = termUtf8.ToArray();
        int termLen = term.Length;

        long compactNfaStateCount = ((long)termLen + 1) * ((long)maxEdits + 1);
        if (maxEdits >= 0 && compactNfaStateCount <= 64)
        {
            var compact = CompileCompact(term, maxEdits, (int)compactNfaStateCount);
            _usesCompactCompiler = true;
            _transitions = [];
            _accept = compact.Accept;
            _canMatch = compact.CanMatch;
            _minDistance = compact.MinDistance;
            _stateCount = compact.Accept.Length;
            _compactDefaultTransitions = compact.DefaultTransitions;
            _compactExceptionOffsets = compact.ExceptionOffsets;
            _compactExceptionBytes = compact.ExceptionBytes;
            _compactExceptionTargets = compact.ExceptionTargets;
            return;
        }

        _usesCompactCompiler = false;
        int stateWidth = maxEdits + 1;
        int nfaStateCount = (termLen + 1) * stateWidth;

        // NFA state: (position, editsUsed) encoded as position * stateWidth + edits.
        // Only discovered DFA sets are retained. Every transition reuses scratch;
        // dictionary keys are owned snapshots and are never mutated after insertion.
        int wordCount = ((nfaStateCount - 1) >> 6) + 1;

        static void AddState(ulong[] states, int state)
            => states[state >> 6] |= 1UL << (state & 63);

        void EpsilonClosure(ulong[] states)
        {
            // Deletions increase the encoded state. Visit only set bits, including
            // newly added bits in this word; later words see their additions on entry.
            for (int word = 0; word < states.Length; word++)
            {
                ulong remaining = states[word];
                while (remaining != 0)
                {
                    int state = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(remaining);
                    remaining &= remaining - 1;
                    if (state / stateWidth >= termLen || state % stateWidth >= maxEdits)
                        continue;
                    int deletion = state + stateWidth + 1;
                    int targetWord = deletion >> 6;
                    ulong mask = 1UL << (deletion & 63);
                    if ((states[targetWord] & mask) != 0)
                        continue;
                    states[targetWord] |= mask;
                    if (targetWord == word)
                        remaining |= mask;
                }
            }
        }

        bool NfaStep(ulong[] states, byte input, ulong[] next)
        {
            Array.Clear(next);
            bool hasStates = false;
            for (int word = 0; word < states.Length; word++)
            {
                ulong remaining = states[word];
                while (remaining != 0)
                {
                    int state = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(remaining);
                    remaining &= remaining - 1;
                    int position = state / stateWidth;
                    int edits = state % stateWidth;
                    if (position < termLen)
                    {
                        if (term[position] == input)
                        {
                            AddState(next, state + stateWidth);
                            hasStates = true;
                        }
                        else if (edits < maxEdits)
                        {
                            AddState(next, state + stateWidth + 1);
                            hasStates = true;
                        }
                    }
                    if (edits < maxEdits)
                    {
                        AddState(next, state + 1);
                        hasStates = true;
                    }
                }
            }
            if (hasStates)
                EpsilonClosure(next);
            return hasStates;
        }

        int NfaSetMinDistance(ulong[] states)
        {
            int best = int.MaxValue;
            for (int word = 0; word < states.Length; word++)
            {
                ulong remaining = states[word];
                while (remaining != 0)
                {
                    int state = (word << 6) + System.Numerics.BitOperations.TrailingZeroCount(remaining);
                    remaining &= remaining - 1;
                    int cost = state % stateWidth + termLen - state / stateWidth;
                    if (cost < best) best = cost;
                }
            }
            return best;
        }

        var initialSet = new ulong[wordCount];
        initialSet[0] = 1;
        EpsilonClosure(initialSet);
        var queue = new Queue<ulong[]>();
        var setToId = new Dictionary<ulong[], int>(StateSetComparer.Instance);
        var dfaTransitions = new List<int[]>();
        var dfaAccept = new List<bool>();
        var dfaMinDistance = new List<int>();
        int dfaStateId = 0;

        setToId[initialSet] = dfaStateId++;
        dfaTransitions.Add(new int[256]);
        Array.Fill(dfaTransitions[0], -1);
        int initialDistance = NfaSetMinDistance(initialSet);
        dfaAccept.Add(initialDistance <= maxEdits);
        dfaMinDistance.Add(initialDistance);
        queue.Enqueue(initialSet);
        var scratch = new ulong[wordCount];
        var referenceBytes = new bool[256];
        foreach (byte value in term)
            referenceBytes[value] = true;

        while (queue.Count > 0)
        {
            var currentSet = queue.Dequeue();
            int currentDfa = setToId[currentSet];
            int defaultTarget = int.MinValue;
            for (int b = 0; b < 256; b++)
            {
                // Bytes absent from the reference all have the same substitution
                // and insertion transitions. Compute that state set once per row.
                bool isDefault = !referenceBytes[b];
                if (isDefault && defaultTarget != int.MinValue)
                {
                    dfaTransitions[currentDfa][b] = defaultTarget;
                    continue;
                }
                if (!NfaStep(currentSet, (byte)b, scratch))
                {
                    dfaTransitions[currentDfa][b] = -1;
                    if (isDefault) defaultTarget = -1;
                    continue;
                }
                if (!setToId.TryGetValue(scratch, out int nextDfa))
                {
                    var nextSet = scratch.ToArray();
                    nextDfa = dfaStateId++;
                    setToId[nextSet] = nextDfa;
                    dfaTransitions.Add(new int[256]);
                    Array.Fill(dfaTransitions[^1], -1);
                    int distance = NfaSetMinDistance(nextSet);
                    dfaAccept.Add(distance <= maxEdits);
                    dfaMinDistance.Add(distance);
                    queue.Enqueue(nextSet);
                }
                dfaTransitions[currentDfa][b] = nextDfa;
                if (isDefault) defaultTarget = nextDfa;
            }
        }

        _stateCount = dfaStateId;
        _transitions = new int[_stateCount * 256];
        _accept = dfaAccept.ToArray();
        _minDistance = dfaMinDistance.ToArray();
        _canMatch = new bool[_stateCount];

        for (int s = 0; s < _stateCount; s++)
            Array.Copy(dfaTransitions[s], 0, _transitions, s * 256, 256);

        // Compute canMatch via backward reachability
        for (int s = 0; s < _stateCount; s++)
            _canMatch[s] = _accept[s];

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int s = 0; s < _stateCount; s++)
            {
                if (_canMatch[s]) continue;
                for (int b = 0; b < 256; b++)
                {
                    int next = _transitions[s * 256 + b];
                    if (next >= 0 && _canMatch[next])
                    {
                        _canMatch[s] = true;
                        changed = true;
                        break;
                    }
                }
            }
        }
    }

    /// <inheritdoc/>
    public int Start => 0;

    /// <inheritdoc/>
    public int Step(int state, byte input)
    {
        if (state < 0 || state >= _stateCount) return -1;
        if (_usesCompactCompiler)
        {
            int start = _compactExceptionOffsets![state];
            int length = _compactExceptionOffsets[state + 1] - start;
            int transition = Array.BinarySearch(_compactExceptionBytes!, start, length, input);
            return transition >= 0
                ? _compactExceptionTargets![transition]
                : _compactDefaultTransitions![state];
        }

        return _transitions[state * 256 + input];
    }

    /// <inheritdoc/>
    public bool IsAccept(int state) => state >= 0 && state < _stateCount && _accept[state];

    /// <inheritdoc/>
    public bool CanMatch(int state) => state >= 0 && state < _stateCount && _canMatch[state];

    /// <summary>Indicates whether this instance used the bounded compact subset compiler.</summary>
    internal bool UsesCompactCompiler => _usesCompactCompiler;

    /// <summary>
    /// Returns the minimum edit distance achievable when this state is accepting,
    /// or <see cref="int.MaxValue"/> when no NFA state in the set reaches an accept.
    /// Used by fuzzy queries to recover the distance from an FST traversal that ended on this state.
    /// </summary>
    public int MinDistance(int state)
    {
        if (state < 0 || state >= _stateCount) return int.MaxValue;
        return _minDistance[state];
    }

    /// <summary>Maximum edit distance the automaton was constructed with.</summary>
    public int MaxEdits => _maxEdits;

    private static CompactAutomatonData CompileCompact(byte[] term, int maxEdits, int nfaStateCount)
    {
        int termLen = term.Length;
        int stateWidth = maxEdits + 1;
        Span<bool> literalBytes = stackalloc bool[256];
        literalBytes.Clear();
        foreach (byte value in term)
            literalBytes[value] = true;

        byte defaultByte = 0;
        while (literalBytes[defaultByte])
            defaultByte++;

        ulong EpsilonClosure(ulong states)
        {
            // Every deletion edge increases the encoded NFA state id, so one
            // ascending pass also follows transitive deletion closures.
            for (int state = 0; state < nfaStateCount; state++)
            {
                if ((states & (1UL << state)) == 0)
                    continue;

                int position = state / stateWidth;
                int edits = state % stateWidth;
                if (position < termLen && edits < maxEdits)
                {
                    int deletionState = (position + 1) * stateWidth + edits + 1;
                    states |= 1UL << deletionState;
                }
            }

            return states;
        }

        ulong NfaStep(ulong states, byte input)
        {
            ulong next = 0;
            for (int state = 0; state < nfaStateCount; state++)
            {
                if ((states & (1UL << state)) == 0)
                    continue;

                int position = state / stateWidth;
                int edits = state % stateWidth;
                if (position < termLen)
                {
                    if (term[position] == input)
                    {
                        int matchState = (position + 1) * stateWidth + edits;
                        next |= 1UL << matchState;
                    }
                    else if (edits < maxEdits)
                    {
                        int substitutionState = (position + 1) * stateWidth + edits + 1;
                        next |= 1UL << substitutionState;
                    }
                }

                if (edits < maxEdits)
                {
                    int insertionState = position * stateWidth + edits + 1;
                    next |= 1UL << insertionState;
                }
            }

            return EpsilonClosure(next);
        }

        bool IsAccepting(ulong states)
        {
            for (int state = 0; state < nfaStateCount; state++)
            {
                if ((states & (1UL << state)) == 0)
                    continue;

                int position = state / stateWidth;
                int edits = state % stateWidth;
                if (termLen - position + edits <= maxEdits)
                    return true;
            }

            return false;
        }

        int MinimumDistance(ulong states)
        {
            int best = int.MaxValue;
            for (int state = 0; state < nfaStateCount; state++)
            {
                if ((states & (1UL << state)) == 0)
                    continue;

                int position = state / stateWidth;
                int edits = state % stateWidth;
                best = Math.Min(best, edits + termLen - position);
            }

            return best;
        }

        var subsetToId = new Dictionary<ulong, int>();
        var subsets = new List<ulong>();
        var defaultTransitions = new List<int>();
        var exceptionOffsets = new List<int> { 0 };
        var exceptionBytes = new List<byte>();
        var exceptionTargets = new List<int>();
        var accepts = new List<bool>();
        var minDistances = new List<int>();

        int GetState(ulong subset)
        {
            if (subset == 0)
                return -1;
            if (subsetToId.TryGetValue(subset, out int existing))
                return existing;

            int stateId = subsets.Count;
            subsetToId.Add(subset, stateId);
            subsets.Add(subset);
            accepts.Add(IsAccepting(subset));
            minDistances.Add(MinimumDistance(subset));
            return stateId;
        }

        _ = GetState(EpsilonClosure(1UL));
        for (int currentDfa = 0; currentDfa < subsets.Count; currentDfa++)
        {
            ulong currentSubset = subsets[currentDfa];
            int defaultTarget = GetState(NfaStep(currentSubset, defaultByte));
            defaultTransitions.Add(defaultTarget);

            for (int value = 0; value < literalBytes.Length; value++)
            {
                if (!literalBytes[value])
                    continue;

                int target = GetState(NfaStep(currentSubset, (byte)value));
                if (target == defaultTarget)
                    continue;

                exceptionBytes.Add((byte)value);
                exceptionTargets.Add(target);
            }

            exceptionOffsets.Add(exceptionBytes.Count);
        }

        bool[] canMatch = accepts.ToArray();
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int state = 0; state < canMatch.Length; state++)
            {
                if (canMatch[state])
                    continue;

                int defaultTarget = defaultTransitions[state];
                if (defaultTarget >= 0 && canMatch[defaultTarget])
                {
                    canMatch[state] = true;
                    changed = true;
                    continue;
                }

                int start = exceptionOffsets[state];
                int end = exceptionOffsets[state + 1];
                for (int transition = start; transition < end; transition++)
                {
                    int target = exceptionTargets[transition];
                    if (target >= 0 && canMatch[target])
                    {
                        canMatch[state] = true;
                        changed = true;
                        break;
                    }
                }
            }
        }

        return new CompactAutomatonData(
            defaultTransitions.ToArray(),
            exceptionOffsets.ToArray(),
            exceptionBytes.ToArray(),
            exceptionTargets.ToArray(),
            accepts.ToArray(),
            canMatch,
            minDistances.ToArray());
    }
}

/// <summary>
/// Provides automaton intersection with the term dictionary.
/// Walks sorted terms while simultaneously advancing the automaton,
/// pruning branches where <see cref="IAutomaton.CanMatch"/> returns false.
/// </summary>
internal static class FSTAutomaton
{
    /// <summary>
    /// Intersects the term dictionary with the automaton, returning all matching terms.
    /// The automaton operates on the bare term bytes (after the fieldPrefix).
    /// </summary>
    public static List<(string Term, long Offset)> Intersect(
        TermDictionaryReader reader, string fieldPrefix, IAutomaton automaton)
    {
        return reader.IntersectAutomaton(fieldPrefix, automaton);
    }
}
