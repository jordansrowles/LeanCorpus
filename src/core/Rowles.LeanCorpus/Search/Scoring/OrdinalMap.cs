namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Maps sorted local ordinals from several readers into one stable, lexicographically
/// ordered ordinal space.
/// </summary>
/// <remarks>
/// Each source term list must be sorted with <see cref="StringComparer.Ordinal"/> and
/// must not contain duplicates. The map is immutable after construction, so readers can
/// share it across concurrent aggregation, grouping, and sorted-value operations.
/// </remarks>
public sealed class OrdinalMap
{
    private readonly string[][] _sourceTerms;
    private readonly int[][] _sourceToGlobal;
    private readonly string[] _globalTerms;
    private readonly Dictionary<string, int> _globalLookup;

    private OrdinalMap(
        string[][] sourceTerms,
        int[][] sourceToGlobal,
        string[] globalTerms,
        Dictionary<string, int> globalLookup)
    {
        _sourceTerms = sourceTerms;
        _sourceToGlobal = sourceToGlobal;
        _globalTerms = globalTerms;
        _globalLookup = globalLookup;
    }

    /// <summary>Gets the number of source ordinal spaces represented by this map.</summary>
    public int SourceCount => _sourceTerms.Length;

    /// <summary>Gets the number of unique terms in the global ordinal space.</summary>
    public int ValueCount => _globalTerms.Length;

    /// <summary>Gets the global terms in ordinal order.</summary>
    public IReadOnlyList<string> Terms => _globalTerms;

    /// <summary>
    /// Builds a map from one sorted, duplicate-free term list per source reader.
    /// </summary>
    /// <param name="sourceTerms">The local term dictionaries in local ordinal order.</param>
    /// <exception cref="ArgumentNullException">Thrown when a source or term list is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a source list is not strictly sorted.</exception>
    public static OrdinalMap Build(IReadOnlyList<IReadOnlyList<string>> sourceTerms)
    {
        ArgumentNullException.ThrowIfNull(sourceTerms);

        var copiedSources = new string[sourceTerms.Count][];
        for (int source = 0; source < sourceTerms.Count; source++)
        {
            var terms = sourceTerms[source]
                ?? throw new ArgumentNullException(nameof(sourceTerms), "A source term list cannot be null.");
            var copy = new string[terms.Count];
            for (int ordinal = 0; ordinal < terms.Count; ordinal++)
            {
                var term = terms[ordinal]
                    ?? throw new ArgumentException("A source term cannot be null.", nameof(sourceTerms));
                if (ordinal > 0 && string.CompareOrdinal(copy[ordinal - 1], term) >= 0)
                {
                    throw new ArgumentException(
                        $"Source term list {source} is not strictly sorted at ordinal {ordinal}.",
                        nameof(sourceTerms));
                }
                copy[ordinal] = term;
            }
            copiedSources[source] = copy;
        }

        var positions = new int[copiedSources.Length];
        var globalTerms = new List<string>();
        while (true)
        {
            string? next = null;
            for (int source = 0; source < copiedSources.Length; source++)
            {
                int position = positions[source];
                if (position >= copiedSources[source].Length)
                    continue;
                var candidate = copiedSources[source][position];
                if (next is null || string.CompareOrdinal(candidate, next) < 0)
                    next = candidate;
            }

            if (next is null)
                break;

            globalTerms.Add(next);
            for (int source = 0; source < copiedSources.Length; source++)
            {
                var terms = copiedSources[source];
                while (positions[source] < terms.Length
                    && string.Equals(terms[positions[source]], next, StringComparison.Ordinal))
                {
                    positions[source]++;
                }
            }
        }

        var globalArray = globalTerms.ToArray();
        var globalLookup = new Dictionary<string, int>(globalArray.Length, StringComparer.Ordinal);
        for (int ordinal = 0; ordinal < globalArray.Length; ordinal++)
            globalLookup.Add(globalArray[ordinal], ordinal);

        var sourceToGlobal = new int[copiedSources.Length][];
        for (int source = 0; source < copiedSources.Length; source++)
        {
            var terms = copiedSources[source];
            var mapping = new int[terms.Length];
            for (int ordinal = 0; ordinal < terms.Length; ordinal++)
                mapping[ordinal] = globalLookup[terms[ordinal]];
            sourceToGlobal[source] = mapping;
        }

        return new OrdinalMap(copiedSources, sourceToGlobal, globalArray, globalLookup);
    }

    /// <summary>Gets the global ordinal for a source-local ordinal.</summary>
    public int GetGlobalOrdinal(int sourceIndex, int localOrdinal)
    {
        ValidateSourceIndex(sourceIndex);
        if ((uint)localOrdinal >= (uint)_sourceToGlobal[sourceIndex].Length)
            throw new ArgumentOutOfRangeException(nameof(localOrdinal));
        return _sourceToGlobal[sourceIndex][localOrdinal];
    }

    /// <summary>Looks up a global ordinal by term in a source reader.</summary>
    public bool TryGetGlobalOrdinal(int sourceIndex, string term, out int globalOrdinal)
    {
        ValidateSourceIndex(sourceIndex);
        ArgumentNullException.ThrowIfNull(term);
        if (!_globalLookup.TryGetValue(term, out globalOrdinal))
        {
            globalOrdinal = -1;
            return false;
        }
        if (Array.BinarySearch(_sourceTerms[sourceIndex], term, StringComparer.Ordinal) >= 0)
            return true;
        globalOrdinal = -1;
        return false;
    }

    /// <summary>Gets the term represented by a global ordinal.</summary>
    public string GetTerm(int globalOrdinal)
    {
        if ((uint)globalOrdinal >= (uint)_globalTerms.Length)
            throw new ArgumentOutOfRangeException(nameof(globalOrdinal));
        return _globalTerms[globalOrdinal];
    }

    private void ValidateSourceIndex(int sourceIndex)
    {
        if ((uint)sourceIndex >= (uint)_sourceTerms.Length)
            throw new ArgumentOutOfRangeException(nameof(sourceIndex));
    }
}
