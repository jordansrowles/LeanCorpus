using System.IO.Compression;
using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageArchiveInspectorTests
{
    [Fact]
    public void AcceptsPolicyCompliantLibraryArchive()
    {
        using var package = CreatePackage(addUnexpectedAsset: false);

        PackageArchiveInspector.Validate(package.Path, null, package.Policy, package.Project, []);
    }

    [Fact]
    public void RejectsUnexpectedLibraryAssets()
    {
        using var package = CreatePackage(addUnexpectedAsset: true);

        var exception = Assert.Throws<InvalidDataException>(() =>
            PackageArchiveInspector.Validate(package.Path, null, package.Policy, package.Project, []));

        Assert.Contains("Unexpected library asset", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiresTheSupportedCoreRangeForCompressionPackages()
    {
        using var package = CreateCompressionPackage("[3.0.0,4.0.0)");

        var exception = Assert.Throws<InvalidDataException>(() =>
            PackageArchiveInspector.ValidateCompressionDependencyRange(package.Path, package.Policy));

        Assert.Contains("LeanCorpus [4.0.0,5.0.0)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsTheSupportedCoreRangeForCompressionPackages()
    {
        using var package = CreateCompressionPackage("[4.0.0,5.0.0)");

        PackageArchiveInspector.ValidateCompressionDependencyRange(package.Path, package.Policy);
    }

    private static TemporaryPackage CreatePackage(bool addUnexpectedAsset)
    {
        var directory = Path.Combine(Path.GetTempPath(), "leancorpus-package-inspector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Example.Package.1.0.0.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddEntry(archive, "Example.Package.nuspec", """
                <?xml version="1.0"?>
                <package><metadata>
                  <id>Example.Package</id><version>1.0.0</version>
                  <repository type="git" url="https://github.com/example/repo" />
                </metadata></package>
                """);
            AddEntry(archive, "lib/net10.0/Example.dll", "binary");
            AddEntry(archive, "lib/net10.0/Example.xml", "docs");
            if (addUnexpectedAsset)
            {
                AddEntry(archive, "lib/net10.0/Unexpected.dll", "binary");
            }
        }

        return new TemporaryPackage(
            path,
            new PackagePolicyEntry("Example.Package", "src/Example/Example.csproj", "library", ["net10.0"], ["net10.0"], null, true, false, true, false, [], []),
            new PackageProjectMetadata("Example.Package", "1.0.0", "Example", null, null, "https://github.com/example/repo"),
            directory);
    }

    private static TemporaryPackage CreateCompressionPackage(string dependencyRange)
    {
        var directory = Path.Combine(Path.GetTempPath(), "leancorpus-compression-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "LeanCorpus.Compression.LZ4.1.0.1.nupkg");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            AddEntry(archive, "LeanCorpus.Compression.LZ4.nuspec", $"""
                <?xml version="1.0"?>
                <package><metadata>
                  <id>LeanCorpus.Compression.LZ4</id><version>1.0.1</version>
                  <dependencies><group targetFramework=".NETCoreApp,Version=v10.0"><dependency id="LeanCorpus" version="{dependencyRange}" /></group></dependencies>
                </metadata></package>
                """);
        }

        return new TemporaryPackage(
            path,
            new PackagePolicyEntry("LeanCorpus.Compression.LZ4", "src/LZ4/LZ4.csproj", "library", ["net10.0"], ["net10.0"], null, true, false, true, true, ["compression"], []),
            new PackageProjectMetadata("LeanCorpus.Compression.LZ4", "1.0.1", "LZ4", null, null, "https://github.com/example/repo"),
            directory);
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private sealed class TemporaryPackage(string path, PackagePolicyEntry policy, PackageProjectMetadata project, string directory) : IDisposable
    {
        public string Path { get; } = path;

        public PackagePolicyEntry Policy { get; } = policy;

        public PackageProjectMetadata Project { get; } = project;

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
