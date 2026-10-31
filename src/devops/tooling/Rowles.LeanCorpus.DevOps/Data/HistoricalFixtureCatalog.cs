using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record HistoricalFixtureCatalog(
    IReadOnlyList<HistoricalFixtureVersion> Versions,
    IReadOnlyList<HistoricalFixtureBranchSnapshot> BranchSnapshots)
{
    internal const string FixtureTree = "src/devops/tests/fixtures/historical";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static HistoricalFixtureCatalog Load(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string root = Path.GetFullPath(repositoryRoot);
        string catalogPath = Path.Combine(root, FixtureTree.Replace('/', Path.DirectorySeparatorChar), "fixtures.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Historical fixture catalog has an unsupported schema version.");

        HistoricalFixtureCatalogDocument catalog = JsonSerializer.Deserialize<HistoricalFixtureCatalogDocument>(
            document.RootElement.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("Historical fixture catalog is empty.");
        if (catalog.Versions.Count == 0)
            throw new InvalidDataException("Historical fixture catalog has no package versions.");

        var seenVersions = new HashSet<string>(StringComparer.Ordinal);
        foreach (HistoricalFixtureVersion version in catalog.Versions)
        {
            ValidateVersion(root, version);
            if (!seenVersions.Add(version.Version))
                throw new InvalidDataException($"Historical fixture version '{version.Version}' is duplicated.");
        }

        IReadOnlyList<HistoricalFixtureBranchSnapshot> branchSnapshots = catalog.BranchSnapshots ?? [];
        var seenSnapshots = new HashSet<string>(StringComparer.Ordinal);
        foreach (HistoricalFixtureBranchSnapshot snapshot in branchSnapshots)
        {
            ValidateBranchSnapshot(root, snapshot);
            if (!seenSnapshots.Add(snapshot.Id))
                throw new InvalidDataException($"Historical branch snapshot '{snapshot.Id}' is duplicated.");
        }

        return new HistoricalFixtureCatalog(catalog.Versions, branchSnapshots);
    }

    public HistoricalFixtureVersion GetVersion(string version)
        => Versions.SingleOrDefault(item => string.Equals(item.Version, version, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Historical fixture version '{version}' is not catalogued.");

    public HistoricalFixtureScenario GetScenario(string version, string scenario)
    {
        HistoricalFixtureVersion definition = GetVersion(version);
        return definition.Scenarios.SingleOrDefault(item => string.Equals(item.Id, scenario, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Historical fixture scenario '{scenario}' is not catalogued for version '{version}'.");
    }

    internal static string ResolveCatalogPath(string repositoryRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') ||
            relativePath.Split('/').Any(static part => part is "" or "." or ".."))
            throw new InvalidDataException($"Historical fixture catalog path '{relativePath}' is unsafe.");
        return Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void ValidateVersion(string repositoryRoot, HistoricalFixtureVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.Version) || version.PackageVersion != version.Version ||
            string.IsNullOrWhiteSpace(version.PackageId) || string.IsNullOrWhiteSpace(version.TargetFramework) ||
            string.IsNullOrWhiteSpace(version.SdkVersion) || string.IsNullOrWhiteSpace(version.AssemblyVersion) ||
            !IsSha256(version.PackageSha256) || !IsSha256(version.PackageAssemblySha256) || version.Scenarios.Count == 0)
            throw new InvalidDataException($"Historical fixture version '{version.Version}' has incomplete or invalid package pins.");

        string projectPath = ResolveCatalogPath(repositoryRoot, version.ProjectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string project = File.ReadAllText(projectPath);
        XDocument projectDocument = XDocument.Load(projectPath);
        XElement[] packageReferences = projectDocument.Descendants("PackageReference").ToArray();
        if (packageReferences.Length != 1 ||
            packageReferences[0].Attribute("Include")?.Value != version.PackageId ||
            packageReferences[0].Attribute("Version")?.Value != $"[{version.PackageVersion}]" ||
            projectDocument.Descendants("ProjectReference").Any() ||
            !project.Contains("../../source/Program.cs", StringComparison.Ordinal) ||
            projectDocument.Descendants("Compile").Count() < 4)
            throw new InvalidDataException($"Historical project '{version.ProjectPath}' does not use the exact package and shared generator source.");

        string lockPath = Path.Combine(projectDirectory, "packages.lock.json");
        using JsonDocument lockDocument = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        string framework = version.TargetFramework;
        JsonElement frameworkRoot = lockDocument.RootElement.GetProperty("dependencies").GetProperty(framework);
        if (!frameworkRoot.TryGetProperty(version.PackageId, out JsonElement packageLock) ||
            packageLock.GetProperty("type").GetString() != "Direct" ||
            packageLock.GetProperty("requested").GetString() != $"[{version.PackageVersion}, {version.PackageVersion}]" ||
            packageLock.GetProperty("resolved").GetString() != version.PackageVersion)
            throw new InvalidDataException($"Historical package lock does not pin {version.PackageId} {version.PackageVersion} exactly.");

        string sdkPath = Path.Combine(projectDirectory, "global.json");
        using JsonDocument sdk = JsonDocument.Parse(File.ReadAllBytes(sdkPath));
        if (sdk.RootElement.GetProperty("sdk").GetProperty("version").GetString() != version.SdkVersion ||
            sdk.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString() != "disable")
            throw new InvalidDataException($"Historical project SDK must be pinned to {version.SdkVersion} without roll-forward.");

        string propsPath = Path.Combine(repositoryRoot, FixtureTree.Replace('/', Path.DirectorySeparatorChar), "Directory.Build.props");
        string props = File.ReadAllText(propsPath);
        if (!props.Contains("<ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>", StringComparison.Ordinal) ||
            !props.Contains("<ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>", StringComparison.Ordinal) ||
            !props.Contains("<ImportDirectoryPackagesProps>false</ImportDirectoryPackagesProps>", StringComparison.Ordinal) ||
            !props.Contains("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>", StringComparison.Ordinal))
            throw new InvalidDataException("Historical fixture projects must opt out of repository build and central package configuration.");

        var seenScenarios = new HashSet<string>(StringComparer.Ordinal);
        foreach (HistoricalFixtureScenario scenario in version.Scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.Id) || scenario.Outputs.Count == 0 || !seenScenarios.Add(scenario.Id))
                throw new InvalidDataException($"Historical fixture scenario '{scenario.Id}' is empty or duplicated.");
            foreach (HistoricalFixtureOutput output in scenario.Outputs)
            {
                if (!IsSafeFileName(output.ArchiveFile) || !IsSafeFileName(output.ManifestFile) ||
                    !IsSha256(output.BaselineSha256) ||
                    output.BaselineManifestSha256 is not null && !IsSha256(output.BaselineManifestSha256) ||
                    output.ArchiveRoot != "." && !IsSafeRelativePath(output.ArchiveRoot))
                    throw new InvalidDataException($"Historical fixture output '{output.ArchiveFile}' has invalid path or hash metadata.");

                string archivePath = ResolveCatalogPath(repositoryRoot, output.BaselinePath);
                VerifyHash(archivePath, output.BaselineSha256);
                if (output.BaselineManifestSha256 is not null)
                {
                    string manifestPath = Path.Combine(Path.GetDirectoryName(archivePath)!, output.ManifestFile);
                    VerifyHash(manifestPath, output.BaselineManifestSha256);
                }
            }
        }
    }

    private static void ValidateBranchSnapshot(string repositoryRoot, HistoricalFixtureBranchSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Id) || string.IsNullOrWhiteSpace(snapshot.SourceBranch) ||
            !IsGitCommitId(snapshot.SourceCommit) || !IsGitCommitId(snapshot.SourceCommitParent) ||
            string.IsNullOrWhiteSpace(snapshot.GeneratorSdk) || string.IsNullOrWhiteSpace(snapshot.GeneratorRuntime) ||
            snapshot.GenerationSeed <= 0 || snapshot.Dimension <= 0 || snapshot.DocumentCount <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.FieldName) || snapshot.Quantisation != "BBQ" ||
            snapshot.BodyVersion != 2 || !IsSafeFileName(snapshot.ArchiveFile) ||
            !IsSafeFileName(snapshot.ManifestFile) || !IsSha256(snapshot.ArchiveSha256) ||
            !IsSha256(snapshot.ManifestSha256) || !IsSha256(snapshot.GeneratorProjectSha256) ||
            !IsSha256(snapshot.GeneratorSourceSha256))
            throw new InvalidDataException($"Historical branch snapshot '{snapshot.Id}' has invalid provenance or pins.");

        string archivePath = ResolveCatalogPath(repositoryRoot, snapshot.BaselinePath);
        if (!string.Equals(Path.GetFileName(archivePath), snapshot.ArchiveFile, StringComparison.Ordinal))
            throw new InvalidDataException($"Historical branch snapshot '{snapshot.Id}' archive name does not match its baseline path.");
        VerifyHash(archivePath, snapshot.ArchiveSha256);

        string manifestPath = ResolveCatalogPath(repositoryRoot, snapshot.ManifestPath);
        if (!string.Equals(Path.GetFileName(manifestPath), snapshot.ManifestFile, StringComparison.Ordinal))
            throw new InvalidDataException($"Historical branch snapshot '{snapshot.Id}' manifest name does not match its path.");
        VerifyHash(manifestPath, snapshot.ManifestSha256);

        string generatorProjectPath = ResolveCatalogPath(repositoryRoot, snapshot.GeneratorProjectPath);
        string generatorSourcePath = ResolveCatalogPath(repositoryRoot, snapshot.GeneratorSourcePath);
        VerifyHash(generatorProjectPath, snapshot.GeneratorProjectSha256);
        VerifyHash(generatorSourcePath, snapshot.GeneratorSourceSha256);

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement root = manifest.RootElement;
        if (root.GetProperty("FixtureId").GetString() != snapshot.Id ||
            root.GetProperty("SourceBranch").GetString() != snapshot.SourceBranch ||
            root.GetProperty("SourceCommit").GetString() != snapshot.SourceCommit ||
            root.GetProperty("SourceCommitParent").GetString() != snapshot.SourceCommitParent ||
            root.GetProperty("GeneratorSdk").GetString() != snapshot.GeneratorSdk ||
            root.GetProperty("GeneratorRuntime").GetString() != snapshot.GeneratorRuntime ||
            root.GetProperty("GenerationSeed").GetInt64() != snapshot.GenerationSeed ||
            root.GetProperty("Dimension").GetInt32() != snapshot.Dimension ||
            root.GetProperty("DocumentCount").GetInt32() != snapshot.DocumentCount ||
            root.GetProperty("FieldName").GetString() != snapshot.FieldName ||
            root.GetProperty("Quantisation").GetString() != snapshot.Quantisation ||
            root.GetProperty("QuantisedVectorBodyVersion").GetInt32() != snapshot.BodyVersion ||
            root.GetProperty("ArchiveFile").GetString() != snapshot.ArchiveFile ||
            root.GetProperty("ArchiveSha256").GetString() != snapshot.ArchiveSha256)
            throw new InvalidDataException($"Historical branch snapshot '{snapshot.Id}' manifest does not match its catalogue pins.");
    }

    private static void VerifyHash(string path, string expectedHash)
    {
        byte[] bytes = path.EndsWith(".fixture.b64", StringComparison.Ordinal)
            ? Convert.FromBase64String(File.ReadAllText(path))
            : File.ReadAllBytes(path);
        string actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException($"Historical fixture baseline '{path}' has SHA-256 {actual}, expected {expectedHash}.");
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsGitCommitId(string value)
        => value.Length == 40 && value.All(Uri.IsHexDigit);

    private static bool IsSafeFileName(string value)
        => !string.IsNullOrWhiteSpace(value) && Path.GetFileName(value) == value &&
            !value.Contains('/') && !value.Contains('\\');

    private static bool IsSafeRelativePath(string value)
        => !string.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) && !value.Contains('\\') &&
            value.Split('/').All(static part => part.Length > 0 && part is not "." and not "..");

    private sealed record HistoricalFixtureCatalogDocument(
        IReadOnlyList<HistoricalFixtureVersion> Versions,
        IReadOnlyList<HistoricalFixtureBranchSnapshot>? BranchSnapshots = null);
}

internal sealed record HistoricalFixtureVersion(
    string Version,
    string PackageId,
    string PackageVersion,
    string PackageSha256,
    string PackageAssemblySha256,
    string AssemblyVersion,
    string SdkVersion,
    string TargetFramework,
    string ProjectPath,
    IReadOnlyList<HistoricalFixtureScenario> Scenarios);

internal sealed record HistoricalFixtureScenario(string Id, IReadOnlyList<HistoricalFixtureOutput> Outputs);

internal sealed record HistoricalFixtureOutput(
    string ArchiveFile,
    string ArchiveRoot,
    string ManifestFile,
    string BaselinePath,
    string BaselineSha256,
    string? BaselineManifestSha256);

internal sealed record HistoricalFixtureBranchSnapshot(
    string Id,
    string SourceBranch,
    string SourceCommit,
    string SourceCommitParent,
    string GeneratorSdk,
    string GeneratorRuntime,
    long GenerationSeed,
    int Dimension,
    int DocumentCount,
    string FieldName,
    string Quantisation,
    int BodyVersion,
    string ArchiveFile,
    string BaselinePath,
    string ArchiveSha256,
    string ManifestFile,
    string ManifestPath,
    string ManifestSha256,
    string GeneratorProjectPath,
    string GeneratorProjectSha256,
    string GeneratorSourcePath,
    string GeneratorSourceSha256);
