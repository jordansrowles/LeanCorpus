using System.Reflection;
using System.Xml.Linq;

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
    public void Project_wiring_must_keep_the_two_assemblies_independent()
    {
        string root = FindRepositoryRoot();
        XDocument leanCorpus = XDocument.Load(Path.Combine(
            root, "src", "core", "Rowles.LeanCorpus", "Rowles.LeanCorpus.csproj"));
        XDocument rowlesText = XDocument.Load(Path.Combine(
            root, "src", "core", "Rowles.Text", "Rowles.Text.csproj"));

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

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rowles.LeanCorpus.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LeanCorpus repository root.");
    }
}
