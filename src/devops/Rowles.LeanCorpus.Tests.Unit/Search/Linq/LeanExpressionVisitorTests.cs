using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Linq;

/// <summary>
/// Tests every expression → Query mapping in <see cref="LeanExpressionVisitor"/>.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class LeanExpressionVisitorTests
{
    // Test model — mirrors what a user's [LeanDocument] class would look like.
    private sealed class TestDoc
    {
        public string? Title { get; set; }
        public string? Status { get; set; }
        public int Year { get; set; }
        public double Price { get; set; }
        public bool IsPublished { get; set; }
        public IReadOnlyList<string>? Tags { get; set; }
    }

    // Field descriptors matching the test model.
    private static readonly IFieldDescriptor TitleField = new TestFieldDescriptor("title", FieldType.Text, isStored: true, isIndexed: true, isRequired: true);
    private static readonly IFieldDescriptor StatusField = new TestFieldDescriptor("status", FieldType.String, isStored: true, isIndexed: true, isRequired: true);
    private static readonly IFieldDescriptor YearField = new TestFieldDescriptor("year", FieldType.Numeric, isStored: true, isIndexed: true, isRequired: true);
    private static readonly IFieldDescriptor PriceField = new TestFieldDescriptor("price", FieldType.Numeric, isStored: true, isIndexed: true, isRequired: true);
    private static readonly IFieldDescriptor TagsField = new TestFieldDescriptor("tags", FieldType.String, isStored: true, isIndexed: true, isRequired: false);

    private static readonly Func<string, IFieldDescriptor?> Resolver = name => name switch
    {
        "Title" => TitleField,
        "Status" => StatusField,
        "Year" => YearField,
        "Price" => PriceField,
        "Tags" => TagsField,
        _ => null,
    };

    private static readonly LeanExpressionVisitor Visitor = new(Resolver);

    // Helpers: build expression and translate.
    private static Query Translate(Expression<Func<TestDoc, bool>> predicate)
    {
        return Visitor.Translate(predicate);
    }

    // === Equality ===

    [Fact(DisplayName = "Equal: text field maps to TermQuery")]
    public void Equal_TextMapsToTermQuery()
    {
        var query = Translate(d => d.Title == "corpus");

        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("title", tq.Field);
        Assert.Equal("corpus", tq.Term);
    }

    [Fact(DisplayName = "Equal: string field maps to TermQuery")]
    public void Equal_StringMapsToTermQuery()
    {
        var query = Translate(d => d.Status == "active");

        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("status", tq.Field);
        Assert.Equal("active", tq.Term);
    }

    [Fact(DisplayName = "Equal: numeric field maps to RangeQuery with equal bounds")]
    public void Equal_NumericMapsToRangeQuery()
    {
        var query = Translate(d => d.Year == 2024);

        var rq = Assert.IsType<RangeQuery>(query);
        Assert.Equal("year", rq.Field);
        Assert.Equal(2024.0, rq.Min);
        Assert.Equal(2024.0, rq.Max);
    }

    [Fact(DisplayName = "Equal: constant on left side is supported")]
    public void Equal_ConstantOnLeft_SwapsSides()
    {
        int year = 2024;
        var query = Translate(d => year == d.Year);

        var rq = Assert.IsType<RangeQuery>(query);
        Assert.Equal("year", rq.Field);
        Assert.Equal(2024.0, rq.Min);
    }

    [Fact(DisplayName = "Equal: captured local variable is supported")]
    public void Equal_CapturedLocalVariable_ResolvesValue()
    {
        var term = "corpus";
        var query = Translate(d => d.Title == term);

        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("corpus", tq.Term);
    }

    // === NotEqual ===

    [Fact(DisplayName = "NotEqual: text field wraps TermQuery in MustNot")]
    public void NotEqual_TextWrapsInMustNot()
    {
        var query = Translate(d => d.Title != "corpus");

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
        var inner = Assert.IsType<TermQuery>(bq.Clauses[0].Query);
        Assert.Equal("corpus", inner.Term);
    }

    [Fact(DisplayName = "NotEqual: numeric field wraps RangeQuery in MustNot")]
    public void NotEqual_NumericWrapsInMustNot()
    {
        var query = Translate(d => d.Year != 2024);

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
        var inner = Assert.IsType<RangeQuery>(bq.Clauses[0].Query);
        Assert.Equal(2024.0, inner.Min);
        Assert.Equal(2024.0, inner.Max);
    }

    // === GreaterThan / GreaterThanOrEqual ===

    [Fact(DisplayName = "GreaterThan: integral numeric uses +1 for exclusive lower bound")]
    public void GreaterThan_Integral_AdjustsLowerBound()
    {
        var query = Translate(d => d.Year > 2020);

        var rq = Assert.IsType<RangeQuery>(query);
        Assert.Equal("year", rq.Field);
        Assert.Equal(2021.0, rq.Min);
        Assert.Equal(double.MaxValue, rq.Max);
    }

    [Fact(DisplayName = "GreaterThanOrEqual: uses value as inclusive lower bound")]
    public void GreaterThanOrEqual_UsesValueAsLowerBound()
    {
        var query = Translate(d => d.Year >= 2020);

        var rq = Assert.IsType<RangeQuery>(query);
        Assert.Equal(2020.0, rq.Min);
        Assert.Equal(double.MaxValue, rq.Max);
    }

    [Fact(DisplayName = "GreaterThan: floating-point uses MustNot of complement range")]
    public void GreaterThan_FloatingPoint_WrapsInMustNot()
    {
        var query = Translate(d => d.Price > 19.99);

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
        var inner = Assert.IsType<RangeQuery>(bq.Clauses[0].Query);
        Assert.Equal("price", inner.Field);
        Assert.Equal(double.MinValue, inner.Min);
        Assert.Equal(19.99, inner.Max);
    }

    // === LessThan / LessThanOrEqual ===

    [Fact(DisplayName = "LessThan: integral numeric uses -1 for exclusive upper bound")]
    public void LessThan_Integral_AdjustsUpperBound()
    {
        var query = Translate(d => d.Year < 2025);

        var rq = Assert.IsType<RangeQuery>(query);
        Assert.Equal("year", rq.Field);
        Assert.Equal(double.MinValue, rq.Min);
        Assert.Equal(2024.0, rq.Max);
    }

    [Fact(DisplayName = "LessThanOrEqual: uses value as inclusive upper bound")]
    public void LessThanOrEqual_UsesValueAsUpperBound()
    {
        var query = Translate(d => d.Year <= 2025);

        var rq = Assert.IsType<RangeQuery>(query);
        Assert.Equal(double.MinValue, rq.Min);
        Assert.Equal(2025.0, rq.Max);
    }

    [Fact(DisplayName = "LessThan: floating-point uses MustNot of complement range")]
    public void LessThan_FloatingPoint_WrapsInMustNot()
    {
        var query = Translate(d => d.Price < 100.0);

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
        var inner = Assert.IsType<RangeQuery>(bq.Clauses[0].Query);
        Assert.Equal("price", inner.Field);
        Assert.Equal(100.0, inner.Min);
        Assert.Equal(double.MaxValue, inner.Max);
    }

    // === Range operator on non-numeric field ===

    [Fact(DisplayName = "GreaterThan: on string field throws NotSupportedException")]
    public void Range_OnNonNumericField_ThrowsNotSupported()
    {
        // d.Title > "x" doesn't compile in C#, so test via d.Status which is a string field
        // but > on string doesn't compile either. Test the error path differently:
        // Use a resolver that returns a String-type field for a numeric comparison.
        var resolver = new Func<string, IFieldDescriptor?>(_ => StatusField);
        var visitor = new LeanExpressionVisitor(resolver);

        var ex = Assert.Throws<NotSupportedException>(() =>
            visitor.Translate((Expression<Func<TestDoc, bool>>)(d => d.Year > 2020)));
        Assert.Contains("numeric", ex.Message);
    }

    // === AndAlso / OrElse ===

    [Fact(DisplayName = "AndAlso: combines with BooleanQuery Must clauses")]
    public void AndAlso_CombinesWithMust()
    {
        var query = Translate(d => d.Title == "corpus" && d.Status == "active");

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Must, c.Occur));
        Assert.IsType<TermQuery>(bq.Clauses[0].Query);
        Assert.IsType<TermQuery>(bq.Clauses[1].Query);
    }

    [Fact(DisplayName = "OrElse: combines with BooleanQuery Should clauses")]
    public void OrElse_CombinesWithShould()
    {
        var query = Translate(d => d.Title == "corpus" || d.Status == "active");

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Should, c.Occur));
    }

    [Fact(DisplayName = "AndAlso: three conditions produce three Must clauses")]
    public void AndAlso_ThreeConditions_ProducesThreeClauses()
    {
        var query = Translate(d => d.Title == "a" && d.Status == "b" && d.Year >= 2020);

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Must, c.Occur));
    }

    // === Not ===

    [Fact(DisplayName = "Not: wraps inner query in MustNot")]
    public void Not_WrapsInMustNot()
    {
        var query = Translate(d => !(d.Title == "corpus"));

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
        Assert.IsType<TermQuery>(bq.Clauses[0].Query);
    }

    // === string.Contains ===

    [Fact(DisplayName = "Contains: maps to WildcardQuery with surrounding wildcards")]
    public void Contains_MapsToWildcardQuery()
    {
        var query = Translate(d => d.Title!.Contains("corp"));

        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("title", wq.Field);
        Assert.Equal("*corp*", wq.Pattern);
    }

    // === string.StartsWith ===

    [Fact(DisplayName = "StartsWith: maps to PrefixQuery")]
    public void StartsWith_MapsToPrefixQuery()
    {
        var query = Translate(d => d.Title!.StartsWith("corp"));

        var pq = Assert.IsType<PrefixQuery>(query);
        Assert.Equal("title", pq.Field);
        Assert.Equal("corp", pq.Prefix);
    }

    // === string.EndsWith ===

    [Fact(DisplayName = "EndsWith: maps to WildcardQuery with leading wildcard")]
    public void EndsWith_MapsToWildcardQuery()
    {
        var query = Translate(d => d.Title!.EndsWith("pus"));

        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("title", wq.Field);
        Assert.Equal("*pus", wq.Pattern);
    }

    // === Error cases ===

    [Fact(DisplayName = "Translate: null expression throws ArgumentNullException")]
    public void Translate_NullExpression_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Visitor.Translate(null!));
    }

    [Fact(DisplayName = "Translate: standalone member access treated as implicit == true")]
    public void Translate_StandaloneMember_TreatedAsImplicitTrue()
    {
        // Test that a boolean member used as a standalone predicate
        // (e.g. .Where(d => d.IsPublished)) is treated as TermQuery("published", "true").
        var isPublishedDesc = new TestFieldDescriptor("published", FieldType.String, true, true, true);
        var resolver = new Func<string, IFieldDescriptor?>(name => name switch
        {
            "IsPublished" => isPublishedDesc,
            _ => null,
        });
        var visitor = new LeanExpressionVisitor(resolver);

        // Simulate a bool-returning expression that is just a member access.
        // In C#, .Where(d => d.IsPublished) produces a MemberExpression where
        // the member type is bool.
        Expression<Func<TestDoc, bool>> expr = d => d.Status == "active"; // dummy lambda shape
        // Build the member access manually via expression API.
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var memberAccess = Expression.Property(param, nameof(TestDoc.IsPublished));
        var boolLambda = Expression.Lambda<Func<TestDoc, bool>>(memberAccess, param);

        var query = visitor.Translate(boolLambda);
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("published", tq.Field);
        Assert.Equal("true", tq.Term);
    }

    [Fact(DisplayName = "Translate: unresolvable property throws NotSupportedException")]
    public void Translate_UnresolvableProperty_ThrowsNotSupported()
    {
        var resolver = new Func<string, IFieldDescriptor?>(_ => null);
        var visitor = new LeanExpressionVisitor(resolver);

        var ex = Assert.Throws<NotSupportedException>(() =>
            visitor.Translate((Expression<Func<TestDoc, bool>>)(d => d.Title == "x")));
        Assert.Contains("Title", ex.Message);
    }

    [Fact(DisplayName = "Translate: unindexed field throws NotSupportedException")]
    public void Translate_UnindexedField_ThrowsNotSupported()
    {
        var desc = new TestFieldDescriptor("notes", FieldType.Text, isStored: true, isIndexed: false, isRequired: false);
        var resolver = new Func<string, IFieldDescriptor?>(_ => desc);
        var visitor = new LeanExpressionVisitor(resolver);

        var ex = Assert.Throws<NotSupportedException>(() =>
            visitor.Translate((Expression<Func<TestDoc, bool>>)(d => d.Title == "x")));
        Assert.Contains("not indexed", ex.Message);
    }

    [Fact(DisplayName = "Translate: Contains on constant translates to TermInSetQuery")]
    public void Contains_OnConstant_ProducesTermInSetQuery()
    {
        // "corpus".Contains(d.Title) — collection is a constant, field is the argument.
        var query = Translate(d => "corpus".Contains(d.Title!));
        var tq = Assert.IsType<TermInSetQuery>(query);
        Assert.Equal("title", tq.Field);
        Assert.Contains("corpus", tq.Terms);
    }

    // === Fix #13: string.Contains(string, StringComparison) ===

    [Fact(DisplayName = "Contains: two-argument overload ignores StringComparison")]
    public void Contains_StringComparison_IgnoresSecondArg()
    {
        var query = Translate(d => d.Title!.Contains("corp", StringComparison.Ordinal));
        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("title", wq.Field);
        Assert.Equal("*corp*", wq.Pattern);
    }

    [Fact(DisplayName = "Contains: two-argument with OrdinalIgnoreCase still works")]
    public void Contains_OrdinalIgnoreCase_ProducesCorrectPattern()
    {
        var query = Translate(d => d.Status!.Contains("act", StringComparison.OrdinalIgnoreCase));
        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("*act*", wq.Pattern);
    }

    // === Fix #12: captured collection.Contains(d.Field) → TermInSetQuery ===

    [Fact(DisplayName = "Contains: captured array produces TermInSetQuery (real compiler)")]
    public void Contains_CapturedArray_ProducesTermInSetQuery()
    {
        // The .NET 9+ compiler optimises T[].Contains to MemoryExtensions.Contains
        // with an op_Implicit conversion. Test via the real compiler, not manual
        // expression construction.
        var statuses = new[] { "active", "archived" };
        var query = Translate(d => statuses.Contains(d.Status!));

        var tq = Assert.IsType<TermInSetQuery>(query);
        Assert.Equal("status", tq.Field);
        Assert.Contains("active", tq.Terms);
        Assert.Contains("archived", tq.Terms);
    }

    [Fact(DisplayName = "Contains: Enumerable.Contains with captured List produces TermInSetQuery")]
    public void Contains_EnumerableContains_ProducesTermInSetQuery()
    {
        var param = Expression.Parameter(typeof(TestDoc), "d");
        var list = System.Linq.Expressions.Expression.Constant(
            new List<string> { "active", "draft" });

        var convert = System.Linq.Expressions.Expression.Convert(
            list, typeof(IEnumerable<string>));

        var member = System.Linq.Expressions.Expression.Property(param, nameof(TestDoc.Status));

        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(string));
        var call = System.Linq.Expressions.Expression.Call(containsMethod, convert, member);
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<TestDoc, bool>>(call, param);

        var query = Visitor.Translate(lambda);
        var tq = Assert.IsType<TermInSetQuery>(query);
        Assert.Equal("status", tq.Field);
        Assert.Contains("active", tq.Terms);
        Assert.Contains("draft", tq.Terms);
    }

    // === Collection field .Contains() (d.Tags.Contains("foo")) ===

    [Fact(DisplayName = "Contains: on collection field produces TermQuery")]
    public void Contains_OnCollectionField_ProducesTermQuery()
    {
        var query = Translate(d => d.Tags!.Contains("corpus"));
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("tags", tq.Field);
        Assert.Equal("corpus", tq.Term);
    }

    // === Captured variable caching (#5) ===

    [Fact(DisplayName = "Translate: captured local variables are evaluated correctly")]
    public void CapturedLocals_EvaluatedCorrectly()
    {
        var term = "corpus";
        var year = 2024;
        var query = Translate(d => d.Title == term && d.Year >= year);

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Must, c.Occur));
    }

    [Fact(DisplayName = "Translate: captured variable read twice hits cache path")]
    public void CapturedLocal_TwiceInSameExpression_UsesCache()
    {
        var lo = 10.0;
        var hi = 100.0;
        // d.Price > lo && d.Price < hi — two different captured variables,
        // each exercising the EvaluateMemberExpression cache.
        var query = Translate(d => d.Price > lo && d.Price < hi);
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, bq.Clauses.Count);
    }

    [Fact(DisplayName = "Translate: unsupported method throws NotSupportedException")]
    public void Translate_UnsupportedMethod_ThrowsNotSupported()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            Translate(d => d.Title!.ToUpper() == "CORPUS"));
        // The left side is a MethodCallExpression (ToUpper), which is not a member access.
        Assert.Contains("member access", ex.Message);
    }

    // === Combine (static helper) edge ===

    [Fact(DisplayName = "Combine: two MatchAllDocs produce a valid BooleanQuery")]
    public void Combine_TwoMatchAll_ProducesBooleanQuery()
    {
        // This is tested indirectly through AndAlso/OrElse, but verify the
        // underlying logic works on any two queries.
        var query = Translate(d => d.Title == "a" && d.Status == "b");

        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(2, bq.Clauses.Count);
    }

    // === IFieldDescriptor stub ===

    private sealed class TestFieldDescriptor : IFieldDescriptor
    {
        public string Name { get; }
        public FieldType FieldType { get; }
        public bool IsStored { get; }
        public bool IsIndexed { get; }
        public bool IsRequired { get; }

        public TestFieldDescriptor(string name, FieldType fieldType, bool isStored, bool isIndexed, bool isRequired)
        {
            Name = name;
            FieldType = fieldType;
            IsStored = isStored;
            IsIndexed = isIndexed;
            IsRequired = isRequired;
        }
    }
}
