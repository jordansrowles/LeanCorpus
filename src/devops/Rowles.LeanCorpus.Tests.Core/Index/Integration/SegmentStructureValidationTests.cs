using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class SegmentStructureValidationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ll-segment-structure-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false, ".fdt")]
    [InlineData(true, ".fdt")]
    [InlineData(false, ".vec")]
    [InlineData(true, ".hnsw")]
    public void SegmentReaderAndExplicitSearcher_RejectMissingLogicalMembers_ThenRecover(
        bool compound,
        string missingExtension)
    {
        using var directory = CreateIndex(compound, out SegmentInfo info);
        string segmentId = info.SegmentId;
        string fileName = missingExtension switch
        {
            ".fdt" => segmentId + ".fdt",
            ".vec" => Path.GetFileName(VectorFilePaths.VectorFile(segmentId, "embedding")),
            ".hnsw" => Path.GetFileName(VectorFilePaths.HnswFile(segmentId, "embedding")),
            _ => throw new ArgumentOutOfRangeException(nameof(missingExtension))
        };
        Assert.True(info.VectorFields.Single().HasHnsw);

        string cfsPath = Path.Combine(_path, segmentId + ".cfs");
        byte[]? originalCompound = compound ? File.ReadAllBytes(cfsPath) : null;
        string memberPath = Path.Combine(_path, fileName);
        byte[]? originalMember = compound ? null : File.ReadAllBytes(memberPath);

        try
        {
            if (compound)
                RewriteCompoundWithoutMember(segmentId, fileName, cfsPath);
            else
                File.Delete(memberPath);

            Assert.Throws<FileNotFoundException>(() =>
            {
                using var reader = new SegmentReader(directory, info);
            });
            Assert.Throws<FileNotFoundException>(() =>
            {
                using var searcher = new IndexSearcher(directory, new[] { info });
            });
            Assert.Throws<InvalidDataException>(
                () => IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false));
        }
        finally
        {
            if (compound)
                File.WriteAllBytes(cfsPath, originalCompound!);
            else
                File.WriteAllBytes(memberPath, originalMember!);
        }

        using var recoveredReader = new SegmentReader(directory, info);
        Assert.NotNull(recoveredReader.GetVector("embedding", 0));
    }

    [Fact]
    public void SegmentReader_RejectsEmptyRequiredMember_AndCanOpenAfterRestoration()
    {
        using var directory = CreateIndex(compound: false, out SegmentInfo info);
        string path = Path.Combine(_path, info.SegmentId + ".fdt");
        byte[] original = File.ReadAllBytes(path);
        Assert.NotEmpty(original);

        try
        {
            File.WriteAllBytes(path, []);

            Assert.Throws<InvalidDataException>(() =>
            {
                using var reader = new SegmentReader(directory, info);
            });
            Assert.Throws<InvalidDataException>(
                () => IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false));
        }
        finally
        {
            File.WriteAllBytes(path, original);
        }

        using var recoveredReader = new SegmentReader(directory, info);
        Assert.Equal(2, recoveredReader.MaxDoc);
        Assert.NotNull(recoveredReader.GetVector("embedding", 0));
    }

    private MMapDirectory CreateIndex(bool compound, out SegmentInfo info)
    {
        using (MMapDirectory writerDirectory = new(_path))
        using (var writer = new IndexWriter(writerDirectory, new IndexWriterConfig
        {
            UseCompoundFile = compound,
            BuildHnswOnFlush = true,
            NormaliseVectors = false,
        }))
        {
            for (int i = 0; i < 2; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", $"document {i}", stored: true));
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>([i + 1f, 1f, 0.5f])));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        info = Assert.Single(IndexRecovery.RecoverLatestCommit(_path, cleanupOrphans: false)!.SegmentInfos);
        return new MMapDirectory(_path);
    }

    private void RewriteCompoundWithoutMember(string segmentId, string missingFileName, string cfsPath)
    {
        string unpackedPath = Path.Combine(_path, $"cfs-rewrite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(unpackedPath);
        try
        {
            using (var sourceDirectory = new MMapDirectory(_path))
            using (var source = new CompoundSegmentFileSource(sourceDirectory, segmentId))
            {
                Assert.Contains(missingFileName, source.EnumerateFiles());
                foreach (string fileName in source.EnumerateFiles())
                {
                    if (string.Equals(fileName, missingFileName, StringComparison.Ordinal))
                        continue;

                    using var input = source.OpenInput(fileName);
                    byte[] bytes = input.ReadBytes(checked((int)input.Length));
                    File.WriteAllBytes(Path.Combine(unpackedPath, fileName), bytes);
                }
            }

            Assert.True(CompoundFileWriter.Pack(unpackedPath, segmentId));
            File.Copy(Path.Combine(unpackedPath, segmentId + ".cfs"), cfsPath, overwrite: true);
        }
        finally
        {
            Directory.Delete(unpackedPath, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_path))
            Directory.Delete(_path, recursive: true);
    }
}
