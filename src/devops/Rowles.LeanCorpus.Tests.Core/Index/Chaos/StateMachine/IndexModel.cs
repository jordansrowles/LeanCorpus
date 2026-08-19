using System.Collections.Immutable;
using System.Globalization;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed record ModelDocument(string Id, string Category, int Price, string Body)
{
    private static readonly string[] Categories = ["alpha", "beta", "gamma"];

    public static ModelDocument Create(int number)
    {
        string category = Categories[number % Categories.Length];
        string bodyTerm = number % 2 == 0 ? "alpha" : "beta";
        return new ModelDocument(
            $"doc-{number.ToString(CultureInfo.InvariantCulture)}",
            category,
            (number * 17 + 3) % 101,
            $"{category} {bodyTerm} document");
    }

    public ModelDocument Replacement() => this with
    {
        Category = "updated",
        Price = (Price + 37) % 101,
        Body = "updated replacement document"
    };

    public LeanDocument ToLeanDocument()
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", Id));
        document.Add(new StringField("category", Category));
        document.Add(new NumericField("price", Price));
        document.Add(new TextField("body", Body));
        return document;
    }
}

internal enum SearchKind
{
    MatchAll,
    Category,
    BodyTerm,
    PriceRange
}

internal sealed record SearchSpec(
    SearchKind Kind,
    string? Value = null,
    double Min = 0,
    double Max = 0,
    bool IncludeMin = true,
    bool IncludeMax = true)
{
    public static IReadOnlyList<SearchSpec> Cases { get; } =
    [
        new(SearchKind.MatchAll),
        new(SearchKind.Category, "alpha"),
        new(SearchKind.Category, "beta"),
        new(SearchKind.Category, "updated"),
        new(SearchKind.BodyTerm, "alpha"),
        new(SearchKind.BodyTerm, "beta"),
        new(SearchKind.BodyTerm, "replacement"),
        new(SearchKind.BodyTerm, "missing"),
        new(SearchKind.PriceRange, Min: 0, Max: 100),
        new(SearchKind.PriceRange, Min: 20, Max: 70, IncludeMin: true, IncludeMax: false),
        new(SearchKind.PriceRange, Min: 50, Max: 50),
        new(SearchKind.PriceRange, Min: 101, Max: 120)
    ];

    public Query ToQuery() => Kind switch
    {
        SearchKind.MatchAll => new MatchAllDocsQuery(),
        SearchKind.Category => new TermQuery("category", Value!),
        SearchKind.BodyTerm => new TermQuery("body", Value!),
        SearchKind.PriceRange => new RangeQuery("price", Min, Max, IncludeMin, IncludeMax),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown search kind.")
    };

    public bool Matches(ModelDocument document) => Kind switch
    {
        SearchKind.MatchAll => true,
        SearchKind.Category => string.Equals(document.Category, Value, StringComparison.Ordinal),
        SearchKind.BodyTerm => document.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(Value!, StringComparer.Ordinal),
        SearchKind.PriceRange =>
            (IncludeMin ? document.Price >= Min : document.Price > Min) &&
            (IncludeMax ? document.Price <= Max : document.Price < Max),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown search kind.")
    };

    public override string ToString() => Kind switch
    {
        SearchKind.MatchAll => "MatchAll",
        SearchKind.Category => $"Category={Value}",
        SearchKind.BodyTerm => $"BodyTerm={Value}",
        SearchKind.PriceRange => $"PriceRange={(IncludeMin ? '[' : '(')}" +
            $"{Min.ToString(CultureInfo.InvariantCulture)}..{Max.ToString(CultureInfo.InvariantCulture)}" +
            $"{(IncludeMax ? ']' : ')')}",
        _ => Kind.ToString()
    };
}

internal sealed record IndexModel(
    ImmutableDictionary<string, ModelDocument> Working,
    ImmutableDictionary<string, ModelDocument> Committed,
    int NextId)
{
    public static IndexModel Empty { get; } = new(
        CreateDocumentMap(),
        CreateDocumentMap(),
        0);

    public IndexModel Add(ModelDocument document) => this with
    {
        Working = Working.Add(document.Id, document),
        NextId = NextId + 1
    };

    public IndexModel AddBatch(IReadOnlyList<ModelDocument> documents)
    {
        var working = Working;
        foreach (var document in documents)
            working = working.Add(document.Id, document);

        return this with
        {
            Working = working,
            NextId = NextId + documents.Count
        };
    }

    public IndexModel Delete(string id) => this with { Working = Working.Remove(id) };

    public IndexModel Update(ModelDocument replacement) => this with
    {
        // IndexWriter.UpdateDocument applies the delete to the existing segment
        // before queuing the replacement document. It also flushes and applies
        // earlier pending deletes, so those Working deletions become visible at
        // this point as well. The replacement is Working-only until the next
        // commit, while the old committed document is no longer visible.
        Working = Working.SetItem(replacement.Id, replacement),
        Committed = RemoveWorkingDeletionsAndReplacement(replacement.Id)
    };

    public IndexModel Commit() => this with { Committed = Working };

    public IndexModel Reopen() => this with { Working = Committed };

    private static ImmutableDictionary<string, ModelDocument> CreateDocumentMap() =>
        ImmutableDictionary.Create<string, ModelDocument>(StringComparer.Ordinal);

    private ImmutableDictionary<string, ModelDocument> RemoveWorkingDeletionsAndReplacement(string replacementId)
    {
        var committed = Committed;
        foreach (string id in Committed.Keys)
        {
            if (!Working.ContainsKey(id) || string.Equals(id, replacementId, StringComparison.Ordinal))
                committed = committed.Remove(id);
        }

        return committed;
    }
}
