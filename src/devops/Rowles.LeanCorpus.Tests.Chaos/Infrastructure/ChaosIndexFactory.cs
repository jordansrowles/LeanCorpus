using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

internal static class ChaosIndexFactory
{
    public static MMapDirectory CreateSimpleIndex(string rootPath, string name, int documentCount = 3)
    {
        var path = System.IO.Path.Combine(rootPath, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        for (int i = 0; i < documentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            document.Add(new TextField("body", $"hello world safety migration document {i}"));
            writer.AddDocument(document);
        }

        writer.Commit();
        return directory;
    }
}
