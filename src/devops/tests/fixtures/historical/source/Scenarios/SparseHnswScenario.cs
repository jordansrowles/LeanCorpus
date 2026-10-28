using System.Globalization;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.HistoricalFixtures;

internal static class SparseHnswScenario
{
    public static void Generate(string outputRoot)
    {

        if (typeof(IndexWriter).Assembly.GetName().Version != new Version(3, 1, 1, 0))
            throw new InvalidOperationException("This generator requires the LeanCorpus 3.1.1 package assembly.");
        if (Directory.Exists(outputRoot) && Directory.EnumerateFileSystemEntries(outputRoot).Any())
            throw new IOException($"Output directory '{outputRoot}' must be empty.");
        Directory.CreateDirectory(outputRoot);

        foreach (VectorQuantisation quantisation in new[] { VectorQuantisation.None, VectorQuantisation.Int8 })
        {
            foreach (bool compound in new[] { false, true })
            {
                string indexPath = Path.Combine(
                    outputRoot,
                    $"{(quantisation == VectorQuantisation.None ? "float32" : "int8")}-{(compound ? "compound" : "loose")}");
                Directory.CreateDirectory(indexPath);

                using var directory = new MMapDirectory(indexPath);
                var config = new IndexWriterConfig
                {
                    BuildHnswOnFlush = true,
                    HnswSeed = 42,
                    MaxBufferedDocs = 3,
                    MergePolicy = NoMergePolicy.Instance,
                    NormaliseVectors = false,
                    UseCompoundFile = compound,
                    VectorQuantisation = quantisation,
                };

                using (var writer = new IndexWriter(directory, config))
                {
                    Add(writer, 0, [0, 0]);
                    Add(writer, 1, [1, 0]);
                    Add(writer, 2, null);
                    writer.Commit();
                    Add(writer, 3, [0, 1]);
                    Add(writer, 4, [1, 1]);
                    Add(writer, 5, null);
                    writer.Commit();
                }
            }
        }

        FixtureOutput.WriteInventoryManifest(outputRoot, Path.Combine(outputRoot, "sparse-hnsw.manifest.json"));

        static void Add(IndexWriter writer, int id, float[]? vector)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", id.ToString(CultureInfo.InvariantCulture), stored: true));
            if (vector is not null)
                document.Add(new VectorField("embedding", vector));
            writer.AddDocument(document);
        }
    }
}
