using System.Text;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Represents one hierarchical facet path as ordered components.</summary>
/// <remarks>
/// <see cref="Components"/> is the authoritative representation. The indexed
/// values returned by <see cref="ToIndexedValues"/> are an internal-safe,
/// length-prefixed representation of each path prefix.
/// </remarks>
public sealed class FacetPath : IEquatable<FacetPath>
{
    /// <summary>Maximum supported hierarchy depth, bounding prefix expansion at index time.</summary>
    public const int MaximumDepth = 32;
    private readonly string[] _components;
    private readonly IReadOnlyList<string> _readOnlyComponents;

    /// <summary>Initialises a path from ordered components.</summary>
    /// <param name="components">The non-empty ordered path components.</param>
    public FacetPath(IReadOnlyList<string> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (components.Count == 0)
            throw new ArgumentException("A facet path must contain at least one component.", nameof(components));
        if (components.Count > MaximumDepth)
            throw new ArgumentOutOfRangeException(nameof(components), components.Count,
                $"A facet path cannot contain more than {MaximumDepth} components.");

        _components = new string[components.Count];
        for (int i = 0; i < components.Count; i++)
            _components[i] = components[i] ?? throw new ArgumentException("Path components must not be null.", nameof(components));

        _readOnlyComponents = Array.AsReadOnly(_components);
    }

    /// <summary>Initialises a path from ordered components.</summary>
    public FacetPath(params string[] components)
        : this((IReadOnlyList<string>)components)
    {
    }

    /// <summary>Gets the ordered, immutable path components.</summary>
    public IReadOnlyList<string> Components => _readOnlyComponents;

    /// <summary>
    /// Converts this path to the values that should be indexed for hierarchical faceting.
    /// The returned values contain every prefix, including the complete path.
    /// </summary>
    public IReadOnlyList<string> ToIndexedValues()
        => FacetPathEncoder.EncodePrefixes(_components);

    /// <summary>Returns a display representation of the path.</summary>
    /// <remarks>This representation is for display only and is not the indexed identity.</remarks>
    public override string ToString() => string.Join(" / ", _components);

    /// <inheritdoc/>
    public bool Equals(FacetPath? other)
    {
        if (other is null || _components.Length != other._components.Length)
            return false;

        for (int i = 0; i < _components.Length; i++)
        {
            if (!string.Equals(_components[i], other._components[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FacetPath other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(FacetPath));
        foreach (var component in _components)
            hash.Add(component, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>Indexes hierarchical facet paths through existing string fields and DocValues.</summary>
public static class FacetPathIndexer
{
    /// <summary>
    /// Adds all prefixes of <paramref name="path"/> as queryable sorted-set string fields.
    /// </summary>
    /// <remarks>
    /// The generated fields use existing postings and sorted-set DocValues. This
    /// helper deliberately keeps the dimension queryable as well as facetable.
    /// Add a separate <see cref="StoredField"/> when an application needs a
    /// display copy of the original path.
    /// </remarks>
    /// <param name="document">The document to which the path fields are added.</param>
    /// <param name="fieldName">The facet dimension field name.</param>
    /// <param name="path">The path to index.</param>
    /// <param name="boost">Index-time boost applied to the generated string fields.</param>
    /// <param name="indexOptions">Postings data to write for the generated fields.</param>
    public static void AddToDocument(
        LeanDocument document,
        string fieldName,
        FacetPath path,
        float boost = 1.0f,
        FieldIndexOptions indexOptions = FieldIndexOptions.DocsAndFreqs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(path);

        foreach (var value in path.ToIndexedValues())
        {
            document.Add(new StringField(
                fieldName,
                value,
                stored: false,
                boost,
                StringDocValues.SortedSet,
                indexOptions));
        }
    }
}

internal static class FacetPathEncoder
{
    // The length prefixes make component delimiters, including '/', ':' and
    // the marker itself, ordinary data rather than syntax.
    internal const string Prefix = "\uE000LCFacetPath1:";

    internal static string[] EncodePrefixes(IReadOnlyList<string> components)
    {
        if (components.Count is < 1 or > FacetPath.MaximumDepth)
            throw new ArgumentOutOfRangeException(nameof(components),
                $"A facet path must contain between 1 and {FacetPath.MaximumDepth} components.");
        var values = new string[components.Count];
        for (int count = 1; count <= components.Count; count++)
            values[count - 1] = Encode(components, count);
        return values;
    }

    internal static string Encode(IReadOnlyList<string> components, int count)
    {
        if (count is < 1 or > FacetPath.MaximumDepth || count > components.Count)
            throw new ArgumentOutOfRangeException(nameof(count),
                $"A facet path cannot contain more than {FacetPath.MaximumDepth} components.");
        var builder = new StringBuilder(Prefix.Length + (count * 8));
        builder.Append(Prefix);
        for (int i = 0; i < count; i++)
        {
            string component = components[i];
            builder.Append(component.Length).Append(':').Append(component);
        }
        return builder.ToString();
    }

    internal static bool IsEncodedPath(string value)
    {
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        int cursor = Prefix.Length;
        int componentCount = 0;
        while (cursor < value.Length)
        {
            if (!TryReadComponent(value, ref cursor, out _, out _))
                return false;
            componentCount++;
        }

        return componentCount is > 0 and <= FacetPath.MaximumDepth;
    }

    internal static bool TryGetImmediateChild(
        string value,
        FacetPath? parentPath,
        out string? child)
    {
        child = null;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parent = parentPath?.Components;
        int parentCount = parent?.Count ?? 0;
        int cursor = Prefix.Length;
        int componentIndex = 0;
        int childStart = -1;
        int childLength = 0;
        bool matchesParent = true;

        while (cursor < value.Length)
        {
            if (!TryReadComponent(value, ref cursor, out int componentStart, out int componentLength))
                return false;

            if (componentIndex < parentCount)
            {
                if (!value.AsSpan(componentStart, componentLength)
                    .SequenceEqual(parent![componentIndex].AsSpan()))
                    matchesParent = false;
            }
            else if (componentIndex == parentCount)
            {
                childStart = componentStart;
                childLength = componentLength;
            }

            componentIndex++;
        }

        if (!matchesParent || componentIndex != parentCount + 1 || childStart < 0)
            return false;

        child = value.Substring(childStart, childLength);
        return true;
    }

    private static bool TryReadComponent(
        string value,
        ref int cursor,
        out int componentStart,
        out int componentLength)
    {
        componentStart = 0;
        componentLength = 0;
        int colon = value.IndexOf(':', cursor);
        if (colon <= cursor)
            return false;

        int length = 0;
        for (int i = cursor; i < colon; i++)
        {
            char digit = value[i];
            if (digit is < '0' or > '9')
                return false;

            int next = (length * 10) + (digit - '0');
            if (next < length)
                return false;
            length = next;
        }

        int start = colon + 1;
        if (length > value.Length - start)
            return false;

        componentStart = start;
        componentLength = length;
        cursor = start + length;
        return true;
    }
}
