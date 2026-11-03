using Rowles.LeanCorpus.Codecs.Vectors;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyIndex_RejectsRetiredBbqConfigurationBeforeCreatingFiles(bool normalise)
    {
        string path = SubDir($"{nameof(EmptyIndex_RejectsRetiredBbqConfigurationBeforeCreatingFiles)}_{normalise}");
        using var directory = new MMapDirectory(path);

        var exception = Assert.Throws<ArgumentException>(() => new IndexWriter(directory, new IndexWriterConfig
        {
            VectorQuantisation = VectorQuantisation.BBQ,
            NormaliseVectors = normalise
        }));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(path));
    }

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

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(384)]
    [InlineData(768)]
    public void SemanticWriter_RejectsEveryBbqDimensionBeforeTemporaryFiles(int dimension)
    {
        string directory = SubDir($"retired-{dimension}");
        string path = Path.Combine(directory, "vectors.vq");
        var vectors = new Dictionary<int, ReadOnlyMemory<float>> { [0] = UnitVector(dimension) };
        var source = new InMemoryVectorSource(vectors, dimension);
        Assert.Throws<InvalidOperationException>(() => QuantisedVectorWriter.WriteBBQOsqCanonical(path, 1, dimension, source, [0]));
        Assert.Throws<InvalidOperationException>(() => QuantisedVectorWriter.WriteBBQ(path, 1, dimension, source, [0]));
        Assert.Throws<InvalidOperationException>(() => QuantisedVectorWriter.WriteBBQ(path, 1, dimension, vectors, new float[dimension]));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
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
