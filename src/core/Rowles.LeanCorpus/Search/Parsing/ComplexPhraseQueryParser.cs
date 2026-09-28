using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>Parses analysed quoted phrases with flat <c>(a OR b)</c> alternative slots.</summary>
public sealed class ComplexPhraseQueryParser : QueryParser
{
    /// <summary>Gets or sets whether phrase slots must match in query order.</summary>
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
        ReadOnlySpan<char> raw = rawPhraseText.AsSpan();
        if (ContainsUnescapedParenthesis(raw))
            return BuildFlatAlternativePhrase(field, raw, slop);

        if (ContainsUnsupportedComplexSyntax(raw))
            throw UnsupportedEmbeddedSyntax();

        return base.BuildPhraseQuery(field, phraseText, slop);
    }

    private Query BuildFlatAlternativePhrase(string field, ReadOnlySpan<char> rawPhraseText, int slop)
    {
        List<PhraseSlot> slots = ParseFlatPhrase(rawPhraseText);
        int clauseCount = 0;
        int alternativeClauseCount = 0;
        foreach (PhraseSlot slot in slots)
        {
            clauseCount += slot.Terms.Count;
            if (slot.IsAlternative)
                alternativeClauseCount += slot.Terms.Count;
        }

        ConsumeComplexPhraseClauses(clauseCount, alternativeClauseCount);

        var clauses = new List<SpanQuery>(slots.Count);
        foreach (PhraseSlot slot in slots)
        {
            var terms = new SpanQuery[slot.Terms.Count];
            for (int index = 0; index < slot.Terms.Count; index++)
            {
                string analysedTerm = AnalyseComplexPhraseTerm(field, slot.Terms[index]);
                terms[index] = new SpanTermQuery(field, analysedTerm);
            }

            clauses.Add(slot.IsAlternative
                ? new SpanOrQuery(terms)
                : terms[0]);
        }

        return clauses.Count == 1
            ? clauses[0]
            : new SpanNearQuery(clauses.ToArray(), slop, InOrder);
    }

    private static List<PhraseSlot> ParseFlatPhrase(ReadOnlySpan<char> rawPhraseText)
    {
        var slots = new List<PhraseSlot>();
        int position = 0;
        while (position < rawPhraseText.Length)
        {
            SkipWhitespace(rawPhraseText, ref position);
            if (position == rawPhraseText.Length)
                break;

            if (rawPhraseText[position] == ')')
                throw InvalidAlternative("unmatched closing parenthesis");

            PhraseSlot slot;
            if (rawPhraseText[position] == '(')
            {
                slot = ParseAlternativeGroup(rawPhraseText, ref position);
            }
            else
            {
                PhraseTerm term = ReadTerm(rawPhraseText, ref position);
                ValidateSimpleTerm(term);
                slot = new PhraseSlot([term.Value], IsAlternative: false);
            }

            if (position < rawPhraseText.Length && !char.IsWhiteSpace(rawPhraseText[position]))
                throw InvalidAlternative("phrase slots must be separated by whitespace");

            slots.Add(slot);
        }

        if (slots.Count == 0 || !slots.Any(static slot => slot.IsAlternative))
            throw InvalidAlternative("a flat alternative group is required");

        return slots;
    }

    private static PhraseSlot ParseAlternativeGroup(ReadOnlySpan<char> rawPhraseText, ref int position)
    {
        position++; // opening parenthesis
        var terms = new List<string>();
        bool expectingTerm = true;

        while (true)
        {
            SkipWhitespace(rawPhraseText, ref position);
            if (position >= rawPhraseText.Length)
                throw InvalidAlternative("unmatched opening parenthesis");

            char current = rawPhraseText[position];
            if (current == '(')
                throw InvalidAlternative("nested groups are unsupported");

            if (current == ')')
            {
                if (expectingTerm || terms.Count < 2)
                    throw InvalidAlternative("groups require at least two terms separated by OR");
                position++;
                return new PhraseSlot(terms, IsAlternative: true);
            }

            PhraseTerm term = ReadTerm(rawPhraseText, ref position);
            if (term.Value.Length == 0)
                throw InvalidAlternative("a term was expected");

            bool isOr = !term.WasEscaped
                && term.Value.Equals("OR", StringComparison.OrdinalIgnoreCase);
            if (expectingTerm)
            {
                if (isOr)
                    throw InvalidAlternative("OR must appear between terms");
                ValidateSimpleTerm(term);
                terms.Add(term.Value);
                expectingTerm = false;
            }
            else
            {
                if (!isOr)
                    throw InvalidAlternative("terms in a group must be separated by OR");
                expectingTerm = true;
            }
        }
    }

    private static PhraseTerm ReadTerm(ReadOnlySpan<char> rawPhraseText, ref int position)
    {
        var value = new System.Text.StringBuilder();
        bool wasEscaped = false;
        while (position < rawPhraseText.Length)
        {
            char current = rawPhraseText[position];
            if (char.IsWhiteSpace(current) || current is '(' or ')')
                break;

            position++;
            if (current == '\\')
            {
                wasEscaped = true;
                if (position < rawPhraseText.Length)
                    value.Append(rawPhraseText[position++]);
                else
                    value.Append('\\');
            }
            else
            {
                value.Append(current);
            }
        }

        return new PhraseTerm(value.ToString(), wasEscaped);
    }

    private static void ValidateSimpleTerm(PhraseTerm term)
    {
        if (term.Value.Length == 0)
            throw InvalidAlternative("a term was expected");
        if (term.WasEscaped)
            throw UnsupportedEmbeddedSyntax();

        foreach (char current in term.Value)
        {
            if (current is '*' or '?' or '~' or '/' or '^' or '=' or ':' or '[' or ']' or '{' or '}' or '|')
                throw UnsupportedEmbeddedSyntax();
        }
    }

    private static void SkipWhitespace(ReadOnlySpan<char> text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
            position++;
    }

    private static bool ContainsUnescapedParenthesis(ReadOnlySpan<char> text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                index++;
                continue;
            }

            if (text[index] is '(' or ')')
                return true;
        }

        return false;
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

            // A slash is syntax only at a token boundary, where it can start a
            // regexp clause. Ordinary slashes remain analyser input.
            if (current is '*' or '?' or '~' || (current == '/' && atTokenStart))
                return true;

            atTokenStart = false;
        }

        return false;
    }

    private static QueryParseException InvalidAlternative(string reason) =>
        new($"Invalid complex phrase alternative group: {reason}.", 0);

    private static QueryParseException UnsupportedEmbeddedSyntax() =>
        new(
            "Complex phrase syntax supports flat alternatives only; other embedded operators remain unsupported until a position-preserving grammar is available.",
            0);

    private readonly record struct PhraseTerm(string Value, bool WasEscaped);

    private sealed record PhraseSlot(IReadOnlyList<string> Terms, bool IsAlternative);
}
