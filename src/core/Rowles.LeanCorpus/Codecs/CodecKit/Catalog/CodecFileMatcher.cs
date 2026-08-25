namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// Matches a logical index file name without using reflection or regular expressions.
/// </summary>
public sealed class CodecFileMatcher
{
    private readonly CodecFileMatcherKind _kind;
    private readonly string _prefix;
    private readonly string _suffix;

    private CodecFileMatcher(CodecFileMatcherKind kind, string prefix, string suffix)
    {
        _kind = kind;
        _prefix = prefix;
        _suffix = suffix;
    }

    /// <summary>
    /// Creates a matcher for a file extension, including generated and per-field file names.
    /// </summary>
    /// <param name="extension">The extension, with or without its leading full stop.</param>
    /// <returns>A matcher for the extension.</returns>
    public static CodecFileMatcher Extension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var suffix = extension.Length > 0 && extension[0] == '.' ? extension : "." + extension;
        return new CodecFileMatcher(CodecFileMatcherKind.Suffix, string.Empty, suffix);
    }

    /// <summary>
    /// Creates a matcher for an exact logical file name.
    /// </summary>
    /// <param name="fileName">The exact file name.</param>
    /// <returns>A matcher for the file name.</returns>
    public static CodecFileMatcher Exact(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return new CodecFileMatcher(CodecFileMatcherKind.Exact, fileName, string.Empty);
    }

    /// <summary>
    /// Creates a matcher whose file name has a fixed prefix and suffix.
    /// </summary>
    /// <param name="prefix">The required prefix.</param>
    /// <param name="suffix">The required suffix.</param>
    /// <returns>A matcher for the prefix and suffix.</returns>
    public static CodecFileMatcher PrefixAndSuffix(string prefix, string suffix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        return new CodecFileMatcher(CodecFileMatcherKind.PrefixAndSuffix, prefix, suffix);
    }

    /// <summary>
    /// Creates a matcher whose file name has a fixed prefix and suffix with a decimal number between them.
    /// </summary>
    /// <param name="prefix">The required prefix.</param>
    /// <param name="suffix">The required suffix.</param>
    /// <returns>A matcher for the numbered file name.</returns>
    public static CodecFileMatcher Numbered(string prefix, string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        return new CodecFileMatcher(CodecFileMatcherKind.Numbered, prefix, suffix);
    }

    /// <summary>
    /// Creates a matcher for temporary names containing a codec extension followed by an optional token and a fixed suffix.
    /// </summary>
    /// <param name="extension">The owned codec extension, with or without its leading full stop.</param>
    /// <param name="trailingSuffix">The final temporary-file suffix.</param>
    /// <returns>A matcher covering direct, body-staging and tokenised temporary names.</returns>
    public static CodecFileMatcher ExtensionWithTrailingSuffix(string extension, string trailingSuffix)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(trailingSuffix);
        var normalisedExtension = extension.Length > 0 && extension[0] == '.' ? extension : "." + extension;
        return new CodecFileMatcher(CodecFileMatcherKind.ExtensionWithTrailingSuffix, normalisedExtension, trailingSuffix);
    }

    /// <summary>Returns whether the supplied logical file name matches this declaration.</summary>
    /// <param name="fileName">A logical file name or path.</param>
    public bool IsMatch(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var name = GetFileName(fileName);

        return _kind switch
        {
            CodecFileMatcherKind.Exact => name.Equals(_prefix, StringComparison.OrdinalIgnoreCase),
            CodecFileMatcherKind.Suffix => name.EndsWith(_suffix, StringComparison.OrdinalIgnoreCase),
            CodecFileMatcherKind.PrefixAndSuffix =>
                name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(_suffix, StringComparison.OrdinalIgnoreCase) &&
                name.Length >= _prefix.Length + _suffix.Length,
            CodecFileMatcherKind.Numbered => IsNumberedMatch(name),
            CodecFileMatcherKind.ExtensionWithTrailingSuffix => IsExtensionWithTrailingSuffixMatch(name),
            _ => false,
        };
    }

    internal string PhysicalClaim => string.Concat(
        ((int)_kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ":",
        _prefix.ToLowerInvariant(),
        ":",
        _suffix.ToLowerInvariant());

    internal bool Overlaps(CodecFileMatcher other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var representative in GetRepresentativeFileNames())
        {
            if (other.IsMatch(representative))
                return true;
        }

        foreach (var representative in other.GetRepresentativeFileNames())
        {
            if (IsMatch(representative))
                return true;
        }

        return false;
    }

    internal void Validate(string parameterName)
    {
        if (_kind is CodecFileMatcherKind.Exact or CodecFileMatcherKind.PrefixAndSuffix or CodecFileMatcherKind.Numbered or CodecFileMatcherKind.ExtensionWithTrailingSuffix)
        {
            if (string.IsNullOrWhiteSpace(_prefix))
                throw new ArgumentException("A file matcher prefix or exact name cannot be empty.", parameterName);
        }

        if (_kind == CodecFileMatcherKind.Suffix &&
            (_suffix.Length < 2 || _suffix[0] != '.' || _suffix.AsSpan().ContainsAny('/', '\\')))
        {
            throw new ArgumentException($"Invalid file extension '{_suffix}'.", parameterName);
        }

        if (_prefix.AsSpan().ContainsAny('/', '\\') || _suffix.AsSpan().ContainsAny('/', '\\'))
            throw new ArgumentException("File matcher components cannot contain directory separators.", parameterName);

        if (_kind == CodecFileMatcherKind.PrefixAndSuffix && _suffix.Length == 0)
            throw new ArgumentException("A prefix-and-suffix matcher requires a suffix.", parameterName);

        if (_kind == CodecFileMatcherKind.ExtensionWithTrailingSuffix &&
            (_prefix.Length < 2 || _prefix[0] != '.' || _suffix.Length == 0))
        {
            throw new ArgumentException("An extension-with-trailing-suffix matcher requires an extension and suffix.", parameterName);
        }
    }

    private bool IsNumberedMatch(ReadOnlySpan<char> name)
    {
        if (!name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(_suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var middleLength = name.Length - _prefix.Length - _suffix.Length;
        if (middleLength <= 0)
            return false;

        var middle = name.Slice(_prefix.Length, middleLength);
        foreach (var character in middle)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }

    private bool IsExtensionWithTrailingSuffixMatch(ReadOnlySpan<char> name)
    {
        if (!name.EndsWith(_suffix, StringComparison.OrdinalIgnoreCase) ||
            name.Length < _prefix.Length + _suffix.Length)
        {
            return false;
        }

        var searchFrom = 0;
        while (searchFrom < name.Length)
        {
            var relativeIndex = name[searchFrom..].IndexOf(_prefix, StringComparison.OrdinalIgnoreCase);
            if (relativeIndex < 0)
                return false;

            var extensionEnd = searchFrom + relativeIndex + _prefix.Length;
            if (extensionEnd < name.Length && name[extensionEnd] == '.')
                return true;

            searchFrom += relativeIndex + 1;
        }

        return false;
    }

    private static ReadOnlySpan<char> GetFileName(string path)
    {
        var span = path.AsSpan();
        var slash = span.LastIndexOf('/');
        var backslash = span.LastIndexOf('\\');
        var separator = Math.Max(slash, backslash);
        return separator < 0 ? span : span[(separator + 1)..];
    }

    private string[] GetRepresentativeFileNames()
        => _kind switch
        {
            CodecFileMatcherKind.Exact => [_prefix],
            CodecFileMatcherKind.Suffix => ["file" + _suffix],
            CodecFileMatcherKind.PrefixAndSuffix => [_prefix + "file" + _suffix],
            CodecFileMatcherKind.Numbered => [_prefix + "1" + _suffix],
            CodecFileMatcherKind.ExtensionWithTrailingSuffix =>
                ["file" + _prefix + _suffix, "file" + _prefix + ".token" + _suffix],
            _ => [],
        };

    private enum CodecFileMatcherKind
    {
        Exact,
        Suffix,
        PrefixAndSuffix,
        Numbered,
        ExtensionWithTrailingSuffix,
    }
}
