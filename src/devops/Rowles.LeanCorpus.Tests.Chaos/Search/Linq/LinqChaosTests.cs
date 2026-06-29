using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Search.Linq;

/// <summary>
/// Chaos / property-based tests for the LINQ queryable layer.
/// Verifies that randomly-generated queries produce deterministic results
/// and that edge cases do not crash the expression visitor.
/// </summary>
[Trait("Category", "Chaos")]
[Trait("Category", "Search")]
public sealed class LinqChaosTests
{

    private static string CreatePath(string suffix) =>
        Path.Combine(Path.GetTempPath(), "LeanCorpusLinqChaos", $"{suffix}_{Guid.NewGuid():N}");

    // ---- test model ----

    private sealed class Widget
    {
        public string? Name { get; set; }
        public string? Colour { get; set; }
        public int Quantity { get; set; }
    }

    private sealed class WidgetMap : LeanDocumentMap<Widget>
    {
        public override string DocumentName => "widget";
        public override bool StrictSchema => true;
        public override IReadOnlyList<LeanFieldBinding<Widget>> Fields { get; } = new[]
        {
            new LeanFieldBinding<Widget>("name", FieldType.Text, true, true, true),
            new LeanFieldBinding<Widget>("colour", FieldType.String, true, true, true),
            new LeanFieldBinding<Widget>("qty", FieldType.Numeric, true, true, true),
        };
        public override LeanDocument ToDocument(Widget w) => throw new NotSupportedException();
        public override Widget FromStoredDocument(StoredDocument d) => new()
        {
            Name = d.GetFirst("name"),
            Colour = d.GetFirst("colour"),
            Quantity = d.GetFirst("qty") is { } s && int.TryParse(s, out var q) ? q : 0,
        };
        public override IndexSchema CreateSchema(bool strict)
        {
            var s = new IndexSchema { StrictMode = strict };
            foreach (var f in Fields) s.Add(new FieldMapping(f.Name, f.FieldType) { IsStored = f.IsStored, IsIndexed = f.IsIndexed, IsRequired = f.IsRequired });
            return s;
        }
    }

    private static readonly IFieldDescriptor NameDesc   = new SimpleDescriptor("name",   FieldType.Text,    true, true, true);
    private static readonly IFieldDescriptor ColourDesc = new SimpleDescriptor("colour", FieldType.String,  true, true, true);
    private static readonly IFieldDescriptor QtyDesc    = new SimpleDescriptor("qty",    FieldType.Numeric, true, true, true);

    private static readonly Func<string, IFieldDescriptor?> Resolver = name => name switch
    {
        "Name"     => NameDesc,
        "Colour"   => ColourDesc,
        "Quantity" => QtyDesc,
        _ => null,
    };

    private static int _indexCounter;

    private LeanQueryable<Widget> BuildIndex(string name, int docCount)
    {
        var path = CreatePath(name + "_" + Interlocked.Increment(ref _indexCounter));
        Directory.CreateDirectory(path);
        var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var rng = new Random(42);
        string[] colours = ["red", "green", "blue", "yellow"];
        string[] names = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta"];

        for (int i = 0; i < docCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("name", names[rng.Next(names.Length)], stored: true));
            doc.Add(new StringField("colour", colours[rng.Next(colours.Length)], stored: true));
            doc.Add(new NumericField("qty", rng.Next(1, 100), stored: true));
            writer.AddDocument(doc);
        }
        writer.Commit();

        var searcher = new IndexSearcher(dir);
        return new WidgetMap().AsQueryable(searcher, Resolver);
    }

    // ---- Determinism ----

    [Fact(DisplayName = "LINQ Chaos: repeated queries return identical results")]
    public void RepeatedQueries_ReturnIdenticalResults()
    {
        var queryable = BuildIndex(nameof(RepeatedQueries_ReturnIdenticalResults), 50);

        var first = queryable
            .Where(w => w.Colour == "red" && w.Quantity > 20)
            .ToList();

        var second = queryable
            .Where(w => w.Colour == "red" && w.Quantity > 20)
            .ToList();

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Name, second[i].Name);
    }

    [Fact(DisplayName = "LINQ Chaos: compound OR returns union")]
    public void CompoundOr_ReturnsUnion()
    {
        var queryable = BuildIndex(nameof(CompoundOr_ReturnsUnion), 30);

        var results = queryable
            .Where(w => w.Colour == "red" || w.Colour == "blue")
            .ToList();

        Assert.NotEmpty(results);
        Assert.All(results, w => Assert.True(w.Colour is "red" or "blue"));
    }

    [Fact(DisplayName = "LINQ Chaos: NOT excludes matching documents")]
    public void Not_ExcludesMatchingDocuments()
    {
        var queryable = BuildIndex(nameof(Not_ExcludesMatchingDocuments), 30);

        var all = queryable.ToList();
        var excluded = queryable.Where(w => w.Colour != "red").ToList();

        Assert.True(excluded.Count < all.Count);
        Assert.All(excluded, w => Assert.NotEqual("red", w.Colour));
    }

    [Fact(DisplayName = "LINQ Chaos: Take and Skip are deterministic")]
    public void TakeAndSkip_Deterministic()
    {
        var queryable = BuildIndex(nameof(TakeAndSkip_Deterministic), 30);

        var first = queryable.Where(w => w.Quantity > 10).Take(3).ToList();
        var second = queryable.Where(w => w.Quantity > 10).Take(3).ToList();

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
            Assert.Equal(first[i].Name, second[i].Name);
    }

    [Fact(DisplayName = "LINQ Chaos: chained Where produces deterministic intersection")]
    public void ChainedWhere_Deterministic()
    {
        var queryable = BuildIndex(nameof(ChainedWhere_Deterministic), 40);

        var first = queryable
            .Where(w => w.Colour == "green")
            .Where(w => w.Quantity > 30)
            .ToList();

        var second = queryable
            .Where(w => w.Colour == "green")
            .Where(w => w.Quantity > 30)
            .ToList();

        Assert.Equal(first.Count, second.Count);
    }

    // ---- Edge cases ----

    [Fact(DisplayName = "LINQ Chaos: empty result set does not throw")]
    public void EmptyResultSet_DoesNotThrow()
    {
        var queryable = BuildIndex(nameof(EmptyResultSet_DoesNotThrow), 10);

        var results = queryable
            .Where(w => w.Name == "nonexistent-value-xyz")
            .ToList();

        Assert.Empty(results);
    }

    [Fact(DisplayName = "LINQ Chaos: Count on empty result returns zero")]
    public void CountOnEmpty_ReturnsZero()
    {
        var queryable = BuildIndex(nameof(CountOnEmpty_ReturnsZero), 10);

        var count = queryable
            .Where(w => w.Name == "nonexistent-value-xyz")
            .Count();

        Assert.Equal(0, count);
    }

    [Fact(DisplayName = "LINQ Chaos: Any on empty result returns false")]
    public void AnyOnEmpty_ReturnsFalse()
    {
        var queryable = BuildIndex(nameof(AnyOnEmpty_ReturnsFalse), 10);

        var any = queryable
            .Where(w => w.Name == "nonexistent-value-xyz")
            .Any();

        Assert.False(any);
    }

    [Fact(DisplayName = "LINQ Chaos: expression visitor does not throw on valid trees")]
    public void ExpressionVisitor_ValidTrees_DoesNotThrow()
    {
        var resolver = Resolver;
        var visitor = new LeanExpressionVisitor(resolver);

        // Test a variety of expression shapes.
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Colour == "red"));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Quantity > 50));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Quantity >= 1));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Quantity < 100));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Quantity <= 99));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Colour == "red" && w.Quantity > 20));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Colour == "red" || w.Colour == "blue"));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => !(w.Colour == "red")));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Name!.Contains("ph")));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Name!.StartsWith("al")));
        visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Name!.EndsWith("ta")));
    }

    [Fact(DisplayName = "LINQ Chaos: unsupported expressions throw NotSupportedException")]
    public void UnsupportedExpressions_ThrowNotSupported()
    {
        var resolver = Resolver;
        var visitor = new LeanExpressionVisitor(resolver);

        Assert.Throws<NotSupportedException>(() =>
            visitor.Translate((System.Linq.Expressions.Expression<Func<Widget, bool>>)(w => w.Name == null!)));
    }

    private sealed class SimpleDescriptor : IFieldDescriptor
    {
        public string Name { get; }
        public FieldType FieldType { get; }
        public bool IsStored { get; }
        public bool IsIndexed { get; }
        public bool IsRequired { get; }

        public SimpleDescriptor(string name, FieldType fieldType, bool isStored, bool isIndexed, bool isRequired)
        {
            Name = name;
            FieldType = fieldType;
            IsStored = isStored;
            IsIndexed = isIndexed;
            IsRequired = isRequired;
        }
    }
}
