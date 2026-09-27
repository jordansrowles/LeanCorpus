using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>Parses quoted phrases with the configured analyser and rejects unsupported embedded query operators.</summary>
public sealed class ComplexPhraseQueryParser : QueryParser
{
    /// <summary>Gets or sets whether complex phrase slots must match in query order.</summary>
    /// <remarks>Embedded complex phrase syntax is currently rejected because its positional grammar is not yet defined.</remarks>
    public bool InOrder { get; set; } = true;

    /// <summary>Initialises a complex-phrase query parser.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyser">The analyser used to build quoted phrase queries.</param>
    /// <param name="lenient">Must be <see langword="false"/>; lenient recovery was removed.</param>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public ComplexPhraseQueryParser(
        string defaultField,
        IAnalyser analyser,
        bool lenient = false)
        : base(defaultField, analyser, lenient)
    {
    }

    /// <summary>Initialises a complex-phrase parser with explicit parser-time budgets.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyser">The analyser used to build quoted phrase queries.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public ComplexPhraseQueryParser(string defaultField, IAnalyser analyser, QueryParserOptions options)
        : base(defaultField, analyser, options)
    {
    }

    /// <summary>Initialises a complex-phrase parser with a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for each parse invocation.</param>
    /// <param name="lenient">Must be <see langword="false"/>; lenient recovery was removed.</param>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public ComplexPhraseQueryParser(
        string defaultField,
        Func<IAnalyser> analyserFactory,
        bool lenient = false)
        : base(defaultField, analyserFactory, lenient)
    {
    }

    /// <summary>Initialises a complex-phrase parser with explicit budgets and a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit field prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for each parse invocation.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public ComplexPhraseQueryParser(string defaultField, Func<IAnalyser> analyserFactory, QueryParserOptions options)
        : base(defaultField, analyserFactory, options)
    {
    }

    /// <inheritdoc/>
    private protected override Query BuildPhraseQuery(string field, string phraseText, string rawPhraseText, int slop)
    {
        if (ContainsUnsupportedComplexSyntax(rawPhraseText.AsSpan()))
        {
            throw new QueryParseException(
                "Embedded complex phrase operators are unsupported until a position-preserving grammar is available.",
                0);
        }

        return base.BuildPhraseQuery(field, phraseText, slop);
    }

    private static bool ContainsUnsupportedComplexSyntax(ReadOnlySpan<char> rawPhraseText)
    {
        bool atTokenStart = true;
        for (int index = 0; index < rawPhraseText.Length; index++)
        {
            char current = rawPhraseText[index];
            if (current == '\\' && index + 1 < rawPhraseText.Length)
            {
                index++;
                atTokenStart = false;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                atTokenStart = true;
                continue;
            }

            // These are the operators the former phrase parser attempted to lower
            // independently from the complete phrase analyser. A slash is syntax
            // only at a token boundary, where it can start a regexp clause; a
            // slash inside ordinary text such as "foo/bar" stays analyser input.
            if (current is '(' or ')' or '*' or '?' or '~'
                || (current == '/' && atTokenStart))
            {
                return true;
            }

            atTokenStart = false;
        }

        return false;
    }
}
