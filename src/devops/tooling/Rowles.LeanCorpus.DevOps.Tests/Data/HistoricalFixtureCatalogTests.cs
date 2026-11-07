using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class HistoricalFixtureCatalogTests
{
    [Fact]
    public void LoadsBothPinned311ScenariosFromOneSharedVersionProject()
    {
        string repositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;

        HistoricalFixtureCatalog catalog = HistoricalFixtureCatalog.Load(repositoryRoot);
        HistoricalFixtureVersion version = Assert.Single(catalog.Versions);

        Assert.Equal("3.1.1", version.Version);
        Assert.Equal("LeanCorpus", version.PackageId);
        Assert.Equal("3.1.1", version.PackageVersion);
        Assert.Equal("cf124d5a2b9b3bf623cd69f98ea25c5a2e3f7d67ac9bdb4c017a60a00d00e478", version.PackageSha256);
        Assert.Equal("6a633412002f615c36579490b6f7b5be57e6b237778bb92bd24492b943d6060c", version.PackageAssemblySha256);
        Assert.Equal(new[] { "migration", "sparse-hnsw" }, version.Scenarios.Select(static scenario => scenario.Id).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(repositoryRoot, version.ProjectPath.Replace('/', Path.DirectorySeparatorChar))));

        HistoricalFixtureBranchSnapshot snapshot = Assert.Single(catalog.BranchSnapshots);
        Assert.Equal("pre-v3-bbq-v2", snapshot.Id);
        Assert.Equal("fix/bbq-retrieval-quality", snapshot.SourceBranch);
        Assert.Equal("3dab5b656c0077470cd216646ba085a04fa40ef3", snapshot.SourceCommit);
        Assert.Equal(311031, snapshot.GenerationSeed);
        Assert.Equal(128, snapshot.Dimension);
        Assert.Equal(2, snapshot.BodyVersion);
        Assert.Equal("189d29dbe1fe84cbd5806ab56c32babcada457efdb1c3ff5da9f6530b11f2b9c", snapshot.ArchiveSha256);
    }

    [Fact]
    public void RejectsUnknownVersionAndScenarioWithoutFallingBackToCurrentCore()
    {
        string repositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
        HistoricalFixtureCatalog catalog = HistoricalFixtureCatalog.Load(repositoryRoot);

        Assert.Throws<KeyNotFoundException>(() => catalog.GetVersion("4.0.0"));
        Assert.Throws<KeyNotFoundException>(() => catalog.GetScenario("3.1.1", "current-writers"));
    }

    [Fact]
    public void RejectsVersionProjectThatChangesTheExactPackagePin()
    {
        string repositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
        using var temporary = new TemporaryDirectory();
        string copiedRoot = CopyCatalogFixture(repositoryRoot, temporary.Path);
        string projectPath = Path.Combine(copiedRoot, "src", "devops", "tests", "fixtures", "historical", "versions", "3.1.1", "Generator.csproj");
        string project = File.ReadAllText(projectPath).Replace("Version=\"[3.1.1]\"", "Version=\"3.1.*\"", StringComparison.Ordinal);
        File.WriteAllText(projectPath, project);

        Assert.Throws<InvalidDataException>(() => HistoricalFixtureCatalog.Load(copiedRoot));
    }

    [Fact]
    public void RejectsAnArchiveHashPinThatDoesNotMatchTheCommittedSnapshot()
    {
        string repositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
        using var temporary = new TemporaryDirectory();
        string copiedRoot = CopyCatalogFixture(repositoryRoot, temporary.Path);
        string catalogPath = Path.Combine(copiedRoot, "src", "devops", "tests", "fixtures", "historical", "fixtures.json");
        string catalog = File.ReadAllText(catalogPath).Replace(
            "ecbffc14996b10374beb92d86a71648ad470c0c4c23889647e4950a72574830f",
            new string('0', 64), StringComparison.Ordinal);
        File.WriteAllText(catalogPath, catalog);

        Assert.Throws<InvalidDataException>(() => HistoricalFixtureCatalog.Load(copiedRoot));
    }

    [Fact]
    public void RejectsAPreV3SnapshotHashPinThatDoesNotMatchTheCommittedArchive()
    {
        string repositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
        using var temporary = new TemporaryDirectory();
        string copiedRoot = CopyCatalogFixture(repositoryRoot, temporary.Path);
        string catalogPath = Path.Combine(copiedRoot, "src", "devops", "tests", "fixtures", "historical", "fixtures.json");
        string catalog = File.ReadAllText(catalogPath).Replace(
            "189d29dbe1fe84cbd5806ab56c32babcada457efdb1c3ff5da9f6530b11f2b9c",
            new string('0', 64), StringComparison.Ordinal);
        File.WriteAllText(catalogPath, catalog);

        Assert.Throws<InvalidDataException>(() => HistoricalFixtureCatalog.Load(copiedRoot));
    }

    private static string CopyCatalogFixture(string repositoryRoot, string destinationRoot)
    {
        string copiedRoot = destinationRoot;
        string sourceFixtureTree = Path.Combine(repositoryRoot, "src", "devops", "tests", "fixtures", "historical");
        string destinationFixtureTree = Path.Combine(copiedRoot, "src", "devops", "tests", "fixtures", "historical");
        CopyDirectory(sourceFixtureTree, destinationFixtureTree);

        string sourceIndexRoot = Path.Combine(repositoryRoot, "src", "devops", "tests", "Rowles.LeanCorpus.Tests.Shared", "Fixtures", "Indexes");
        string destinationIndexRoot = Path.Combine(copiedRoot, "src", "devops", "tests", "Rowles.LeanCorpus.Tests.Shared", "Fixtures", "Indexes");
        Directory.CreateDirectory(destinationIndexRoot);
        foreach (string file in Directory.EnumerateFiles(sourceIndexRoot, "*"))
        {
            if (!file.EndsWith(".fixture.b64", StringComparison.Ordinal) &&
                !file.EndsWith(".manifest.json", StringComparison.Ordinal))
                continue;
            File.Copy(file, Path.Combine(destinationIndexRoot, Path.GetFileName(file)));
        }
        return copiedRoot;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-historical-catalog-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
