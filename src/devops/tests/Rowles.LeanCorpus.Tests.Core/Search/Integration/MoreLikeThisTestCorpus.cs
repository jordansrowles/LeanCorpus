using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>Shared deterministic corpus for the MoreLikeThis cache tests.</summary>
internal static class MoreLikeThisTestCorpus
{
    internal const int DocumentCount = 400;
    private static readonly string[] Words =
    [
        "market", "government", "economy", "reform", "policy", "growth", "trade", "budget", "tax", "bank",
        "export", "import", "labour", "wages", "energy", "oil", "climate", "science", "school", "health",
        "court", "law", "vote", "party", "minister", "council", "city", "rural", "farm", "harvest"
    ];

    internal static int DocId(IndexSearcher searcher, int id)
        => searcher.Search(new TermQuery("id", id.ToString(CultureInfo.InvariantCulture)), 1, TestContext.Current.CancellationToken).ScoreDocs[0].DocId;

    /// <summary>Creates a new index directory under <paramref name="root"/> holding the corpus in <paramref name="segments"/> segments.</summary>
    internal static MMapDirectory Build(string root, string name, int segments)
    {
        var directory = new MMapDirectory(NewPath(root, name));
        Write(directory, 0, DocumentCount, segments);
        return directory;
    }

    internal static string NewPath(string root, string name)
    {
        string path = Path.Combine(root, name + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    internal static void Write(MMapDirectory directory, int start, int count, int segments)
    {
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = (int)Math.Ceiling(count / (double)segments),
            MergePolicy = NoMergePolicy.Instance,
            StoreTermVectors = true
        });
        for (int i = start; i < start + count; i++)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", i.ToString(CultureInfo.InvariantCulture), stored: true));
            document.Add(new TextField("title", Text(i, 3, 3)));
            document.Add(new TextField("body", Text(i, 12, 7)));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    // Deterministic text: a skewed choice of words with repeats, so frequencies and document
    // frequencies vary widely.
    private static string Text(int i, int length, int salt)
    {
        var words = new string[length];
        for (int w = 0; w < length; w++)
        {
            int pick = (int)((((long)(i + 1) * 2654435761L) + (w * 40503L) + salt) % 997);
            words[w] = Words[(pick * pick) % Words.Length];
        }
        return string.Join(' ', words);
    }
}
