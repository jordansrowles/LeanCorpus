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
    private static void AssertDoesNotReference(string project, params string[] forbiddenProjects)
    {
        string[] references = GetProjectReferences(project);
        foreach (string forbidden in forbiddenProjects)
            Assert.DoesNotContain(references, reference => reference.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] GetProjectReferences(string project) => LoadProject(project)
        .Descendants("ProjectReference")
        .Select(static reference => (string?)reference.Attribute("Include"))
        .OfType<string>()
        .ToArray();

    private static XDocument LoadProject(string project) => XDocument.Load(RepositoryPaths.FromRoot(
        "src", "server", project, $"{project}.csproj"));
}
