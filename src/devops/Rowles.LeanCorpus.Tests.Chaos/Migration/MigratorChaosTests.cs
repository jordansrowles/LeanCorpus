using System.Text;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Migration;

[Trait("Category", "Chaos")]
[Trait("Category", "Migration")]
public sealed class MigratorChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public MigratorChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Migrate_BlockedTemporaryDicPath_CatchesExceptionAndReturnsFailed()
    {
        using var directory = CreateMigrationIndex("chaos_readonly_dic");
        RewriteTermDictionaryAsV1(directory);
        var dicPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        var temporaryDicPath = dicPath + ".tmp";
        // Blocking the temporary rewrite path is deterministic across Windows and Unix-like runners.
        Directory.CreateDirectory(temporaryDicPath);
        try
        {
            var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
            {
                DryRun = false,
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
                ValidateBeforeMigration = false
            });

            Assert.False(result.Succeeded);
            Assert.Contains(result.Issues, i => i.Code == IndexCheckIssueCodes.UnsupportedMigrationPath);
            Assert.Equal(IndexMigrationState.Failed, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
        }
        finally
        {
            Directory.Delete(temporaryDicPath, recursive: true);
        }
    }

    [Fact]
    public void Migrate_ValidateAfterMigration_CorruptNrmAfterRewrite_MarksAsFailed()
    {
        using var directory = CreateMigrationIndex("chaos_validate_after");
        // Properly rewrite .dic as V1 so the plan has an executable action to run.
        RewriteTermDictionaryAsV1(directory);
        // Corrupt the .nrm magic so post-migration validation of the staging directory
        // finds an InvalidCodecMagic error. The .nrm is not in ExecutableRewriteExtensions,
        // so the migrator copies it unchanged into staging.
        CorruptMagic(directory, "*.nrm");

        var stagingPath = Path.Combine(_fixture.Path, $"chaos_validate_after_staging_{Guid.NewGuid():N}");
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            ValidateBeforeMigration = false,
            ValidateAfterMigration = true,
            StagingDirectory = stagingPath
        });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ValidationResult);
        Assert.Contains(result.ValidationResult!.DetailedIssues, i => i.Severity == IndexCheckSeverity.Error);
        Assert.Equal(IndexMigrationState.Failed, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
    }

    private MMapDirectory CreateMigrationIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        var documents = new[]
        {
            ("doc-1", "chaos migration alpha body"),
            ("doc-2", "chaos migration beta body"),
            ("doc-3", "chaos migration gamma body")
        };

        foreach (var (id, body) in documents)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", id, stored: true));
            document.Add(new TextField("body", body, stored: true));
            writer.AddDocument(document);
        }

        writer.Commit();
        return directory;
    }

    private static void RewriteTermDictionaryAsV1(MMapDirectory directory)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        using var reader = TermDictionaryReader.Open(path);
        var terms = reader.EnumerateAllTerms();

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false);
        CodecConstants.WriteHeader(writer, 1);
        writer.Write(0);
        foreach (var (term, offset) in terms)
        {
            var bytes = Encoding.UTF8.GetBytes(term);
            writer.Write(bytes.Length);
            writer.Write(bytes);
            writer.Write(offset);
        }
    }

    private static void CorruptMagic(MMapDirectory directory, string pattern)
    {
        var path = Directory.GetFiles(directory.DirectoryPath, pattern).Single();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = 0;
        stream.Write([0x00, 0x00, 0x00, 0x00]);
    }
}
