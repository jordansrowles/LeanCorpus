using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
public sealed class VectorMergeGenerationQualificationTests : IDisposable
{
    private const int Dimension = 4;
    private readonly string _path = RepositoryTemporaryStorage.NewPath("tests", "vector-merge-generations-");

    public VectorMergeGenerationQualificationTests() => Directory.CreateDirectory(_path);

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Theory]
    [InlineData(VectorQuantisation.None, false)]
    [InlineData(VectorQuantisation.None, true)]
    public void FourMergeGenerationsPreserveExactFloat32Vectors(
        VectorQuantisation quantisation,
        bool compound)
    {
        string sourcePath = Path.Combine(_path, "source");
        string targetPath = Path.Combine(_path, "target");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(targetPath);
        using var sourceDirectory = new MMapDirectory(sourcePath);
        using var targetDirectory = new MMapDirectory(targetPath);
        var originalVectors = new Dictionary<string, float[]?>(StringComparer.Ordinal);
        var deletedIds = new HashSet<string>(StringComparer.Ordinal);

        using (var sourceWriter = new IndexWriter(sourceDirectory, CreateConfig(quantisation, compound)))
        {
            AddVectorDocuments(sourceWriter, originalVectors, firstId: 0, count: 24);
            sourceWriter.Commit();
        }

        using var writer = new IndexWriter(targetDirectory, CreateConfig(quantisation, compound));
        writer.AddIndexes(sourceDirectory);
        AddVectorDocuments(writer, originalVectors, firstId: 24, count: 24);
        writer.Commit();
        deletedIds.Add(FormatId(7));
        writer.DeleteDocuments(new TermQuery("id", FormatId(7)));
        Assert.Equal(2, IndexRecovery.RecoverLatestCommit(targetPath, cleanupOrphans: false)!.SegmentInfos.Count);

        for (int generation = 1; generation <= 4; generation++)
        {
            int lastId = 47;
            if (generation > 1)
            {
                int firstId = (generation * 16) + 16;
                AddVectorDocuments(writer, originalVectors, firstId, count: 16);
                writer.Commit();
                int deletedId = firstId + 7;
                deletedIds.Add(FormatId(deletedId));
                writer.DeleteDocuments(new TermQuery("id", FormatId(deletedId)));
                lastId = firstId + 15;
            }

            Assert.True(writer.ForceMerge(1) > 0, $"Generation {generation} did not merge any segments.");
            writer.Commit();

            SegmentInfo merged = Assert.Single(
                IndexRecovery.RecoverLatestCommit(targetPath, cleanupOrphans: false)!.SegmentInfos);
            Assert.Equal(compound, merged.IsCompoundFile);
            Assert.Empty(Directory.GetDirectories(targetPath, ".merge-*"));
            AssertMergedDocumentOrderAndVectors(targetDirectory, merged, originalVectors, deletedIds, lastId, quantisation);

        }
    }

    private static IndexWriterConfig CreateConfig(VectorQuantisation quantisation, bool compound)
        => new()
        {
            MaxBufferedDocs = 24,
            MergePolicy = NoMergePolicy.Instance,
            MergeThreshold = 100,
            BuildHnswOnFlush = false,
            NormaliseVectors = false,
            UseCompoundFile = compound,
            VectorQuantisation = quantisation,
            IndexSort = new IndexSort(SortField.String("id")),
        };

    private static void AddVectorDocuments(
        IndexWriter writer,
        Dictionary<string, float[]?> originalVectors,
        int firstId,
        int count)
    {
        for (int id = firstId; id < firstId + count; id++)
        {
            string documentId = FormatId(id);
            float[]? vector = CreateVector(id);
            originalVectors.Add(documentId, vector);
            var document = new LeanDocument();
            document.Add(new StringField("id", documentId, stored: true));
            if (vector is not null)
                document.Add(new VectorField("embedding", new ReadOnlyMemory<float>(vector)));
            writer.AddDocument(document);
        }
    }

    private static float[]? CreateVector(int id)
    {
        if (id != 0 && id % 9 == 0)
            return null;
        if (id == 0)
            return new float[Dimension];

        int group = id % Dimension;
        int rank = id / Dimension;
        var vector = new float[Dimension];
        vector[group] = 0.6f + rank * 0.025f;
        for (int component = 0; component < Dimension; component++)
        {
            if (component == group)
                continue;
            int noise = ((id * 7 + component * 11) % 9) - 4;
            vector[component] = noise * 0.015f;
        }
        return vector;
    }

    private static void AssertMergedDocumentOrderAndVectors(
        MMapDirectory directory,
        SegmentInfo merged,
        IReadOnlyDictionary<string, float[]?> originalVectors,
        IReadOnlySet<string> deletedIds,
        int lastId,
        VectorQuantisation quantisation)
    {
        using var reader = new SegmentReader(directory, merged);
        string[] expectedIds = originalVectors.Keys
            .Where(id => ParseId(id) <= lastId && !deletedIds.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds.Length, reader.MaxDoc);
        for (int localDocId = 0; localDocId < expectedIds.Length; localDocId++)
        {
            string actualId = reader.GetStoredFields(localDocId)["id"].Single();
            Assert.Equal(expectedIds[localDocId], actualId);
            float[]? expectedVector = originalVectors[actualId];
            if (expectedVector is null)
            {
                Assert.False(reader.HasVector("embedding", localDocId));
                Assert.Null(reader.GetVector("embedding", localDocId));
            }
            else
            {
                Assert.True(reader.HasVector("embedding", localDocId));
                float[]? actualVector = reader.GetVector("embedding", localDocId);
                Assert.NotNull(actualVector);
                Assert.Equal(expectedVector, actualVector);
            }
        }
    }

    private static string FormatId(int id) => $"doc-{id:D3}";

    private static int ParseId(string id) => int.Parse(id.AsSpan("doc-".Length));

}
