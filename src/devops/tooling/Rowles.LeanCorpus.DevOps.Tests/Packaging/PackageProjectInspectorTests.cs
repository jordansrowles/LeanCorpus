using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageProjectInspectorTests
{
    [Fact]
    public void ReadsPackageMetadataAndShippingDependenciesFromProjectReferences()
    {
        using var repository = new TemporaryRepository();
        var project = repository.WriteProject("src/Example/Example.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <PackageId>Example.Package</PackageId><Version>1.2.3</Version>
                <AssemblyName>Example.Assembly</AssemblyName><PackageReadmeFile>README.md</PackageReadmeFile>
                <PackageLicenseFile>LICENSE</PackageLicenseFile><RepositoryUrl>https://example.test/repo</RepositoryUrl>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Example.Dependency;Example.Another" />
                <PackageReference Include="Private.Dependency" PrivateAssets="all" />
                <PackageReference Update="Updated.Dependency" ExcludeAssets="all" />
                <ProjectReference Include="../Thing/Thing.csproj" />
              </ItemGroup>
            </Project>
            """);
        repository.WriteProject("src/Thing/Thing.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><PackageId>Thing.Package</PackageId></PropertyGroup>
            </Project>
            """);

        var metadata = PackageProjectInspector.ReadMetadata(project);
        var dependencies = PackageProjectInspector.ReadDependencyIds(project, repository.Root);

        Assert.Equal(new PackageProjectMetadata(
            "Example.Package", "1.2.3", "Example.Assembly", "README.md", "LICENSE", "https://example.test/repo"), metadata);
        Assert.Equal(["Example.Another", "Example.Dependency", "Thing.Package"], dependencies);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), "leancorpus-package-project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string WriteProject(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
