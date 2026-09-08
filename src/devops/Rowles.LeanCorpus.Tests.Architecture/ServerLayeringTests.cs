using System.Xml.Linq;
using Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Architecture;

public sealed class ServerLayeringTests
{
    [Fact]
    public void Server_Abstractions_must_not_reference_concrete_layers() =>
        AssertDoesNotReference("Rowles.LeanCorpus.Server.Abstractions", "Rowles.LeanCorpus.Server.Core", "Rowles.LeanCorpus.Server.AspNetCore", "Rowles.LeanCorpus.Server.Grpc", "Rowles.LeanCorpus.Studio", "Rowles.LeanCorpus.Server.Local");

    [Fact]
    public void Server_Core_must_not_reference_transports_or_composition_layers() =>
        AssertDoesNotReference("Rowles.LeanCorpus.Server.Core", "Rowles.LeanCorpus.Server.AspNetCore", "Rowles.LeanCorpus.Server.Grpc", "Rowles.LeanCorpus.Studio", "Rowles.LeanCorpus.Server.Local");

    [Fact]
    public void Reusable_Server_layers_must_not_reference_Studio()
    {
        AssertDoesNotReference("Rowles.LeanCorpus.Server.Abstractions", "Rowles.LeanCorpus.Studio");
        AssertDoesNotReference("Rowles.LeanCorpus.Server.Core", "Rowles.LeanCorpus.Studio");
        AssertDoesNotReference("Rowles.LeanCorpus.Server.AspNetCore", "Rowles.LeanCorpus.Studio");
        AssertDoesNotReference("Rowles.LeanCorpus.Server.Grpc", "Rowles.LeanCorpus.Studio");
    }

    [Fact]
    public void Server_Local_remains_the_composition_root()
    {
        string[] references = GetProjectReferences("Rowles.LeanCorpus.Server.Local");

        Assert.Contains(references, static reference => reference.Contains("Rowles.LeanCorpus.Server.Core", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, static reference => reference.Contains("Rowles.LeanCorpus.Server.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, static reference => reference.Contains("Rowles.LeanCorpus.Server.Grpc", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, static reference => reference.Contains("Rowles.LeanCorpus.Studio", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertDoesNotReference(string project, params string[] forbiddenProjects)
    {
        string[] references = GetProjectReferences(project);
        foreach (string forbidden in forbiddenProjects)
            Assert.DoesNotContain(references, reference => reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetProjectReferences(string project) => XDocument.Load(RepositoryPaths.FromRoot(
            "src", "server", project, $"{project}.csproj"))
        .Descendants("ProjectReference")
        .Select(static reference => (string?)reference.Attribute("Include"))
        .OfType<string>()
        .ToArray();
}
