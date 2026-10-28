using System.Reflection;
using System.Xml.Linq;
using Rowles.LeanCorpus.OrchardCore.Search;

namespace Rowles.LeanCorpus.OrchardCore.Search.Tests;

[Trait("Area", "Orchard")]
public sealed class DependencyIsolationTests
{
    [Fact]
    public void ProductionProviderHasNoLuceneOrServerRuntimeDependency()
    {
        Assembly provider = typeof(Startup).Assembly;
        string[] references = provider.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain(references, name => name.StartsWith("Lucene.Net", StringComparison.Ordinal));
        Assert.DoesNotContain("OrchardCore.Lucene", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Rowles.LeanCorpus.Server.", StringComparison.Ordinal));

        string repositoryRoot = FindRepositoryRoot();
        string projectPath = Path.Combine(repositoryRoot, "src/server/Rowles.LeanCorpus.OrchardCore.Search/Rowles.LeanCorpus.OrchardCore.Search.csproj");
        XDocument project = XDocument.Load(projectPath);
        string[] packageReferences = project.Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(packageReferences, name => name.StartsWith("Lucene", StringComparison.Ordinal));
        Assert.DoesNotContain("OrchardCore.Lucene", packageReferences);
        Assert.DoesNotContain(packageReferences, name => name.StartsWith("Rowles.LeanCorpus.Server.", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")) &&
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "Rowles.LeanCorpus.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find the LeanCorpus repository root from the test output directory.");
    }
}
