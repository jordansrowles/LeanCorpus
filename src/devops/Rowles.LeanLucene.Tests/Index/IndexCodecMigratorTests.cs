using System.Text;
using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Compatibility;
using Rowles.LeanLucene.Index.Migration;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

[Trait("Category", "Index")]
[Trait("Category", "Migration")]
public sealed class IndexCodecMigratorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCodecMigratorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void Migrate_OlderTermDictionary_StagesAndPublishesCurrentCodec()
    {
        using var directory = CreateIndex("migration_dic");
        RewriteTermDictionaryAsV1(Directory.GetFiles(directory.DirectoryPath, "*.dic").Single());
        var stagingDirectory = Path.Combine(_fixture.Path, "migration_dic_staging");

        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            StagingDirectory = stagingDirectory
        });

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.ToString())));
        Assert.Equal(IndexMigrationState.Published, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
    }

    [Fact]
    public void Migrate_OlderPostings_RewritesCurrentCodecWithoutChangingHits()
    {
        using var directory = CreateIndex("migration_pos");
        RewritePostingsAsV2(directory);
        var stagingDirectory = Path.Combine(_fixture.Path, "migration_pos_staging");

        var plan = IndexCodecMigrator.Plan(directory);
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            StagingDirectory = stagingDirectory
        });

        Assert.Contains(plan.Actions, action => action.FileName is not null && action.FileName.EndsWith(".pos", StringComparison.Ordinal));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Select(static issue => issue.ToString())));
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
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

    private static void RewriteTermDictionaryAsV1(string path)
    {
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

    private static void RewritePostingsAsV2(MMapDirectory directory)
    {
        var dicPath = Directory.GetFiles(directory.DirectoryPath, "*.dic").Single();
        var posPath = Directory.GetFiles(directory.DirectoryPath, "*.pos").Single();
        List<(string Term, List<PostingRow> Rows)> terms;
        using (var dictionary = TermDictionaryReader.Open(dicPath))
        using (var input = new IndexInput(posPath))
        {
            byte version = PostingsEnum.ValidateFileHeader(input);
            terms = dictionary
                .EnumerateAllTerms()
                .Select(term => (term.Term, ReadRows(input, term.Offset, version)))
                .ToList();
        }

        var offsets = new Dictionary<string, long>(terms.Count, StringComparer.Ordinal);
        using (var stream = File.Create(posPath))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
        {
            CodecConstants.WriteHeader(writer, 2);
            foreach (var (term, rows) in terms)
            {
                offsets[term] = stream.Position;
                WriteV2Rows(writer, rows);
            }
        }

        TermDictionaryWriter.Write(dicPath, terms.Select(static term => term.Term).ToList(), offsets);
    }

    private static List<PostingRow> ReadRows(IndexInput input, long offset, byte version)
    {
        using var postings = PostingsEnum.CreateWithPositions(input, offset, version);
        var rows = new List<PostingRow>(postings.DocFreq);
        while (postings.MoveNext())
        {
            var positions = postings.GetCurrentPositions().ToArray();
            var payloads = new byte[positions.Length][];
            for (int i = 0; i < positions.Length; i++)
                payloads[i] = postings.GetPayload(i).ToArray();
            rows.Add(new PostingRow(postings.DocId, postings.Freq, positions, payloads));
        }

        return rows;
    }

    private static void WriteV2Rows(BinaryWriter writer, IReadOnlyList<PostingRow> rows)
    {
        writer.Write(rows.Count);
        writer.Write(0);
        int previousDocId = 0;
        foreach (var row in rows)
        {
            PostingsWriter.WriteVarInt(writer, row.DocId - previousDocId);
            previousDocId = row.DocId;
        }

        bool hasFreqs = rows.Any(static row => row.Frequency != 1);
        writer.Write(hasFreqs);
        if (hasFreqs)
        {
            foreach (var row in rows)
                PostingsWriter.WriteVarInt(writer, row.Frequency);
        }

        bool hasPositions = rows.Any(static row => row.Positions.Length > 0);
        bool hasPayloads = rows.Any(static row => row.Payloads.Any(static payload => payload.Length > 0));
        writer.Write(hasPositions);
        writer.Write(hasPayloads);
        if (!hasPositions)
            return;

        foreach (var row in rows)
        {
            PostingsWriter.WriteVarInt(writer, row.Positions.Length);
            int previousPosition = 0;
            for (int i = 0; i < row.Positions.Length; i++)
            {
                PostingsWriter.WriteVarInt(writer, row.Positions[i] - previousPosition);
                previousPosition = row.Positions[i];
                if (hasPayloads)
                {
                    var payload = row.Payloads[i];
                    PostingsWriter.WriteVarInt(writer, payload.Length);
                    writer.Write(payload);
                }
            }
        }
    }

    private sealed record PostingRow(int DocId, int Frequency, int[] Positions, byte[][] Payloads);
}
