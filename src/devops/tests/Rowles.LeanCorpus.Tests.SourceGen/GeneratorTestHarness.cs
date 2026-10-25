using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rowles.LeanCorpus.SourceGen;

namespace Rowles.LeanCorpus.Tests.SourceGen;

internal static class GeneratorTestHarness
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> CachedReferences = new(BuildReferences);

    public static GeneratorRunResult Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName: "Tests.SourceGen.Driver",
            syntaxTrees: new[] { syntaxTree },
            references: CachedReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var generator = new LeanDocumentGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees[0].Options);

        var updated = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var outputDiagnostics);
        var runResult = updated.GetRunResult();
        var generatedSources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(g => g.SourceText.ToString())
            .ToArray();

        var allDiagnostics = outputDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .ToArray();

        return new GeneratorRunResult(generatedSources, allDiagnostics, outputCompilation);
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var refs = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic) continue;
            string? location;
            try { location = asm.Location; } catch { continue; }
            if (string.IsNullOrEmpty(location)) continue;
            if (!seen.Add(location)) continue;
            try { refs.Add(MetadataReference.CreateFromFile(location)); } catch { }
        }

        // Pull in the .NET reference assemblies that ship next to System.Private.CoreLib.
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrEmpty(tpa))
        {
            foreach (var path in tpa!.Split(Path.PathSeparator))
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (!seen.Add(path)) continue;
                try { refs.Add(MetadataReference.CreateFromFile(path)); } catch { }
            }
        }

        return refs;
    }
}

internal sealed record GeneratorRunResult(
    IReadOnlyList<string> GeneratedSources,
    IReadOnlyList<Diagnostic> Diagnostics,
    Compilation Compilation)
{
    public string CombinedSource => string.Join(Environment.NewLine + "// ----" + Environment.NewLine, GeneratedSources);

    public IReadOnlyList<Diagnostic> GeneratorDiagnostics => Diagnostics
        .Where(d => d.Id.StartsWith("LCGEN", StringComparison.Ordinal))
        .ToArray();

    public IReadOnlyList<Diagnostic> CompilationErrors => Diagnostics
        .Where(d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("LCGEN", StringComparison.Ordinal))
        .ToArray();
}
