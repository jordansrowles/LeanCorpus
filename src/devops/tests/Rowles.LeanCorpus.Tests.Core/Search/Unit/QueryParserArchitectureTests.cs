using System.Reflection;
using Rowles.LeanCorpus.Search.Parsing;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class QueryParserArchitectureTests
{
    [Fact(DisplayName = "QueryParser: lexing, syntax parsing and query compilation have separate owners")]
    public void ParserStages_HaveSeparateInternalOwners()
    {
        const string parserNamespace = "Rowles.LeanCorpus.Search.Parsing.";
        Assembly assembly = typeof(QueryParser).Assembly;

        Type? lexer = assembly.GetType($"{parserNamespace}QueryLexer");
        Type? syntaxParser = assembly.GetType($"{parserNamespace}QuerySyntaxParser");
        Type? compiler = assembly.GetType($"{parserNamespace}QueryCompiler");

        Assert.NotNull(lexer);
        Assert.NotNull(syntaxParser);
        Assert.NotNull(compiler);
        const BindingFlags internalMethods = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.NotNull(lexer!.GetMethod("Lex", internalMethods));
        Assert.NotNull(syntaxParser!.GetMethod("ParseExpression", internalMethods));
        Assert.NotNull(compiler!.GetMethod("CompileQuery", internalMethods));

        string[] parserMethods = typeof(QueryParser)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(static method => method.Name)
            .ToArray();

        Assert.DoesNotContain("Tokenize", parserMethods);
        Assert.DoesNotContain("ParseExpression", parserMethods);
        Assert.DoesNotContain("LowerAnalysedSyntax", parserMethods);
        Assert.DoesNotContain("Compile", parserMethods);
    }

    [Fact(DisplayName = "QueryLexer: raw text and escape metadata survive operator classification")]
    public void Lexer_PreservesRawEscapesAndDecodedValues()
    {
        var lexer = new QueryLexer(QueryParserOptions.Trusted, limitsAreComplexity: false);

        List<QueryToken> tokens = lexer.Lex("escaped\\*prefix* AND /a\\/b/ \"x\\ y\"");

        Assert.Collection(
            tokens,
            term =>
            {
                Assert.Equal(QueryTokenType.Term, term.Type);
                Assert.Equal("escaped*prefix*", term.Value);
                Assert.Equal("escaped\\*prefix*", term.Raw);
                Assert.True(term.HasEscapes);
                Assert.True(term.HasUnescapedWildcard);
            },
            keyword => Assert.Equal(QueryTokenType.And, keyword.Type),
            regexp =>
            {
                Assert.Equal(QueryTokenType.Regex, regexp.Type);
                Assert.Equal("a/b", regexp.Value);
                Assert.Equal("a\\/b", regexp.Raw);
                Assert.True(regexp.HasEscapes);
            },
            phrase =>
            {
                Assert.Equal(QueryTokenType.Phrase, phrase.Type);
                Assert.Equal("x y", phrase.Value);
                Assert.Equal("x\\ y", phrase.Raw);
                Assert.True(phrase.HasEscapes);
            });
    }
}
