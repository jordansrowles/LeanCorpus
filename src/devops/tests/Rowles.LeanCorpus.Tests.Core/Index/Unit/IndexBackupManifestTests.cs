using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for <see cref="IndexBackup"/> manifest format handling and CRC-32 checksum validation.
/// Manifests are hand-crafted in a temporary directory; no index is built.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
[Area(TestArea.Util)]
public sealed class IndexBackupManifestTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexBackupManifestTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name) => Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");

    private static IndexBackupManifest CreateManifest(params (string FileName, byte[] Content)[] files)
    {
        var entries = new List<IndexBackupFileEntry>(files.Length);
        foreach (var (name, content) in files)
        {
            entries.Add(new IndexBackupFileEntry
            {
                FileName = name,
                Length = content.Length,
                Crc32 = Crc32.Compute(content),
                Role = "sidecar",
                IsRequired = false,
                IsCommitFile = false,
                PresentInBackup = true
            });
        }

        return new IndexBackupManifest
        {
            FormatVersion = IndexBackup.CurrentManifestFormatVersion,
            Kind = IndexBackupKind.Full,
            ChainDepth = 1,
            CommitGeneration = 1,
            ContentToken = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CommitFileName = "segments_1",
            Files = entries
        };
    }

    private static void WriteBackup(
        string directory,
        IndexBackupManifest manifest,
        params (string FileName, byte[] Content)[] files)
    {
        Directory.CreateDirectory(directory);
        foreach (var (name, content) in files)
            File.WriteAllBytes(Path.Combine(directory, name), content);

        var json = JsonSerializer.Serialize(manifest, LeanCorpusJsonContext.Default.IndexBackupManifest);
        File.WriteAllText(Path.Combine(directory, IndexBackup.ManifestFileName), json);
    }

    [Fact]
    public void ReadManifest_MissingFile_ThrowsInvalidData()
    {
        var dir = SubDir("missing");
        Directory.CreateDirectory(dir);

        var exception = Assert.Throws<InvalidDataException>(() => IndexBackup.ReadManifest(dir));

        Assert.Contains("not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadManifest_InvalidJson_Throws()
    {
        var dir = SubDir("invalid-json");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, IndexBackup.ManifestFileName), "not-json");

        Assert.Throws<JsonException>(() => IndexBackup.ReadManifest(dir));
    }

    [Fact]
    public void ReadManifest_UnsupportedFormatVersion_Throws()
    {
        var dir = SubDir("unsupported-version");
        WriteBackup(dir, new IndexBackupManifest { FormatVersion = "99" });

        var exception = Assert.Throws<InvalidDataException>(() => IndexBackup.ReadManifest(dir));

        Assert.Contains("not supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadManifest_LegacyV1_RewritesAsFull()
    {
        var dir = SubDir("legacy-v1");
        var created = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var manifest = new IndexBackupManifest
        {
            FormatVersion = "1",
            Kind = IndexBackupKind.Incremental,
            ChainDepth = 9,
            ParentManifestSha256 = "deadbeef",
            CommitGeneration = 7,
            ContentToken = 42,
            CreatedAtUtc = created,
            CommitFileName = "segments_7",
            Files =
            [
                new IndexBackupFileEntry
                {
                    FileName = "data.bin",
                    Length = 4,
                    Crc32 = Crc32.Compute(new byte[] { 1, 2, 3, 4 }),
                    Role = "sidecar",
                    IsRequired = true,
                    IsCommitFile = false,
                    PresentInBackup = true
                }
            ]
        };
        WriteBackup(dir, manifest, ("data.bin", new byte[] { 1, 2, 3, 4 }));

        var result = IndexBackup.ReadManifest(dir);

        Assert.Equal("1", result.FormatVersion);
        Assert.Equal(IndexBackupKind.Full, result.Kind);
        Assert.Equal(1, result.ChainDepth);
        Assert.Null(result.ParentManifestSha256);
        Assert.Equal(7, result.CommitGeneration);
        Assert.Equal(42L, result.ContentToken);
        Assert.Equal(created, result.CreatedAtUtc);
        Assert.Equal("segments_7", result.CommitFileName);
        Assert.Equal("data.bin", Assert.Single(result.Files).FileName);
    }

    [Fact]
    public void ReadManifest_CurrentV2_RoundTrips()
    {
        var dir = SubDir("current-v2");
        var parent = string.Concat(Enumerable.Repeat("ab", 32));
        var manifest = new IndexBackupManifest
        {
            FormatVersion = IndexBackup.CurrentManifestFormatVersion,
            Kind = IndexBackupKind.Incremental,
            ParentManifestSha256 = parent,
            ChainDepth = 3,
            CommitGeneration = 5,
            ContentToken = 99,
            CreatedAtUtc = new DateTimeOffset(2025, 6, 7, 8, 9, 10, TimeSpan.Zero),
            CommitFileName = "segments_5",
            Files =
            [
                new IndexBackupFileEntry
                {
                    FileName = "a.bin", Length = 3, Crc32 = Crc32.Compute(new byte[] { 1, 2, 3 }),
                    Role = "sidecar", IsRequired = true, IsCommitFile = false, PresentInBackup = true
                },
                new IndexBackupFileEntry
                {
                    FileName = "b.bin", Length = 4, Crc32 = Crc32.Compute(new byte[] { 4, 5, 6, 7 }),
                    Role = "sidecar", IsRequired = true, IsCommitFile = false, PresentInBackup = true
                }
            ]
        };
        WriteBackup(dir, manifest,
            ("a.bin", new byte[] { 1, 2, 3 }),
            ("b.bin", new byte[] { 4, 5, 6, 7 }));

        var result = IndexBackup.ReadManifest(dir);

        Assert.Equal(IndexBackupKind.Incremental, result.Kind);
        Assert.Equal(3, result.ChainDepth);
        Assert.Equal(parent, result.ParentManifestSha256);
        Assert.Equal(5, result.CommitGeneration);
        Assert.Equal(99L, result.ContentToken);
        Assert.Equal("segments_5", result.CommitFileName);
        Assert.Collection(result.Files,
            file =>
            {
                Assert.Equal("a.bin", file.FileName);
                Assert.Equal(3L, file.Length);
                Assert.Equal(Crc32.Compute(new byte[] { 1, 2, 3 }), file.Crc32);
            },
            file =>
            {
                Assert.Equal("b.bin", file.FileName);
                Assert.Equal(4L, file.Length);
                Assert.Equal(Crc32.Compute(new byte[] { 4, 5, 6, 7 }), file.Crc32);
            });
    }

    [Fact]
    public void Crc32_KnownVector()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"));
        Assert.Equal(0u, Crc32.Compute(""));
    }

    [Fact]
    public void Crc32_IncrementalMatchesOneShot()
    {
        var data = new byte[512];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i & 0xFF);

        uint oneShot = Crc32.Compute(data);

        uint state = Crc32.Begin();
        foreach (var chunk in data.Chunk(3))
            state = Crc32.Update(state, chunk);

        Assert.Equal(oneShot, Crc32.Finish(state));
    }

    [Fact]
    public void ValidateBackup_Valid_ReturnsManifest()
    {
        var dir = SubDir("valid");
        var manifest = CreateManifest(("data.bin", new byte[] { 1, 2, 3, 4 }));
        WriteBackup(dir, manifest, ("data.bin", new byte[] { 1, 2, 3, 4 }));

        var result = IndexBackup.ValidateBackup(dir);

        Assert.Equal(manifest.CommitGeneration, result.CommitGeneration);
        Assert.Single(result.Files);
    }

    [Fact]
    public void ValidateBackup_MissingFile_Throws()
    {
        var dir = SubDir("missing-file");
        var manifest = CreateManifest(("data.bin", new byte[] { 1, 2, 3, 4 }));
        WriteBackup(dir, manifest);

        var exception = Assert.Throws<InvalidDataException>(() => IndexBackup.ValidateBackup(dir));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains("data.bin", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateBackup_LengthMismatch_Throws()
    {
        var dir = SubDir("length-mismatch");
        var manifest = CreateManifest(("data.bin", new byte[] { 1, 2, 3, 4, 5 }));
        WriteBackup(dir, manifest, ("data.bin", new byte[] { 1, 2, 3, 4, 5 }));

        using (var stream = new FileStream(Path.Combine(dir, "data.bin"), FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(3);

        var exception = Assert.Throws<InvalidDataException>(() => IndexBackup.ValidateBackup(dir));

        Assert.Contains("length", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateBackup_CrcMismatch_SameLength_Throws()
    {
        var dir = SubDir("crc-mismatch");
        var manifest = CreateManifest(("data.bin", new byte[] { 1, 2, 3, 4 }));
        WriteBackup(dir, manifest, ("data.bin", new byte[] { 1, 2, 3, 4 }));

        using (var stream = File.Open(Path.Combine(dir, "data.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var value = stream.ReadByte();
            stream.Position = 0;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        var exception = Assert.Throws<InvalidDataException>(() => IndexBackup.ValidateBackup(dir));

        Assert.Contains("CRC-32", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../escape.dic")]
    [InlineData("sub/escape.dic")]
    [InlineData("/rooted.dic")]
    public void ValidateManifestFileName_RejectsUnsafeNames(string fileName)
    {
        var dir = SubDir("unsafe-name");
        var manifest = new IndexBackupManifest
        {
            FormatVersion = IndexBackup.CurrentManifestFormatVersion,
            Kind = IndexBackupKind.Full,
            Files =
            [
                new IndexBackupFileEntry
                {
                    FileName = fileName,
                    Length = 1,
                    Crc32 = 0,
                    Role = "sidecar",
                    PresentInBackup = true
                }
            ]
        };
        WriteBackup(dir, manifest);

        Assert.Throws<InvalidDataException>(() => IndexBackup.ValidateBackup(dir));
    }
}
