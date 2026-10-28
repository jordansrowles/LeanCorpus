using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class HistoricalFixtureGenerationServiceTests
{
    [Fact]
    public void BuildsARepeatableArchiveFromExactlyTheHashedManifestFiles()
    {
        using var directory = new TemporaryDirectory();
        var layoutDirectory = Path.Combine(directory.Path, "loose");
        Directory.CreateDirectory(layoutDirectory);
        File.WriteAllText(Path.Combine(layoutDirectory, "b.seg"), "segment b");
        File.WriteAllText(Path.Combine(layoutDirectory, "a.seg"), "segment a");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        File.WriteAllText(manifestPath, CreateManifest(layoutDirectory, "a.seg", "b.seg"));

        var first = HistoricalFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath).Archive;
        var second = HistoricalFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath).Archive;

        Assert.Equal(first, second);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(first)), HistoricalFixtureGenerationService.Hash(first));
        Assert.Equal(2, first[8]);
        Assert.Equal(byte.MaxValue, first[9]);
        var canonicalEncoding = first.ToArray();
        canonicalEncoding[9] = 0;
        Assert.Equal(canonicalEncoding, HistoricalFixtureGenerationService.PreferCanonicalEncoding(first, canonicalEncoding));
        using (var archiveInput = new MemoryStream(first))
        using (var archiveGzip = new GZipStream(archiveInput, CompressionMode.Decompress))
        using (var tarBytes = new MemoryStream())
        {
            archiveGzip.CopyTo(tarBytes);
            Assert.Equal(0, tarBytes.Length % 10_240);
        }
        using var input = new MemoryStream(first);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var firstEntry = tar.GetNextEntry();
        var secondEntry = tar.GetNextEntry();
        Assert.Equal("a.seg", firstEntry?.Name);
        Assert.Equal("b.seg", secondEntry?.Name);
        Assert.Null(tar.GetNextEntry());
    }

    [Fact]
    public void UsesGeneratedEncodingWhenCanonicalArchiveHasDifferentTarPayload()
    {
        using var directory = new TemporaryDirectory();
        var layoutDirectory = Path.Combine(directory.Path, "loose");
        Directory.CreateDirectory(layoutDirectory);
        File.WriteAllText(Path.Combine(layoutDirectory, "a.seg"), "original");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        File.WriteAllText(manifestPath, CreateManifest(layoutDirectory, "a.seg"));
        var canonical = HistoricalFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath).Archive;

        File.WriteAllText(Path.Combine(layoutDirectory, "a.seg"), "changed");
        File.WriteAllText(manifestPath, CreateManifest(layoutDirectory, "a.seg"));
        var generated = HistoricalFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath).Archive;

        Assert.Equal(generated, HistoricalFixtureGenerationService.PreferCanonicalEncoding(generated, canonical));
    }

    [Fact]
    public void RejectsAFileThatDoesNotMatchItsManifestHash()
    {
        using var directory = new TemporaryDirectory();
        var layoutDirectory = Path.Combine(directory.Path, "loose");
        Directory.CreateDirectory(layoutDirectory);
        File.WriteAllText(Path.Combine(layoutDirectory, "a.seg"), "actual content");
        var manifestPath = Path.Combine(directory.Path, "manifest.json");
        File.WriteAllText(manifestPath, """
            {"Files":[{"Name":"a.seg","Length":7,"Sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}
            """);

        Assert.Throws<InvalidDataException>(() => HistoricalFixtureGenerationService.CreateArchive(layoutDirectory, manifestPath));
    }

    [Fact]
    public void CanonicalisesOnlyHistoricalSegmentJsonPropertyOrdering()
    {
        using var directory = new TemporaryDirectory();
        var canonicalRoot = Path.Combine(directory.Path, "canonical");
        var generatedRoot = Path.Combine(directory.Path, "generated");
        Directory.CreateDirectory(canonicalRoot);
        Directory.CreateDirectory(generatedRoot);
        byte[] canonicalMetadata = "{\"SegmentId\":\"seg_0\",\"CodecBytes\":{\".dvs\":1,\".dic\":2}}"u8.ToArray();
        byte[] generatedMetadata = "{\"SegmentId\":\"seg_0\",\"CodecBytes\":{\".dic\":2,\".dvs\":1}}"u8.ToArray();
        File.WriteAllBytes(Path.Combine(canonicalRoot, "seg_0.seg"), canonicalMetadata);
        File.WriteAllBytes(Path.Combine(generatedRoot, "seg_0.seg"), generatedMetadata);
        string canonicalManifest = Path.Combine(directory.Path, "canonical.json");
        string generatedManifest = Path.Combine(directory.Path, "generated.json");
        File.WriteAllText(canonicalManifest, CreateManifest(canonicalRoot, "seg_0.seg"));
        File.WriteAllText(generatedManifest, CreateManifest(generatedRoot, "seg_0.seg"));
        ArchiveBuildResult canonical = HistoricalFixtureGenerationService.CreateArchive(canonicalRoot, canonicalManifest);
        var canonicalEntries = HistoricalFixtureGenerationService.ReadArchiveEntries(canonical.Archive);

        ArchiveBuildResult generated = HistoricalFixtureGenerationService.CreateArchive(generatedRoot, generatedManifest, canonicalEntries);
        using JsonDocument manifest = JsonDocument.Parse(generated.Manifest);

        Assert.True(HistoricalFixtureGenerationService.ArchiveEntriesMatch(generated.Archive, canonical.Archive));
        Assert.Equal(HistoricalFixtureGenerationService.Hash(canonicalMetadata),
            manifest.RootElement.GetProperty("Files")[0].GetProperty("Sha256").GetString());
    }

    [Fact]
    public void RejectsCorruptHistoricalArchiveBytes()
    {
        Assert.Throws<InvalidDataException>(() => HistoricalFixtureGenerationService.ReadArchiveEntries([1, 2, 3]));
    }

    [Fact]
    public void ReadsTheCommittedSparseFixtureArchiveDirectoryEntries()
    {
        string repositoryRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
        string path = Path.Combine(repositoryRoot, "src", "devops", "tests", "Rowles.LeanCorpus.Tests.Shared", "Fixtures", "Indexes", "3.1.1-sparse-hnsw.fixture.b64");
        byte[] archive = Convert.FromBase64String(File.ReadAllText(path));

        IReadOnlyDictionary<string, ArchiveEntryData> entries = HistoricalFixtureGenerationService.ReadArchiveEntries(archive);

        Assert.Contains("float32-compound", entries.Keys);
        Assert.Contains("float32-compound/seg_0.cfs", entries.Keys);
        Assert.Equal(TarEntryType.Directory, entries["float32-compound"].Type);
    }

    [Fact]
    public void NuGetConfigurationUsesOnlyTheRequestedPinnedSource()
    {
        XDocument config = HistoricalFixtureGenerationService.BuildNuGetConfigDocument("/tmp/pinned-feed");
        XElement sources = config.Root!.Element("packageSources")!;

        Assert.Single(sources.Elements("clear"));
        Assert.Equal(new[] { "pinned-release" }, sources.Elements("add").Select(element => element.Attribute("key")!.Value));
        Assert.Equal("/tmp/pinned-feed", sources.Element("add")!.Attribute("value")!.Value);
        Assert.Null(config.Root.Element("packageSourceMapping"));
    }

    [Fact]
    public void AcceptsAnAbsoluteLocalPackageSourceDirectory()
    {
        Assert.Null(HistoricalFixtureGenerationService.ValidatePackageSource(Path.GetTempPath()));
        Assert.NotNull(HistoricalFixtureGenerationService.ValidatePackageSource(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public async Task ExistingOutputDirectoryIsRejectedWithoutTouchingItsFiles()
    {
        using var artifacts = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        string marker = Path.Combine(output.Path, "keep.txt");
        await File.WriteAllTextAsync(marker, "user data", TestContext.Current.CancellationToken);
        CheckoutIdentity checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var runner = new RecordingProcessRunner(OutcomeKind.Success);
        var provenance = new RecordingProvenanceReader();
        var paths = new ArtifactPaths(artifacts.Path);
        var service = new HistoricalFixtureGenerationService(paths, new ArtifactRunStore(paths, runCategory: "data"),
            runner, provenance);

        HistoricalFixtureGenerationResult result = await service.GenerateAsync(new HistoricalFixtureGenerationRequest(
            checkout, "3.1.1", "migration", "https://example.test/v3/index.json", output.Path), TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.Equal("user data", await File.ReadAllTextAsync(marker, TestContext.Current.CancellationToken));
        Assert.Empty(runner.Requests);
        Assert.Equal(0, provenance.Calls);
    }

    [Fact]
    public async Task CancelledRestoreLeavesTerminalRunEvidenceAndRetainedIsolationState()
    {
        using var artifacts = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        CheckoutIdentity checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var runner = new RecordingProcessRunner(OutcomeKind.Cancelled);
        var provenance = new RecordingProvenanceReader();
        var paths = new ArtifactPaths(artifacts.Path);
        var service = new HistoricalFixtureGenerationService(paths, new ArtifactRunStore(paths, runCategory: "data"),
            runner, provenance);

        HistoricalFixtureGenerationResult result = await service.GenerateAsync(new HistoricalFixtureGenerationRequest(
            checkout, "3.1.1", "migration", "https://example.test/v3/index.json", Path.Combine(output.Path, "generated")),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Cancelled, result.CommandResult.Outcome);
        Assert.NotNull(result.RunDirectory);
        using JsonDocument cleanup = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(result.RunDirectory!, "cleanup.json"), TestContext.Current.CancellationToken));
        Assert.False(cleanup.RootElement.GetProperty("removed").GetBoolean());
        Assert.Single(runner.Requests);
        Assert.Contains("--locked-mode", runner.Requests[0].Arguments);
        Assert.Equal(1, provenance.Calls);
    }

    private static string CreateManifest(string layoutDirectory, params string[] names)
    {
        var files = names.Select(name =>
        {
            var bytes = File.ReadAllBytes(Path.Combine(layoutDirectory, name));
            return new { Name = name, Length = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        });
        return JsonSerializer.Serialize(new { Files = files }, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-migration-fixture-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class RecordingProcessRunner(OutcomeKind outcome) : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Directory.CreateDirectory(Path.GetDirectoryName(request.StandardOutputLogPath)!);
            File.WriteAllText(request.StandardOutputLogPath, string.Empty);
            File.WriteAllText(request.StandardErrorLogPath, string.Empty);
            return Task.FromResult(new ProcessResult(outcome,
                outcome == OutcomeKind.Success ? 0 : null, [], [], 0, 0, TimeSpan.Zero,
                request.StandardOutputLogPath, request.StandardErrorLogPath,
                outcome == OutcomeKind.Success ? null : "cancelled by test"));
        }
    }

    private sealed class RecordingProvenanceReader : IRepositoryProvenanceReader
    {
        public int Calls { get; private set; }

        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new GitProvenance(OutcomeKind.Success, new string('a', 40), "issue-97", true, null));
        }
    }
}
