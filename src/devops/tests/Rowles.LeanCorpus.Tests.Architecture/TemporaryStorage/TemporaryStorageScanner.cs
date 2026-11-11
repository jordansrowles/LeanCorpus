using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rowles.LeanCorpus.Tests.Architecture.TemporaryStorage;

/// <summary>One direct selection of operating-system temporary storage.</summary>
internal sealed record TemporaryStorageViolation(string Path, int Line, string Api, string Member)
{
    public override string ToString() => $"{Path}:{Line}: {Api} ({Member})";
}

/// <summary>One C# source file of a project.</summary>
internal sealed record ScannedSource(string Path, string Text);

/// <summary>
/// Finds direct selections of operating-system temporary storage in C# source (ADR046):
/// <c>Path.GetTempPath</c>, <c>Path.GetTempFileName</c>, <c>Directory.CreateTempSubdirectory</c>
/// and reads of the <c>TMP</c>, <c>TEMP</c> and <c>TMPDIR</c> environment variables. Calls are bound
/// with a Roslyn semantic model per project, so aliases, namespace aliases and global or static
/// usings in any file are resolved. A call that cannot be bound falls back to a syntactic match, so
/// the scanner errs towards reporting.
/// </summary>
internal static class TemporaryStorageScanner
{
    private static readonly Dictionary<string, HashSet<string>> TemporaryMethods = new(StringComparer.Ordinal)
    {
        ["System.IO.Path"] = new(StringComparer.Ordinal) { "GetTempPath", "GetTempFileName" },
        ["System.IO.Directory"] = new(StringComparer.Ordinal) { "CreateTempSubdirectory" }
    };

    private static readonly HashSet<string> TemporaryMethodNames = new(StringComparer.Ordinal)
    {
        "GetTempPath", "GetTempFileName", "CreateTempSubdirectory"
    };

    private static readonly HashSet<string> TemporaryVariables = new(StringComparer.Ordinal) { "TMP", "TEMP", "TMPDIR" };

    /// <summary>The SDK's implicit global usings for <c>Microsoft.NET.Sdk</c> projects.</summary>
    internal static readonly string[] ImplicitUsings =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"
    ];

    private static readonly Lazy<IReadOnlyList<MetadataReference>> FrameworkReferences = new(LoadFrameworkReferences);

    /// <summary>Scans one project's sources together, with the given extra global usings.</summary>
    public static IReadOnlyList<TemporaryStorageViolation> ScanProject(
        IReadOnlyList<ScannedSource> sources,
        IEnumerable<string> globalUsings,
        IEnumerable<string>? globalStaticUsings = null)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = sources.Select(source => CSharpSyntaxTree.ParseText(source.Text, options, source.Path)).ToList();
        string usingText = string.Concat(
            globalUsings.Distinct(StringComparer.Ordinal).Select(static name => $"global using {name};\n"),
            string.Concat((globalStaticUsings ?? []).Distinct(StringComparer.Ordinal).Select(static name => $"global using static {name};\n")));
        var usingTree = CSharpSyntaxTree.ParseText(usingText, options, "<global usings>");
        var compilation = CSharpCompilation.Create(
            "TemporaryStorageScan",
            [.. trees, usingTree],
            FrameworkReferences.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        var violations = new List<TemporaryStorageViolation>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string? api = Classify(model, invocation);
                if (api is null)
                    continue;
                int line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                violations.Add(new TemporaryStorageViolation(tree.FilePath, line, api, EnclosingMember(invocation)));
            }
        }

        return violations;
    }

    /// <summary>Scans one file on its own with the SDK implicit usings, for tests of the scanner.</summary>
    public static IReadOnlyList<TemporaryStorageViolation> Scan(string relativePath, string source)
        => ScanProject([new ScannedSource(relativePath, source)], ImplicitUsings);

    /// <summary>Reads <c>&lt;Using Include="..." /&gt;</c> items, plain and static, from a project file.</summary>
    internal static (IReadOnlyList<string> Usings, IReadOnlyList<string> StaticUsings) ReadProjectUsings(string projectFile)
    {
        var usings = new List<string>(ImplicitUsings);
        var statics = new List<string>();
        foreach (var file in new[] { projectFile }.Concat(DirectoryBuildProps(Path.GetDirectoryName(projectFile)!)))
        {
            foreach (var item in XDocument.Load(file).Descendants().Where(static element => element.Name.LocalName == "Using"))
            {
                string? include = item.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include) || item.Attribute("Alias") is not null)
                    continue;
                if (string.Equals(item.Attribute("Static")?.Value, "true", StringComparison.OrdinalIgnoreCase))
                    statics.Add(include);
                else
                    usings.Add(include);
            }
        }

        return (usings, statics);
    }

    private static IEnumerable<string> DirectoryBuildProps(string directory)
    {
        for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
        {
            string props = Path.Combine(current.FullName, "Directory.Build.props");
            if (File.Exists(props))
                yield return props;
        }
    }

    private static string? Classify(SemanticModel model, InvocationExpressionSyntax invocation)
    {
        var info = model.GetSymbolInfo(invocation);
        var method = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (method is not null)
        {
            string type = method.ContainingType.ToDisplayString();
            if (TemporaryMethods.TryGetValue(type, out var names) && names.Contains(method.Name))
                return string.Concat(type["System.IO.".Length..], ".", method.Name);
            if (type == "System.Environment" && method.Name == "GetEnvironmentVariable"
                && invocation.ArgumentList.Arguments.FirstOrDefault() is { } argument
                && model.GetConstantValue(argument.Expression) is { HasValue: true, Value: string variable }
                && TemporaryVariables.Contains(variable))
                return string.Concat("Environment.GetEnvironmentVariable(\"", variable, "\")");
            return null;
        }

        // Unbound: report a syntactic match rather than miss one.
        string? name = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
        string expression = invocation.Expression.ToString();
        bool mentionsFileSystem = expression.Contains("Path", StringComparison.Ordinal) || expression.Contains("Directory", StringComparison.Ordinal);
        return name is not null && TemporaryMethodNames.Contains(name) && (mentionsFileSystem || invocation.Expression is IdentifierNameSyntax)
            ? string.Concat(expression, " (unbound)")
            : null;
    }

    /// <summary>Returns <c>Type.Member</c> for the innermost named member containing the node.</summary>
    internal static string EnclosingMember(SyntaxNode node)
    {
        string member = node.Ancestors().Select(static ancestor => ancestor switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax => ".ctor",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.ValueText,
            _ => null
        }).FirstOrDefault(static name => name is not null) ?? "<top-level>";
        string type = string.Join(".", node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(static declaration => declaration.Identifier.ValueText));
        return type.Length == 0 ? member : string.Concat(type, ".", member);
    }

    private static IReadOnlyList<MetadataReference> LoadFrameworkReferences()
    {
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(static path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                || Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
