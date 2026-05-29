namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches documents that produce at least one interval from the supplied source tree.</summary>
public sealed class IntervalsQuery : Query
{
    /// <summary>Gets the interval source to evaluate.</summary>
    public IntervalsSource Source { get; }

    /// <inheritdoc/>
    public override string Field => Source.Field;

    /// <summary>Initialises a new <see cref="IntervalsQuery"/>.</summary>
    public IntervalsQuery(IntervalsSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsQuery other &&
        Source.Equals(other.Source) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(IntervalsQuery), Source));
}

/// <summary>Base type for interval-source nodes.</summary>
public abstract class IntervalsSource : IEquatable<IntervalsSource>
{
    /// <summary>Gets the field targeted by this source.</summary>
    public abstract string Field { get; }

    /// <inheritdoc/>
    public abstract override bool Equals(object? obj);

    /// <inheritdoc/>
    public bool Equals(IntervalsSource? other) => Equals((object?)other);

    /// <inheritdoc/>
    public abstract override int GetHashCode();

    internal static string GetSharedField(IReadOnlyList<IntervalsSource> sources)
    {
        if (sources.Count == 0)
            return string.Empty;

        string field = sources[0].Field;
        for (int i = 1; i < sources.Count; i++)
        {
            if (!string.Equals(field, sources[i].Field, StringComparison.Ordinal))
                throw new ArgumentException("All interval sources must target the same field.", nameof(sources));
        }

        return field;
    }
}

/// <summary>Leaf interval source for a single exact term.</summary>
public sealed class IntervalsTermSource : IntervalsSource
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the exact term.</summary>
    public string Term { get; }

    internal string? CachedQualifiedTerm { get; set; }

    /// <summary>Initialises a new <see cref="IntervalsTermSource"/>.</summary>
    public IntervalsTermSource(string field, string term)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        if (string.IsNullOrWhiteSpace(term))
            throw new ArgumentException("Term must be a non-empty value.", nameof(term));

        Field = field;
        Term = term;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsTermSource other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        string.Equals(Term, other.Term, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nameof(IntervalsTermSource), Field, Term);
}

/// <summary>Interval source for an exact phrase.</summary>
public sealed class IntervalsPhraseSource : IntervalsSource
{
    private readonly string[] _terms;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the ordered phrase terms.</summary>
    public IReadOnlyList<string> Terms => _terms;

    /// <summary>Initialises a new <see cref="IntervalsPhraseSource"/>.</summary>
    public IntervalsPhraseSource(string field, params string[] terms)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(terms);
        Field = field;
        if (terms.Length == 0)
            throw new ArgumentException("Phrase intervals must contain at least one term.", nameof(terms));
        for (int i = 0; i < terms.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(terms[i]))
                throw new ArgumentException("Phrase terms must be non-empty.", nameof(terms));
        }

        _terms = terms.ToArray();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsPhraseSource other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        _terms.AsSpan().SequenceEqual(other._terms);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(IntervalsPhraseSource));
        hash.Add(Field);
        foreach (var term in _terms)
            hash.Add(term);
        return hash.ToHashCode();
    }
}

/// <summary>Union of any child interval source.</summary>
public sealed class IntervalsOrSource : IntervalsSource
{
    private readonly IntervalsSource[] _sources;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the child sources.</summary>
    public IReadOnlyList<IntervalsSource> Sources => _sources;

    /// <summary>Initialises a new <see cref="IntervalsOrSource"/>.</summary>
    public IntervalsOrSource(params IntervalsSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
            throw new ArgumentException("OR intervals must contain at least one child source.", nameof(sources));
        _sources = sources;
        Field = GetSharedField(_sources);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsOrSource other &&
        _sources.Length == other._sources.Length &&
        _sources.SequenceEqual(other._sources);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(IntervalsOrSource));
        foreach (var source in _sources)
            hash.Add(source);
        return hash.ToHashCode();
    }
}

/// <summary>Ordered interval composition across child sources.</summary>
public sealed class IntervalsOrderedSource : IntervalsSource
{
    private readonly IntervalsSource[] _sources;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the child sources.</summary>
    public IReadOnlyList<IntervalsSource> Sources => _sources;

    /// <summary>Gets the maximum allowed gaps across the ordered chain.</summary>
    public int MaxGaps { get; }

    /// <summary>Initialises a new <see cref="IntervalsOrderedSource"/>.</summary>
    public IntervalsOrderedSource(int maxGaps, params IntervalsSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
            throw new ArgumentException("Ordered intervals must contain at least one child source.", nameof(sources));
        ArgumentOutOfRangeException.ThrowIfNegative(maxGaps);
        _sources = sources;
        Field = GetSharedField(_sources);
        MaxGaps = maxGaps;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsOrderedSource other &&
        MaxGaps == other.MaxGaps &&
        _sources.Length == other._sources.Length &&
        _sources.SequenceEqual(other._sources);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(IntervalsOrderedSource));
        hash.Add(MaxGaps);
        foreach (var source in _sources)
            hash.Add(source);
        return hash.ToHashCode();
    }
}

/// <summary>Unordered interval composition across child sources.</summary>
public sealed class IntervalsUnorderedSource : IntervalsSource
{
    private readonly IntervalsSource[] _sources;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the child sources.</summary>
    public IReadOnlyList<IntervalsSource> Sources => _sources;

    /// <summary>Gets the maximum allowed total gaps across the merged span.</summary>
    public int MaxGaps { get; }

    /// <summary>Initialises a new <see cref="IntervalsUnorderedSource"/>.</summary>
    public IntervalsUnorderedSource(int maxGaps, params IntervalsSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
            throw new ArgumentException("Unordered intervals must contain at least one child source.", nameof(sources));
        ArgumentOutOfRangeException.ThrowIfNegative(maxGaps);
        _sources = sources;
        Field = GetSharedField(_sources);
        MaxGaps = maxGaps;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsUnorderedSource other &&
        MaxGaps == other.MaxGaps &&
        _sources.Length == other._sources.Length &&
        _sources.SequenceEqual(other._sources);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(IntervalsUnorderedSource));
        hash.Add(MaxGaps);
        foreach (var source in _sources)
            hash.Add(source);
        return hash.ToHashCode();
    }
}

/// <summary>Returns outer intervals that contain at least one inner interval.</summary>
public sealed class IntervalsContainingSource : IntervalsSource
{
    /// <summary>Gets the outer interval source.</summary>
    public IntervalsSource Outer { get; }

    /// <summary>Gets the inner interval source.</summary>
    public IntervalsSource Inner { get; }

    /// <inheritdoc/>
    public override string Field => Outer.Field;

    /// <summary>Initialises a new <see cref="IntervalsContainingSource"/>.</summary>
    public IntervalsContainingSource(IntervalsSource outer, IntervalsSource inner)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        if (!string.Equals(outer.Field, inner.Field, StringComparison.Ordinal))
            throw new ArgumentException("Interval sources must target the same field.");
        Outer = outer;
        Inner = inner;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsContainingSource other &&
        Outer.Equals(other.Outer) &&
        Inner.Equals(other.Inner);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nameof(IntervalsContainingSource), Outer, Inner);
}

/// <summary>Returns inner intervals that are fully contained by an outer interval.</summary>
public sealed class IntervalsContainedBySource : IntervalsSource
{
    /// <summary>Gets the inner interval source.</summary>
    public IntervalsSource Inner { get; }

    /// <summary>Gets the outer interval source.</summary>
    public IntervalsSource Outer { get; }

    /// <inheritdoc/>
    public override string Field => Inner.Field;

    /// <summary>Initialises a new <see cref="IntervalsContainedBySource"/>.</summary>
    public IntervalsContainedBySource(IntervalsSource inner, IntervalsSource outer)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(outer);
        if (!string.Equals(inner.Field, outer.Field, StringComparison.Ordinal))
            throw new ArgumentException("Interval sources must target the same field.");
        Inner = inner;
        Outer = outer;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsContainedBySource other &&
        Inner.Equals(other.Inner) &&
        Outer.Equals(other.Outer);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nameof(IntervalsContainedBySource), Inner, Outer);
}

/// <summary>Returns outer intervals that do not contain any inner interval.</summary>
public sealed class IntervalsNotContainingSource : IntervalsSource
{
    /// <summary>Gets the outer interval source.</summary>
    public IntervalsSource Outer { get; }

    /// <summary>Gets the inner interval source.</summary>
    public IntervalsSource Inner { get; }

    /// <inheritdoc/>
    public override string Field => Outer.Field;

    /// <summary>Initialises a new <see cref="IntervalsNotContainingSource"/>.</summary>
    public IntervalsNotContainingSource(IntervalsSource outer, IntervalsSource inner)
    {
        ArgumentNullException.ThrowIfNull(outer);
        ArgumentNullException.ThrowIfNull(inner);
        if (!string.Equals(outer.Field, inner.Field, StringComparison.Ordinal))
            throw new ArgumentException("Interval sources must target the same field.");
        Outer = outer;
        Inner = inner;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is IntervalsNotContainingSource other &&
        Outer.Equals(other.Outer) &&
        Inner.Equals(other.Inner);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(nameof(IntervalsNotContainingSource), Outer, Inner);
}
