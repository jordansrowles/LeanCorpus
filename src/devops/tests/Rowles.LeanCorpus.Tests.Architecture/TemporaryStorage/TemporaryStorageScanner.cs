using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rowles.LeanCorpus.Tests.Architecture.TemporaryStorage;

/// <summary>One direct selection of operating-system temporary storage.</summary>
internal sealed record TemporaryStorageViolation(string Path, int Line, string Api, string Member)
{
    public override string ToString() => $"{Path}:{Line}: {Api} ({Member})";
}

/// <summary>
/// Finds direct selections of operating-system temporary storage in C# source (ADR046):
/// <c>Path.GetTempPath</c>, <c>Path.GetTempFileName</c>, <c>Directory.CreateTempSubdirectory</c>
/// and reads of the <c>TMP</c>, <c>TEMP</c> and <c>TMPDIR</c> environment variables.
/// </summary>
internal static class TemporaryStorageScanner
{
    private static readonly HashSet<string> TemporaryMethods = new(StringComparer.Ordinal)
    {
        "GetTempPath", "GetTempFileName", "CreateTempSubdirectory"
    };

    private static readonly HashSet<string> TemporaryVariables = new(StringComparer.Ordinal) { "TMP", "TEMP", "TMPDIR" };

    private static readonly HashSet<string> FileSystemTypes = new(StringComparer.Ordinal)
    {
        "System.IO.Path", "System.IO.Directory"
    };

    public static IReadOnlyList<TemporaryStorageViolation> Scan(string relativePath, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, path: relativePath).GetCompilationUnitRoot();
        var receivers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Path", "Directory", "System.IO.Path", "System.IO.Directory", "global::System.IO.Path", "global::System.IO.Directory",
            "IO.Path", "IO.Directory"
        };
        bool staticPath = false;
        foreach (var directive in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            string target = Normalise(directive.NamespaceOrType.ToString());
            if (directive.Alias is not null && FileSystemTypes.Contains(target))
                receivers.Add(directive.Alias.Name.Identifier.ValueText);
            if (!directive.StaticKeyword.IsKind(SyntaxKind.None) && FileSystemTypes.Contains(target))
                staticPath = true;
        }

        var violations = new List<TemporaryStorageViolation>();
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? api = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member when TemporaryMethods.Contains(member.Name.Identifier.ValueText)
                    && receivers.Contains(Normalise(member.Expression.ToString()))
                    => string.Concat(member.Expression.ToString(), ".", member.Name.Identifier.ValueText),
                IdentifierNameSyntax identifier when staticPath && TemporaryMethods.Contains(identifier.Identifier.ValueText)
                    => identifier.Identifier.ValueText,
                MemberAccessExpressionSyntax member when member.Name.Identifier.ValueText == "GetEnvironmentVariable"
                    && Normalise(member.Expression.ToString()) is "Environment" or "System.Environment"
                    && FirstStringArgument(invocation) is { } variable && TemporaryVariables.Contains(variable)
                    => string.Concat("Environment.GetEnvironmentVariable(\"", variable, "\")"),
                _ => null
            };
            if (api is null)
                continue;

            int line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            violations.Add(new TemporaryStorageViolation(relativePath, line, api, EnclosingMember(invocation)));
        }

        return violations;
    }

    private static string Normalise(string text)
    {
        string compact = string.Concat(text.Where(static character => !char.IsWhiteSpace(character)));
        return compact.StartsWith("global::", StringComparison.Ordinal) ? compact["global::".Length..] : compact;
    }

    private static string? FirstStringArgument(InvocationExpressionSyntax invocation)
        => invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
           && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    /// <summary>Returns <c>Type.Member</c> for the innermost named member containing the node.</summary>
    internal static string EnclosingMember(SyntaxNode node)
    {
        string member = node.Ancestors().Select(static ancestor => ancestor switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            LocalFunctionStatementSyntax local => local.Identifier.ValueText,
            ConstructorDeclarationSyntax => ".ctor",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.ValueText,
            AccessorDeclarationSyntax => null,
            _ => null
        }).FirstOrDefault(static name => name is not null) ?? "<top-level>";

        // Local functions report their containing method, so registers stay stable.
        var outer = node.Ancestors().OfType<LocalFunctionStatementSyntax>().Any()
            ? node.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault() switch
            {
                MethodDeclarationSyntax method => method.Identifier.ValueText,
                ConstructorDeclarationSyntax => ".ctor",
                _ => member
            }
            : member;
        string type = string.Join(".", node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(static type => type.Identifier.ValueText));
        return type.Length == 0 ? outer : string.Concat(type, ".", outer);
    }
}
