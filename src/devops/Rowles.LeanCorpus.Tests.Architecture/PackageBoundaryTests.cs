using System.Xml.Linq;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void LeanCorpus_must_not_reference_Rowles_Text()
    {
        bool hasReference = ArchitectureContext.CoreAssembly
            .GetReferencedAssemblies()
            .Any(static assembly => string.Equals(assembly.Name, "Rowles.Text", StringComparison.Ordinal));

        Assert.False(hasReference, "LeanCorpus must source-include Analysis and must not reference Rowles.Text.");
    }

    [Fact]
    public void Shipping_projects_must_not_reference_test_or_development_projects()
    {
        string[] shippingProjects =
        [
            "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj",
            "src/core/Rowles.Text/Rowles.Text.csproj",
            "src/core/Rowles.LeanCorpus.SourceGen/Rowles.LeanCorpus.SourceGen.csproj",
            "src/core/Rowles.LeanCorpus.Compression.LZ4/Rowles.LeanCorpus.Compression.LZ4.csproj",
            "src/core/Rowles.LeanCorpus.Compression.Snappy/Rowles.LeanCorpus.Compression.Snappy.csproj",
            "src/core/Rowles.LeanCorpus.Compression.Zstandard/Rowles.LeanCorpus.Compression.Zstandard.csproj",
            "src/devops/Rowles.LeanCorpus.Cli/Rowles.LeanCorpus.Cli.csproj",
            "src/server/Rowles.LeanCorpus.Server.Abstractions/Rowles.LeanCorpus.Server.Abstractions.csproj",
            "src/server/Rowles.LeanCorpus.Server.Core/Rowles.LeanCorpus.Server.Core.csproj",
            "src/server/Rowles.LeanCorpus.Server.AspNetCore/Rowles.LeanCorpus.Server.AspNetCore.csproj",
            "src/server/Rowles.LeanCorpus.Server.Grpc/Rowles.LeanCorpus.Server.Grpc.csproj",
            "src/server/Rowles.LeanCorpus.Studio/Rowles.LeanCorpus.Studio.csproj",
            "src/server/Rowles.LeanCorpus.Server.Local/Rowles.LeanCorpus.Server.Local.csproj",
        ];

        var failures = shippingProjects
            .SelectMany(project => GetProjectReferenceIncludes(project).Select(reference => (project, reference)))
            .Where(static pair => IsTestOrDevelopmentProject(pair.project, pair.reference))
            .Select(static pair => $"{pair.project} -> {pair.reference}");

        RuleAssert.Empty("Shipping projects must not reference devops, test, benchmark, profiling or example projects:", failures);
    }

    [Fact]
    public void LeanCorpus_must_remain_independent_of_optional_implementations()
    {
        var references = GetProjectReferences("src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");

        Assert.DoesNotContain(references, static reference => reference.Contains("Rowles.Text", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, static reference => reference.Contains("SourceGen", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, static reference => reference.Contains("Compression.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, static reference => reference.Contains("Server", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LeanCorpus_must_not_gain_runtime_package_dependencies()
    {
        XDocument project = LoadProject("src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        var runtimePackages = project.Descendants("PackageReference")
            .Select(static reference => (string?)reference.Attribute("Include") ?? "unnamed package");

        RuleAssert.Empty("LeanCorpus must not gain direct package references:", runtimePackages);
    }

    [Fact]
    public void Rowles_Text_must_not_gain_runtime_package_dependencies()
    {
        XDocument project = LoadProject("src/core/Rowles.Text/Rowles.Text.csproj");
        var runtimePackages = project.Descendants("PackageReference")
            .Select(static reference => (string?)reference.Attribute("Include") ?? "unnamed package");

        RuleAssert.Empty("Rowles.Text must not gain direct package references:", runtimePackages);
    }

    [Fact]
    public void Compression_plugins_must_depend_on_Core_and_their_declared_implementations()
    {
        (string project, string implementation)[] plugins =
        [
            ("src/core/Rowles.LeanCorpus.Compression.LZ4/Rowles.LeanCorpus.Compression.LZ4.csproj", "K4os.Compression.LZ4"),
            ("src/core/Rowles.LeanCorpus.Compression.Snappy/Rowles.LeanCorpus.Compression.Snappy.csproj", "Snappier"),
            ("src/core/Rowles.LeanCorpus.Compression.Zstandard/Rowles.LeanCorpus.Compression.Zstandard.csproj", "ZstdSharp.Port"),
        ];

        foreach ((string projectPath, string implementation) in plugins)
        {
            XDocument project = LoadProject(projectPath);
            string[] projectReferences = GetProjectReferences(projectPath);
            Assert.Equal(["src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj"], projectReferences);

            string[] packageReferences = project.Descendants("PackageReference")
                .Select(static reference => (string?)reference.Attribute("Include"))
                .OfType<string>()
                .ToArray();
            Assert.Equal([implementation], packageReferences);
        }
    }

    [Fact]
    public void Deliberately_AOT_compatible_projects_must_remain_so()
    {
        string[] projects =
        [
            "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj",
            "src/core/Rowles.Text/Rowles.Text.csproj",
            "src/core/Rowles.LeanCorpus.Compression.LZ4/Rowles.LeanCorpus.Compression.LZ4.csproj",
            "src/core/Rowles.LeanCorpus.Compression.Snappy/Rowles.LeanCorpus.Compression.Snappy.csproj",
            "src/core/Rowles.LeanCorpus.Compression.Zstandard/Rowles.LeanCorpus.Compression.Zstandard.csproj",
            "src/server/Rowles.LeanCorpus.Server.Abstractions/Rowles.LeanCorpus.Server.Abstractions.csproj",
            "src/server/Rowles.LeanCorpus.Server.Core/Rowles.LeanCorpus.Server.Core.csproj",
        ];

        var failures = projects
            .Where(project => !string.Equals(GetProperty(LoadProject(project), "IsAotCompatible"), "true", StringComparison.OrdinalIgnoreCase));

        RuleAssert.Empty("These projects deliberately promise Native AOT compatibility:", failures);
    }

    [Fact]
    public void Project_wiring_must_keep_the_two_assemblies_independent()
    {
        string root = RepositoryPaths.Root;
        XDocument leanCorpus = LoadProject("src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj");
        XDocument rowlesText = LoadProject("src/core/Rowles.Text/Rowles.Text.csproj");

        Assert.False(
            Directory.Exists(Path.Combine(root, "src", "core", "Rowles.LeanCorpus", "Analysis")),
            "LeanCorpus must not retain a second Analysis source tree.");

        var leanCompileIncludes = leanCorpus.Descendants("Compile")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => include is not null)
            .Select(static include => include!.Replace('\\', '/'))
            .ToArray();
        Assert.Contains(
            leanCompileIncludes,
            static include => include.EndsWith("../Rowles.Text/Analysis/**/*.cs", StringComparison.Ordinal));

        var leanProjectReferences = leanCorpus.Descendants("ProjectReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => include is not null)
            .ToArray();
        Assert.DoesNotContain(
            leanProjectReferences,
            static include => include!.Contains("Rowles.Text", StringComparison.OrdinalIgnoreCase));

        var textProjectReferences = rowlesText.Descendants("ProjectReference")
            .Select(static element => (string?)element.Attribute("Include"))
            .Where(static include => include is not null)
            .ToArray();
        Assert.DoesNotContain(
            textProjectReferences,
            static include => include!.Contains("Rowles.LeanCorpus", StringComparison.OrdinalIgnoreCase));

        string constants = string.Join(';', rowlesText.Descendants("DefineConstants").Select(static element => element.Value));
        Assert.Contains("ROWLES_TEXT", constants, StringComparison.Ordinal);
    }

    private static XDocument LoadProject(string relativePath) => XDocument.Load(Path.Combine(
        RepositoryPaths.Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string[] GetProjectReferences(string relativePath) => LoadProject(relativePath)
        .Descendants("ProjectReference")
        .Select(static element => (string?)element.Attribute("Include"))
        .OfType<string>()
        .Select(reference => ToRepositoryRelativePath(relativePath, reference))
        .ToArray();

    private static string[] GetProjectReferenceIncludes(string relativePath) => LoadProject(relativePath)
        .Descendants("ProjectReference")
        .Select(static element => (string?)element.Attribute("Include"))
        .OfType<string>()
        .ToArray();

    private static string? GetProperty(XDocument project, string name) => project.Descendants(name)
        .Select(static element => element.Value.Trim())
        .SingleOrDefault();

    private static bool IsTestOrDevelopmentProject(string projectPath, string reference)
    {
        string target = ToRepositoryRelativePath(projectPath, reference);
        return target.StartsWith("src/devops/", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(target, "src/devops/Rowles.LeanCorpus.Cli/Rowles.LeanCorpus.Cli.csproj", StringComparison.OrdinalIgnoreCase) ||
               target.StartsWith("src/examples/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRepositoryRelativePath(string projectPath, string reference)
    {
        string projectDirectory = Path.GetDirectoryName(Path.Combine(RepositoryPaths.Root, projectPath))!;
        string normalisedReference = reference.Replace('\\', Path.DirectorySeparatorChar);
        string target = Path.GetFullPath(Path.Combine(projectDirectory, normalisedReference));
        return Path.GetRelativePath(RepositoryPaths.Root, target).Replace(Path.DirectorySeparatorChar, '/');
    }
}
