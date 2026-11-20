namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Shared corpus for nested-query composition tests. Two parent/child blocks precede four
/// recipe documents. Every recipe carries text, keyword, numeric, geo and XY fields so each
/// executable query family has at least one positive match.
/// </summary>
internal static class QueryCompositionCorpus
{
    public static void Write(MMapDirectory directory, bool multiSegment, IndexWriterConfig? config = null)
    {
        using var writer = new IndexWriter(directory, config ?? new IndexWriterConfig());
        writer.AddDocumentBlock([Child("red"), Child("blue"), Parent("p1", "red apple", [1f, 0f])]);
        if (multiSegment)
            writer.Commit();
        writer.AddDocumentBlock([Child("green"), Child("green", "large"), Parent("p2", "green pear", [0f, 1f])]);
        if (multiSegment)
            writer.Commit();
        writer.AddDocument(Recipe("r1", "vegan bowl", 1, ["vegan"]));
        writer.AddDocument(Recipe("r2", "breakfast plate", 5, ["fruehstueck"]));
        if (multiSegment)
        {
            writer.Commit();
            // A segment whose only document is deleted exercises empty live segments.
            writer.AddDocument(Recipe("gone", "discarded entry", 7, []));
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "gone"));
            writer.Commit();
        }

        writer.AddDocument(Recipe("r3", "vegan breakfast plate", 1, ["vegan", "fruehstueck"]));
        writer.AddDocument(Recipe("r4", "plain toast", 9, []));
        writer.Commit();
    }

    public static BooleanQuery Bool(params (Query Query, Occur Occur)[] clauses)
    {
        var builder = new BooleanQuery.Builder();
        foreach (var (query, occur) in clauses)
            builder.Add(query, occur);
        return builder.Build();
    }

    public static string[] Ids(IndexSearcher searcher, TopDocs docs)
        => docs.ScoreDocs.Select(doc => Id(searcher, doc.DocId)).Order(StringComparer.Ordinal).ToArray();

    public static string Id(IndexSearcher searcher, int docId)
    {
        var stored = searcher.GetStoredFields(docId);
        if (stored.TryGetValue("id", out var values) && values.Count > 0)
            return values[0];
        return stored.TryGetValue("sku", out var skus) && skus.Count > 0
            ? "child:" + string.Join('+', skus)
            : $"doc:{docId}";
    }

    public static LeanDocument Child(params string[] skus)
    {
        var document = new LeanDocument();
        document.Add(new StringField("type", "child"));
        foreach (string sku in skus)
            document.Add(new StringField("sku", sku, stored: true));
        return document;
    }

    public static LeanDocument Parent(string id, string title, float[] vector)
    {
        var document = new LeanDocument();
        document.Add(new StringField("type", "parent"));
        document.Add(new StringField("id", id, stored: true));
        document.Add(new TextField("title", title, stored: true));
        document.Add(new VectorField("vec", vector));
        return document;
    }

    public static LeanDocument Recipe(string id, string title, int value, string[] tags)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new StringField("type", "recipe"));
        document.Add(new TextField("title", title, stored: true));
        document.Add(new NumericField("number", value));
        document.Add(new Int64Field("integer", value));
        document.Add(new GeoPointField("geo", value, value));
        document.Add(new XYPointField("xy", value, value));
        foreach (string tag in tags)
            document.Add(new StringField("tag_id", tag));
        return document;
    }
}
