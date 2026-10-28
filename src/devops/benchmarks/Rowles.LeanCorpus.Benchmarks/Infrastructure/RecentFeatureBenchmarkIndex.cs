using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

internal static class RecentFeatureBenchmarkIndex
{
    internal static void Build(string path, IReadOnlyList<string> documents, bool useCompoundFile = false,
        int documentOffset = 0, int maxBufferedDocs = 1_000)
    {
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = maxBufferedDocs,
            RamBufferSizeMB = 256,
            UseCompoundFile = useCompoundFile
        });

        for (int i = 0; i < documents.Count; i++)
        {
            int id = checked(documentOffset + i);
            var document = new LeanDocument();
            document.Add(new StringField("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            document.Add(new TextField("body", documents[i], stored: true));
            document.Add(new StringField("category", $"category-{id % 32}", stored: false));
            document.Add(new NumericField("rank", id % 1_000, stored: false));
            writer.AddDocument(document);
        }

        writer.Commit();
    }

    internal static void Append(string path, IReadOnlyList<string> documents, int documentOffset,
        bool useCompoundFile = false)
    {
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            RamBufferSizeMB = 256,
            UseCompoundFile = useCompoundFile
        });

        for (int i = 0; i < documents.Count; i++)
        {
            int id = checked(documentOffset + i);
            var document = new LeanDocument();
            document.Add(new StringField("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            document.Add(new TextField("body", documents[i], stored: true));
            document.Add(new StringField("category", $"category-{id % 32}", stored: false));
            document.Add(new NumericField("rank", id % 1_000, stored: false));
            writer.AddDocument(document);
        }

        writer.Commit();
    }

    internal static void Delete(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
