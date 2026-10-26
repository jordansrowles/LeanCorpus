using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

if (args.Length != 2) throw new ArgumentException("Expected write/read mode and index path.");
string mode = args[0];
string indexPath = args[1];
if (mode == "write")
{
    using var directory = new MMapDirectory(indexPath);
    using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
    for (int i = 1; i <= 3; i++)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", $"doc-{i}", stored: true));
        document.Add(new StringField("kind", i == 2 ? "selected" : "other", stored: false));
        document.Add(new StoredField("title", $"deterministic title {i}"));
        document.Add(new StoredField("source", "package-consumer"));
        writer.AddDocument(document);
    }
    writer.Commit();
    Console.WriteLine("CORE write passed with three documents.");
}
else if (mode == "read")
{
    using var directory = new MMapDirectory(indexPath);
    using var searcher = new IndexSearcher(directory);
    var hits = searcher.Search(new TermQuery("kind", "selected"), 10);
    if (hits.ScoreDocs.Length != 1) throw new InvalidDataException($"Expected one hit, found {hits.ScoreDocs.Length}.");
    var stored = searcher.GetStoredFields(hits.ScoreDocs[0].DocId);
    if (stored["id"].Single() != "doc-2" ||
        stored["title"].Single() != "deterministic title 2" ||
        stored["source"].Single() != "package-consumer")
        throw new InvalidDataException("Stored values did not match the deterministic document.");
    Console.WriteLine("CORE read passed with one exact hit and expected stored values.");
}
else throw new ArgumentException("Expected write or read mode.");