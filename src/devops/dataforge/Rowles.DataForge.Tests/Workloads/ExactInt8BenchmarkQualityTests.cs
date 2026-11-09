using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class ExactInt8BenchmarkQualityTests
{
    [Fact]
    public void Int8WriterConfigurationIsRetiredBeforeIndexSideEffects()
    {
        string path = RepositoryTemporaryStorage.NewPath("dataforge", "lc-int8-retired-");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            var exception = Assert.Throws<ArgumentException>(() => new IndexWriter(directory,
                new IndexWriterConfig { VectorQuantisation = VectorQuantisation.Int8 }));

            Assert.Contains("Int8WriteRetired", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(path));
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void ExplicitHnswWriteModeIsRetiredBeforeIndexSideEffects()
    {
        string path = RepositoryTemporaryStorage.NewPath("dataforge", "lc-hnsw-retired-");
        Directory.CreateDirectory(path);
        try
        {
            using var directory = new MMapDirectory(path);
            var exception = Assert.Throws<ArgumentException>(() => new IndexWriter(directory,
                new IndexWriterConfig { BuildHnswOnFlush = true }));

            Assert.Contains("HnswGraphCreationRetired", exception.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(path));
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
