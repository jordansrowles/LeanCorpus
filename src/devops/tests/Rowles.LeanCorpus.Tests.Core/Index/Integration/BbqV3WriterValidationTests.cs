using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index.Integration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class BbqV3WriterValidationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public BbqV3WriterValidationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void WriterRejectsBbqWithoutNormalisationBeforeAcquiringDirectoryLock()
    {
        string path = SubDir(nameof(WriterRejectsBbqWithoutNormalisationBeforeAcquiringDirectoryLock));
        using var directory = new MMapDirectory(path);
        var config = new IndexWriterConfig
        {
            VectorQuantisation = VectorQuantisation.BBQ,
            NormaliseVectors = false
        };

        Assert.Throws<ArgumentException>(() => new IndexWriter(directory, config));
        Assert.False(File.Exists(Path.Combine(path, "write.lock")));
        Assert.Empty(Directory.EnumerateFiles(path));
    }

    [Fact]
    public void WriterRejectsUnsupportedDimensionsWithoutReservingTheFieldAndWritesV3At128()
    {
        string path = SubDir(nameof(WriterRejectsUnsupportedDimensionsWithoutReservingTheFieldAndWritesV3At128));
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            VectorQuantisation = VectorQuantisation.BBQ,
            NormaliseVectors = true,
            BuildHnswOnFlush = false
        });

        var unsupported = new LeanDocument();
        unsupported.Add(new VectorField("embedding", new float[127]));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddDocument(unsupported));
        Assert.Equal(0, writer.NextSequenceNumber);

        var supported = new LeanDocument();
        supported.Add(new VectorField("embedding", UnitVector(128)));
        writer.AddDocument(supported);
        writer.Commit();

        string vqPath = Assert.Single(Directory.EnumerateFiles(path, "*.vq", SearchOption.AllDirectories));
        using var reader = QuantisedVectorReader.Open(vqPath);
        Assert.Equal(QuantisedVectorStorageKind.BbqOsqCanonicalInt8, reader.StorageKind);
        Assert.Equal(128, reader.Dimension);
        Assert.True(reader.HasVector(0));
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static float[] UnitVector(int dimension)
    {
        var vector = new float[dimension];
        vector[0] = 1f;
        return vector;
    }
}
