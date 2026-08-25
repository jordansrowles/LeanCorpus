using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexCompatibilityTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCompatibilityTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Check_CurrentIndex_ReturnsCompatible()
    {
        using var directory = CreateIndex("compat_current");

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.Compatible, result.Status);
        Assert.True(result.CanRead);
        Assert.True(result.CanWrite);
        Assert.True(result.CanValidate);
        Assert.False(result.MustReject);
        Assert.False(result.RequiresMigration);
    }

    [Fact]
    public void Check_EmptyDirectory_ReturnsEmpty()
    {
        var path = Path.Combine(_fixture.Path, "compat_empty");
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.Empty, result.Status);
        Assert.True(result.CanRead);
        Assert.True(result.CanWrite);
        Assert.True(result.CanValidate);
        Assert.False(result.MustReject);
    }

    [Fact]
    public void Check_OlderReadableCodec_ReturnsMigrationRecommended()
    {
        using var directory = CreateIndex("compat_old");
        WriteCodecVersion(directory, "*.nrm", CodecConstants.NormsVersion - 1);

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.MigrationRecommended, result.Status);
        Assert.True(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.True(result.CanValidate);
        Assert.False(result.MustReject);
        Assert.True(result.CanMigrate);
    }

    [Fact]
    public void Check_OlderReadableCodec_WhenCurrentFormatsRequired_ReturnsMigrationRequired()
    {
        using var directory = CreateIndex("compat_required");
        WriteCodecVersion(directory, "*.nrm", CodecConstants.NormsVersion - 1);

        var result = IndexCompatibility.Check(directory, new IndexCompatibilityOptions { RequireCurrentFormats = true });

        Assert.Equal(IndexCompatibilityStatus.MigrationRequired, result.Status);
        Assert.False(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.True(result.CanValidate);
        Assert.False(result.MustReject);
        Assert.True(result.RequiresMigration);
        Assert.True(result.CanMigrate);
    }

    [Fact]
    public void Check_FutureCodec_ReturnsUnsupportedFutureFormat()
    {
        using var directory = CreateIndex("compat_future");
        WriteCodecKitVersion(directory, "*.dic", CodecConstants.TermDictionaryVersion + 1);

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.UnsupportedFutureFormat, result.Status);
        Assert.False(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.False(result.CanValidate);
        Assert.True(result.MustReject);
    }

    [Fact]
    public void Check_UnknownCanonicalFormat_ReturnsUnknownFormat()
    {
        using var directory = CreateIndex("compat_unknown");
        var path = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        using (var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = 5;
            int length = stream.ReadByte();
            var replacement = System.Text.Encoding.ASCII.GetBytes("x." + new string('x', length - 2));
            stream.Position = CodecFileWriter.FixedHeaderLength;
            stream.Write(replacement);
        }

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.UnknownFormat, result.Status);
        Assert.False(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.True(result.CanValidate);
        Assert.True(result.MustReject);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.UnknownCodecFormat);
    }

    [Fact]
    public void Check_ExplicitCatalogues_AreIsolatedFromTheDefaultCatalogue()
    {
        using var directory = CreateIndex("compat_catalogue_isolation");
        var isolatedCatalogue = CreateIsolatedCatalogue();

        var defaultBefore = IndexCompatibility.Check(directory);
        var isolated = IndexCompatibility.Check(directory, new IndexCompatibilityOptions
        {
            Catalog = isolatedCatalogue,
        });
        var defaultAfter = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.Compatible, defaultBefore.Status);
        Assert.Equal(IndexCompatibilityStatus.UnknownFormat, isolated.Status);
        Assert.Equal(IndexCompatibilityStatus.Compatible, defaultAfter.Status);
    }

    [Fact]
    public void Check_CorruptIndex_ReturnsCorruptMustReject()
    {
        using var directory = CreateIndex("compat_corrupt");
        File.Delete(Directory.GetFiles(directory.DirectoryPath, "*.dic").Single());

        var result = IndexCompatibility.Check(directory);

        Assert.Equal(IndexCompatibilityStatus.Corrupt, result.Status);
        Assert.False(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.True(result.CanValidate);
        Assert.True(result.MustReject);
    }

    [Fact]
    public void IndexSearcher_FutureCodec_IsRejectedDuringRecovery()
    {
        using var directory = CreateIndex("compat_searcher_guard");
        WriteCodecKitVersion(directory, "*.dic", CodecConstants.TermDictionaryVersion + 1);

        Assert.Throws<InvalidDataException>(() => new IndexSearcher(directory));
    }

    [Fact]
    public void IndexSearcher_UnsafeMode_AllowsDiagnosticOpenWithMigrationMarker()
    {
        using var directory = CreateIndex("compat_marker_unsafe");
        File.WriteAllText(
            Path.Combine(directory.DirectoryPath, IndexMigrationRecovery.MarkerFileName),
            $$"""
            {
              "State": 2,
              "SourceDirectory": "{{directory.DirectoryPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "StagingDirectory": "{{Path.Combine(_fixture.Path, "unsafe-staging").Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "SourceCommitGeneration": 1,
              "CreatedAtUtc": "2026-05-10T00:00:00+00:00",
              "UpdatedAtUtc": "2026-05-10T00:00:00+00:00",
              "PlannedActions": []
            }
            """);

        using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
        {
            CompatibilityMode = IndexOpenCompatibilityMode.UnsafeIgnoreCompatibility
        });

        Assert.NotNull(searcher);
    }

    [Fact]
    public void IndexWriter_OlderReadableCodec_ThrowsInvalidDataException()
    {
        using var directory = CreateIndex("compat_writer_guard");
        WriteCodecKitVersion(directory, "*.dic", 0);

        Assert.Throws<InvalidDataException>(() => new IndexWriter(directory, new IndexWriterConfig()));
    }

    [Fact]
    public void IndexSearcher_MigrationMarker_ThrowsInvalidDataException()
    {
        using var directory = CreateIndex("compat_marker_guard");
        File.WriteAllText(
            Path.Combine(directory.DirectoryPath, IndexMigrationRecovery.MarkerFileName),
            $$"""
            {
              "State": 2,
              "SourceDirectory": "{{directory.DirectoryPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "StagingDirectory": "{{Path.Combine(_fixture.Path, "staging").Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "SourceCommitGeneration": 1,
              "CreatedAtUtc": "2026-05-10T00:00:00+00:00",
              "UpdatedAtUtc": "2026-05-10T00:00:00+00:00",
              "PlannedActions": []
            }
            """);

        Assert.Throws<InvalidDataException>(() => new IndexSearcher(directory));
    }

    private MMapDirectory CreateIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new TextField("body", "hello world"));
        document.Add(new StringField("id", "1"));
        writer.AddDocument(document);
        writer.Commit();
        return directory;
    }

    private static void WriteCodecVersion(MMapDirectory directory, string pattern, int version)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, pattern).Single();
        byte[] body;
        using (var input = new IndexInput(path))
        using (var frame = CodecFileReader.Open(input))
            body = frame.ReadBody();

        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte((byte)version);
        ulong length = (ulong)body.Length << 1;
        while (length >= 0x80)
        {
            stream.WriteByte((byte)(length | 0x80));
            length >>= 7;
        }
        stream.WriteByte((byte)length);
        stream.Write(body);
    }

    private static void WriteCodecKitVersion(MMapDirectory directory, string pattern, int version)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, pattern).Single();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = 6;
        stream.Write(BitConverter.GetBytes(version));
    }

    private static CodecCatalog CreateIsolatedCatalogue()
    {
        const string familyId = "example.catalogue-isolation";
        var file = new CodecFileDescriptor(
            "example.catalogue-isolation.data",
            familyId,
            "Catalogue isolation data",
            CodecFileMatcher.Extension(".custom"),
            currentFormatVersion: null);

        return new CodecCatalogBuilder()
            .Add(new CodecFamilyDescriptor(familyId, "Catalogue isolation", [file]))
            .Build();
    }
}
