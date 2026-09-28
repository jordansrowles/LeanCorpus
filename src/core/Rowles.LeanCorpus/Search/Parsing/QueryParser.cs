using Rowles.LeanCorpus.Analysis.Analysers;
namespace Rowles.LeanCorpus.Search.Parsing;

/// <summary>
/// Parses a query string into a Query object tree.
/// Supports: term, field:term, "phrase", +required, -excluded, (grouping),
/// explicit boolean operators, ranges, regular expressions, field existence,
/// prefix*, wild?card, fuzzy~N, "phrase"~N, boosts, and constant scores.
/// </summary>
/// <remarks>
/// When one unquoted syntax term analyses to multiple independent positions, those
/// positions use the parser's implicit OR operator. Same-position alternatives are
/// retained as Boolean alternatives, and non-unit graph edges use bounded phrase-path
/// compilation.
///
/// Each <see cref="Parse(string)"/> call uses independent parser state. Instances created
/// with an <see cref="IAnalyser"/> accept sequential calls and reject overlapping non-empty calls;
/// use the analyser-factory constructor to share a parser across threads. The factory is
/// called once per non-empty parse and must return an analyser instance that is safe for that parse.
/// Custom subclasses must keep per-parse data out of mutable instance fields and should be
/// configured before concurrent parsing begins.
/// </remarks>
public class QueryParser
{
    private readonly string _defaultField;
    private IAnalyser? _analyser;
    private readonly Func<IAnalyser>? _analyserFactory;
    private readonly Func<string, QueryFieldCompilationContext>? _fieldContextResolver;
    private readonly QueryParserOptions _options;
    private QueryCompiler? _compiler;
    private int _parseInProgress;

    /// <summary>Gets the analyser used to build query terms.</summary>
    protected IAnalyser Analyser => _analyser
        ?? throw new InvalidOperationException("An analyser is available only during a parse invocation.");

    /// <summary>Initialises a new <see cref="QueryParser"/> with the given default field and analyser.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyser">The analyser used to tokenise terms and phrases at query time.</param>
    /// <param name="lenient">Must be <see langword="false"/>. Lenient recovery was removed because the parser has no deterministic recovery grammar.</param>
    /// <param name="maxGraphPaths">The maximum complete analysed phrase paths permitted across one parse.</param>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public QueryParser(string defaultField, IAnalyser analyser, bool lenient = false, int maxGraphPaths = 256)
        : this(defaultField, analyser, fieldContextResolver: null, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    /// <summary>Initialises a parser with explicit parser-time budgets.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyser">The analyser used to tokenise terms and phrases at query time.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public QueryParser(string defaultField, IAnalyser analyser, QueryParserOptions options)
        : this(defaultField, analyser, fieldContextResolver: null, ValidateOptions(options))
    {
    }

    /// <summary>
    /// Initialises a parser whose analyser factory is invoked once for each non-empty parse.
    /// This constructor supports concurrent calls on the same parser instance.
    /// </summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present.</param>
    /// <param name="analyserFactory">Creates an analyser for one parse invocation.</param>
    /// <param name="lenient">Must be <see langword="false"/>. Lenient recovery was removed because the parser has no deterministic recovery grammar.</param>
    /// <param name="maxGraphPaths">The maximum complete analysed phrase paths permitted across one parse.</param>
    /// <remarks>
    /// The factory must be safe for concurrent calls and return a fresh analyser per call,
    /// or an analyser that is independently safe for concurrent use.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="lenient"/> is <see langword="true"/>.</exception>
    public QueryParser(string defaultField, Func<IAnalyser> analyserFactory, bool lenient = false, int maxGraphPaths = 256)
        : this(defaultField, analyserFactory, fieldContextResolver: null, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    /// <summary>Initialises a parser with explicit parser-time budgets and a per-parse analyser factory.</summary>
    /// <param name="defaultField">The field used when no explicit <c>field:</c> prefix is present in the query string.</param>
    /// <param name="analyserFactory">Creates an analyser for one parse invocation.</param>
    /// <param name="options">The parser-time limits to enforce for each query.</param>
    public QueryParser(string defaultField, Func<IAnalyser> analyserFactory, QueryParserOptions options)
        : this(defaultField, analyserFactory, fieldContextResolver: null, ValidateOptions(options))
    {
    }

    internal QueryParser(
        string defaultField,
        IAnalyser analyser,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        bool lenient = false,
        int maxGraphPaths = 256)
        : this(defaultField, analyser, fieldContextResolver, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    internal QueryParser(
        string defaultField,
        IAnalyser analyser,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        QueryParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(defaultField);
        ArgumentNullException.ThrowIfNull(analyser);
        _options = ValidateOptions(options);
        _defaultField = defaultField;
        _analyser = analyser;
        _fieldContextResolver = fieldContextResolver;
    }

    internal QueryParser(
        string defaultField,
        Func<IAnalyser> analyserFactory,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        bool lenient = false,
        int maxGraphPaths = 256)
        : this(defaultField, analyserFactory, fieldContextResolver, CreateLegacyOptions(lenient, maxGraphPaths))
    {
    }

    internal QueryParser(
        string defaultField,
        Func<IAnalyser> analyserFactory,
        Func<string, QueryFieldCompilationContext>? fieldContextResolver,
        QueryParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(defaultField);
        ArgumentNullException.ThrowIfNull(analyserFactory);
        _options = ValidateOptions(options);
        _defaultField = defaultField;
        _analyserFactory = analyserFactory;
        _fieldContextResolver = fieldContextResolver;
    }

    private static QueryParserOptions CreateLegacyOptions(bool lenient, int maxGraphPaths)
    {
        EnsureStrictParsing(lenient);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxGraphPaths, 1);
        return ValidateOptions(QueryParserOptions.Trusted with { MaxGraphPaths = maxGraphPaths });
    }

    private static QueryParserOptions ValidateOptions(QueryParserOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Validate();
    }

    private static void EnsureStrictParsing(bool lenient)
    {
        if (lenient)
        {
            throw new NotSupportedException(
                "Lenient parsing has been removed because query recovery has no deterministic clause boundaries. Construct the parser without lenient mode.");
        }
    }

    /// <summary>Parses the query string into a <see cref="Query"/> object tree.</summary>
    /// <param name="queryString">The query string to parse.</param>
    /// <returns>
    /// A <see cref="Query"/> representing the parsed expression, or an empty
    /// <see cref="BooleanQuery"/> when <paramref name="queryString"/> is null or whitespace.
    /// </returns>
    /// <exception cref="QueryParseException">Thrown when the query string contains a syntax error.</exception>
    public Query Parse(string queryString)
    {
        if (queryString is not null && queryString.Length > _options.MaxInputChars)
            throw new QueryParseException(
                $"The query exceeds the configured input character limit of {_options.MaxInputChars}.",
                _options.MaxInputChars);
        if (string.IsNullOrWhiteSpace(queryString))
            return new BooleanQuery.Builder().Build();

        bool hasFixedAnalyser = _analyserFactory is null;
        if (hasFixedAnalyser && System.Threading.Interlocked.CompareExchange(ref _parseInProgress, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "This QueryParser uses a fixed analyser and cannot parse concurrently. Use an analyser factory to share it across threads.");
        }

        try
        {
            QueryParser invocation = CreateInvocationParser();
            return invocation.CompileSyntax(invocation.ParseSyntax(queryString));
        }
        catch (QueryParseLimitException exception)
        {
            throw exception.Offset is int offset
                ? new QueryParseException(exception.Message, offset)
                : new QueryParseException(exception.Message);
        }
        finally
        {
            if (hasFixedAnalyser)
                System.Threading.Volatile.Write(ref _parseInProgress, 0);
        }
    }

    private QueryParser CreateInvocationParser()
    {
        var invocation = (QueryParser)MemberwiseClone();
        invocation._analyser = _analyserFactory is null
            ? _analyser
            : _analyserFactory()
                ?? throw new InvalidOperationException("The analyser factory returned null.");
        invocation._compiler = null;
        invocation._parseInProgress = 0;
        return invocation;
    }

    internal QuerySyntax ParseSyntax(
        string queryString,
        bool limitsAreComplexity = false)
    {
        var compiler = new QueryCompiler(this, _options, limitsAreComplexity);
        _compiler = compiler;

        if (queryString is not null && queryString.Length > _options.MaxInputChars)
            compiler.ThrowQueryParseLimitExceeded(
                $"The query exceeds the configured input character limit of {_options.MaxInputChars}.",
                _options.MaxInputChars);
        if (string.IsNullOrWhiteSpace(queryString))
            return new EmptyQuerySyntax();

        var lexer = new QueryLexer(_options, limitsAreComplexity);
        var syntaxParser = new QuerySyntaxParser(
            _defaultField,
            _options,
            lexer,
            compiler.SyntaxBudget,
            limitsAreComplexity);
        QuerySyntax syntax = syntaxParser.Parse(queryString);
        return compiler.LowerAnalysedSyntax(syntax);
    }

    internal QuerySyntax PrepareSyntax(
        QuerySyntax syntax,
        Action<int, int> consumeAdditionalClauses,
        bool graphPathLimitIsComplexity) =>
        GetCompiler().PrepareSyntax(syntax, consumeAdditionalClauses, graphPathLimitIsComplexity);

    internal Query CompileSyntax(QuerySyntax syntax) =>
        GetCompiler().CompileSyntax(syntax);

    /// <summary>Builds a phrase query from analysed phrase text.</summary>
    protected virtual Query BuildPhraseQuery(string field, string phraseText, int slop) =>
        GetCompiler().BuildPhraseQuery(field, phraseText, slop);

    /// <summary>Builds a phrase query while retaining the raw escaped phrase content.</summary>
    /// <param name="field">The field analysed by the phrase query.</param>
    /// <param name="phraseText">The unescaped phrase content.</param>
    /// <param name="rawPhraseText">The phrase content as it appeared between the quotes.</param>
    /// <param name="slop">The phrase slop.</param>
    /// <returns>The compiled phrase query.</returns>
    private protected virtual Query BuildPhraseQuery(string field, string phraseText, string rawPhraseText, int slop) =>
        BuildPhraseQuery(field, phraseText, slop);

    private protected virtual Query BuildPhraseQuery(
        string field,
        string phraseText,
        string rawPhraseText,
        int slop,
        QuerySourceSpan sourceSpan) =>
        BuildPhraseQuery(field, phraseText, rawPhraseText, slop);

    protected IReadOnlyList<Analysis.Token> AnalyseTerm(string term) => AnalyseTerm(_defaultField, term);

    /// <summary>Analyses a literal query term with the analyser resolved for <paramref name="field"/>.</summary>
    protected IReadOnlyList<Analysis.Token> AnalyseTerm(string field, string term) =>
        AnalyseTermBuffer(field, term);

    private QueryAnalysisTokenBuffer AnalyseTermBuffer(string field, string term)
    {
        var tokens = new QueryAnalysisTokenBuffer();
        ResolveFieldContext(field).QueryAnalyser.Analyse(term.AsSpan(), tokens);
        return tokens;
    }

    /// <summary>Analyses a literal that must remain a single term, such as a wildcard fragment.</summary>
    /// <exception cref="QueryParseException">The analyser emitted more than one token or a graph edge.</exception>
    protected string AnalyseSingleToken(string term) => AnalyseSingleToken(_defaultField, term);

    /// <summary>Analyses a literal using the analyser resolved for <paramref name="field"/>.</summary>
    /// <exception cref="QueryParseException">The analyser emitted more than one token or a graph edge.</exception>
    protected string AnalyseSingleToken(string field, string term) =>
        AnalyseSingleToken(ResolveFieldContext(field).QueryAnalyser, term);

    /// <summary>Analyses one simple complex-phrase slot within the active phrase budgets.</summary>
    private protected string AnalyseComplexPhraseTerm(string field, ReadOnlySpan<char> term, int sourceOffset) =>
        GetCompiler().AnalyseComplexPhraseTerm(field, term, sourceOffset);

    /// <summary>Charges custom complex-phrase slots and alternatives to the active query budgets.</summary>
    private protected void ConsumeComplexPhraseClauses(int clauseCount, int alternativeClauseCount, int sourceOffset) =>
        GetCompiler().ConsumeComplexPhraseClauses(clauseCount, alternativeClauseCount, sourceOffset);

    private static string AnalyseSingleToken(IAnalyser analyser, string term)
    {
        var tokens = new QueryAnalysisTokenBuffer();
        analyser.Analyse(term.AsSpan(), tokens);
        if (tokens.Count == 0)
            return string.Empty;
        if (tokens.Count != 1 || tokens[0].PositionLength != 1)
        {
            throw new QueryParseException(
                "A wildcard or range literal must analyse to at most one unit-length token.", 0);
        }

        return tokens[0].Text;
    }

    internal static Func<string, string> CreateSingleTokenNormaliser(IAnalyser analyser)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        return literal => NormaliseSingleTerm(analyser, literal);
    }

    internal static string NormaliseSingleTerm(IAnalyser analyser, string literal)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        ArgumentNullException.ThrowIfNull(literal);

        if (analyser is not ITermNormaliser normaliser)
        {
            throw new QueryParseException(
                "The configured analyser does not support one-to-one term normalisation required by wildcard and range queries.",
                0);
        }

        if (!normaliser.TryNormalise(literal.AsSpan(), out string normalised) || string.IsNullOrEmpty(normalised))
        {
            throw new QueryParseException(
                "A wildcard or range literal must normalise to exactly one non-empty term.",
                0);
        }

        return normalised;
    }

    internal static string NormaliseMultiTermPattern(string pattern, Func<string, string> normaliseLiteral)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(normaliseLiteral);

        var builder = new System.Text.StringBuilder(pattern.Length);
        var literal = new System.Text.StringBuilder(pattern.Length);

        void FlushLiteral()
        {
            if (literal.Length == 0)
                return;

            builder.Append(EscapeWildcardLiteral(normaliseLiteral(literal.ToString())));
            literal.Clear();
        }

        for (int i = 0; i < pattern.Length;)
        {
            char current = pattern[i];
            if (current == '\\')
            {
                if (i + 1 < pattern.Length)
                {
                    char escaped = pattern[i + 1];
                    if (escaped is '*' or '?' or '\\')
                    {
                        FlushLiteral();
                        builder.Append('\\').Append(escaped);
                    }
                    else
                    {
                        literal.Append(escaped);
                    }
                    i += 2;
                    continue;
                }

                literal.Append('\\');
                i++;
                continue;
            }

            if (current is '*' or '?')
            {
                FlushLiteral();
                builder.Append(current);
                i++;
                continue;
            }

            literal.Append(current);
            i++;
        }

        FlushLiteral();
        return builder.ToString();
    }

    private static string EscapeWildcardLiteral(string literal)
    {
        var escaped = new System.Text.StringBuilder(literal.Length);
        foreach (char character in literal)
        {
            if (character is '*' or '?' or '\\')
                escaped.Append('\\');
            escaped.Append(character);
        }
        return escaped.ToString();
    }

    private QueryFieldCompilationContext ResolveFieldContext(string field) =>
        GetCompiler().ResolveFieldContext(field);

    /// <summary>Normalises a wildcard or prefix term while preserving its operators.</summary>
    protected virtual string AnalyseMultiTerm(string term) => term;

    /// <summary>Normalises one bounded term in a text range query.</summary>
    protected virtual string AnalyseRangeBound(string term) => term;

    internal Func<string, QueryFieldCompilationContext>? FieldContextResolverForCompilation => _fieldContextResolver;
    internal IAnalyser CurrentAnalyserForCompilation => Analyser;

    internal QueryAnalysisTokenBuffer AnalyseTermForCompilation(string field, string term) =>
        AnalyseTermBuffer(field, term);

    internal string AnalyseMultiTermLiteralForCompilation(string pattern) =>
        AnalyseMultiTerm(pattern);

    internal string AnalyseRangeBoundLiteralForCompilation(string term) =>
        AnalyseRangeBound(term);

    internal Query BuildPhraseQueryForCompilation(
        string field,
        string phraseText,
        string rawPhraseText,
        int slop,
        QuerySourceSpan sourceSpan) =>
        BuildPhraseQuery(field, phraseText, rawPhraseText, slop, sourceSpan);

    internal Query BuildStandardPhraseQueryForCompilation(string field, string phraseText, int slop, int sourceOffset) =>
        GetCompiler().BuildPhraseQuery(field, phraseText, slop, sourceOffset);

    private QueryCompiler GetCompiler() =>
        _compiler ??= new QueryCompiler(this, _options, parseLimitsAreComplexity: false);

}

/// <summary>Exception thrown when a query string cannot be parsed.</summary>
public sealed class QueryParseException : FormatException
{
    /// <summary>Gets the zero-based UTF-16 code-unit offset within the original query string where the error was detected.</summary>
    public int Offset { get; }

    internal bool HasOffset { get; }

    /// <summary>Initialises a new <see cref="QueryParseException"/> with the supplied message.</summary>
    /// <param name="message">Description of the parse error.</param>
    public QueryParseException(string message) : base(message)
    {
    }

    /// <summary>Initialises a new <see cref="QueryParseException"/> with the supplied message and UTF-16 code-unit offset.</summary>
    /// <param name="message">Description of the parse error.</param>
    /// <param name="offset">Zero-based UTF-16 code-unit offset within the original query string where the error was detected.</param>
    public QueryParseException(string message, int offset) : base(message)
    {
        Offset = offset;
        HasOffset = true;
    }
}
