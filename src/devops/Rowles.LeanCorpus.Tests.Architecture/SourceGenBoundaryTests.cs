using System.Xml.Linq;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class SourceGenBoundaryTests
{
    [Fact]
    public void SourceGen_must_remain_an_isolated_Roslyn_component()
    {
        XDocument project = LoadProject();

        Assert.Equal("netstandard2.0", GetProperty(project, "TargetFramework"));
        Assert.Equal("true", GetProperty(project, "IsRoslynComponent"));
        Assert.Equal("false", GetProperty(project, "IncludeBuildOutput"));
        Assert.Equal("true", GetProperty(project, "DevelopmentDependency"));

        var references = project.Descendants("ProjectReference")
            .Select(static reference => (string?)reference.Attribute("Include"))
            .OfType<string>();
        Assert.DoesNotContain(references, static reference => reference.Contains("Rowles.LeanCorpus", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, static reference => reference.Contains("Rowles.Text", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, static reference => reference.Contains("Server", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SourceGen_must_package_its_analyser_assembly_correctly()
    {
        XDocument project = LoadProject();
        XElement? roslyn = project.Descendants("PackageReference")
            .SingleOrDefault(static reference => string.Equals((string?)reference.Attribute("Include"), "Microsoft.CodeAnalysis.CSharp", StringComparison.Ordinal));
        Assert.NotNull(roslyn);
        Assert.Equal("all", (string?)roslyn.Attribute("PrivateAssets"), ignoreCase: true);

        Assert.Contains(project.Descendants("None"), static item =>
            string.Equals(((string?)item.Attribute("Include"))?.Replace('\\', '/'), "$(OutputPath)/$(AssemblyName).dll", StringComparison.Ordinal) &&
            string.Equals((string?)item.Attribute("Pack"), "true", StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string?)item.Attribute("PackagePath"), "analyzers/dotnet/cs", StringComparison.Ordinal));
    }

    private static XDocument LoadProject() => XDocument.Load(RepositoryPaths.FromRoot(
        "src", "core", "Rowles.LeanCorpus.SourceGen", "Rowles.LeanCorpus.SourceGen.csproj"));

    private static string? GetProperty(XDocument project, string name) => project.Descendants(name)
        .Select(static property => property.Value.Trim())
        .SingleOrDefault();
}
