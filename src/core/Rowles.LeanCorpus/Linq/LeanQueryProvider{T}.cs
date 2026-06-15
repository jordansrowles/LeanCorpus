using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Linq;

/// <summary>
/// Executes LINQ expression trees against a LeanCorpus <see cref="IndexSearcher"/>.
/// Translates <c>Where</c> predicates into <see cref="Search.Query"/> objects via
/// <see cref="LeanExpressionVisitor"/>, executes searches, and materialises results
/// through <see cref="LeanDocumentMap{TDocument}.FromStoredDocument"/>.
/// </summary>
/// <typeparam name="TDocument">The mapped document model type.</typeparam>
public sealed class LeanQueryProvider<TDocument> : IQueryProvider
{
    private readonly IndexSearcher _searcher;
    private readonly LeanDocumentMap<TDocument> _map;
    private readonly Func<string, IFieldDescriptor?>? _fieldResolver;
    private readonly SearchOptions? _searchOptions;
    private ISet<string>? _storedFieldNames;

    // Cache compiled Select projections keyed by expression shape.
    // Static — shared across all TDocument instantiations. The default
    // ConcurrentDictionary reference-equality comparer prevents collisions
    // between identically-shaped selectors on different document types.
    // Lifetime is AppDomain-scoped; acceptable for typical workloads.
    private static readonly ConcurrentDictionary<Expression, Delegate> s_projectionCache = new();

    // Cache boxed wrapper delegates for the fallback projection path (uncommon element types).
    private static readonly ConcurrentDictionary<Expression, Func<TDocument, object?>> s_boxedWrapperCache = new();

    /// <summary>
    /// Initialises a new <see cref="LeanQueryProvider{TDocument}"/>.
    /// </summary>
    public LeanQueryProvider(
        IndexSearcher searcher,
        LeanDocumentMap<TDocument> map,
        Func<string, IFieldDescriptor?>? fieldResolver,
        SearchOptions? searchOptions = null)
    {
        _searcher = searcher;
        _map = map;
        _fieldResolver = fieldResolver;
        _searchOptions = searchOptions;
    }

    /// <inheritdoc/>
    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        if (typeof(TElement) == typeof(TDocument))
            return (IQueryable<TElement>)(object)new LeanQueryable<TDocument>(this, expression);

        return new LeanProjectedQueryable<TElement>(this, expression);
    }

    /// <inheritdoc/>
    public IQueryable CreateQuery(Expression expression)
        => CreateQuery<TDocument>(expression);

    /// <inheritdoc/>
    public TResult Execute<TResult>(Expression expression)
    {
        return (TResult)ExecuteCore(expression)!;
    }

    /// <inheritdoc/>
    public object? Execute(Expression expression)
    {
        return ExecuteCore(expression);
    }

    /// <summary>
    /// Materialises the query as an enumerable sequence, used by
    /// <see cref="LeanQueryable{TDocument}.GetEnumerator"/>.
    /// </summary>
    internal IEnumerable<TDocument> ExecuteEnumerable(Expression expression)
    {
        var (query, _, topN, offset, sr) = ParseExpression(expression);
        return ExecuteAndMaterialise(query, topN, offset, sr);
    }

    private object? ExecuteCore(Expression expression)
    {
        if (expression is not MethodCallExpression call)
        {
            var (query, _, n, off, so) = ParseExpression(expression);
            return ExecuteAndMaterialise(query, n, off, so);
        }

        // Count and Any accept inline predicates and return scalars.
        switch (call.Method.Name)
        {
            case "Count":
                return ExecuteCount(call);
            case "Any":
                return ExecuteCount(call) > 0;
            case "LongCount":
                return (long)ExecuteCount(call);
            // Select / Take / Skip appearing as the terminal means a projected
            // queryable is being enumerated (ToList/foreach calls GetEnumerator
            // directly, stripping the ToList wrapper).
            case "Select":
            {
                var (q, _, tk, sk, sr0) = ParseExpression(call.Arguments[0]);
                var sel = GetLambda(call.Arguments[1]);
                return ExecuteToList(q, sel, tk, sk, sr0);
            }
            case "Take":
            {
                var (q, sel, tk, sk, sr1) = ParseExpression(call.Arguments[0]);
                var terminalTake = GetIntConstant(call.Arguments[1]);
                return ExecuteToList(q, sel, Math.Min(tk, terminalTake), sk, sr1);
            }
            case "Skip":
            {
                var (q, sel, tk, sk, sr2) = ParseExpression(call.Arguments[0]);
                var terminalSkip = GetIntConstant(call.Arguments[1]);
                return ExecuteToList(q, sel, tk, sk + terminalSkip, sr2);
            }
            // Where appearing as the terminal means a projected queryable
            // was chained with .Where() after .Select().
            case "Where":
            {
                var (q, sel, tk, sk, sr3) = ParseExpression(call.Arguments[0]);
                var predicateLambda = GetLambda(call.Arguments[1]);
                var visitor = new LeanExpressionVisitor(_fieldResolver);
                var predicateQuery = visitor.Translate(predicateLambda);
                var combined = Combine(q, predicateQuery);
                return ExecuteToList(combined, sel, tk, sk, sr3);
            }
        }

        // Parse the source expression chain.
        var (sourceQ, selectLambda, top, offs, sr) = ParseExpression(call.Arguments[0]);

        return call.Method.Name switch
        {
            "ToList" => ExecuteToList(sourceQ, selectLambda, top, offs, sr),
            "ToArray" => ExecuteToArray(sourceQ, selectLambda, top, offs, sr),
            "First" => ExecuteFirst(sourceQ, selectLambda, top, offs, sr),
            "FirstOrDefault" => ExecuteFirstOrDefault(sourceQ, selectLambda, top, offs, sr),
            "Single" => ExecuteSingle(sourceQ, selectLambda, top, offs, sr),
            "SingleOrDefault" => ExecuteSingleOrDefault(sourceQ, selectLambda, top, offs, sr),
            "Last" => ExecuteLast(sourceQ, selectLambda, top, offs, sr),
            "LastOrDefault" => ExecuteLastOrDefault(sourceQ, selectLambda, top, offs, sr),
            "ElementAt" => ExecuteElementAt(sourceQ, selectLambda, offs, sr, GetIntConstant(call.Arguments[1])),
            "ElementAtOrDefault" => ExecuteElementAtOrDefault(sourceQ, selectLambda, offs, sr, GetIntConstant(call.Arguments[1])),
            _ => throw new NotSupportedException(
                $"The LINQ operator '{call.Method.Name}' is not supported as a terminal operation. " +
                "End your query with .ToList(), .First(), .FirstOrDefault(), .Single(), .SingleOrDefault(), .Count(), or .Any()."),
        };
    }

    private object ExecuteToList(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        var source = ExecuteAndMaterialise(query, take, skip, sort);
        return selector is not null ? ApplyProjection(source, selector) : source;
    }

    [SuppressMessage("AOT", "IL3050", Justification = "List<TElement> is guaranteed to exist at the call site via Execute<TResult>.")]
    private object ExecuteToArray(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        var list = (System.Collections.IList)ExecuteToList(query, selector, take, skip, sort);
        var elementType = list.Count > 0 ? list[0]!.GetType() : typeof(TDocument);
        var array = Array.CreateInstance(elementType, list.Count);
        list.CopyTo(array, 0);
        return array;
    }

    private object ExecuteFirst(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, 1, skip, sort);
            if (list.Count == 0)
                throw new InvalidOperationException("The source sequence is empty.");
            return list[0]!;
        }

        var results = ExecuteAndMaterialise(query, 1, skip, sort);
        if (results.Count == 0)
            throw new InvalidOperationException("The source sequence is empty.");
        return results[0]!;
    }

    private object? ExecuteFirstOrDefault(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, 1, skip, sort);
            return list.Count > 0 ? list[0] : null;
        }

        var results = ExecuteAndMaterialise(query, 1, skip, sort);
        return results.Count > 0 ? results[0] : null;
    }

    private object ExecuteSingle(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, 2, skip, sort);
            return list.Count switch
            {
                0 => throw new InvalidOperationException("The source sequence is empty."),
                1 => list[0]!,
                _ => throw new InvalidOperationException("The source sequence contains more than one element."),
            };
        }

        var results = ExecuteAndMaterialise(query, 2, skip, sort);
        return results.Count switch
        {
            0 => throw new InvalidOperationException("The source sequence is empty."),
            1 => results[0]!,
            _ => throw new InvalidOperationException("The source sequence contains more than one element."),
        };
    }

    private object? ExecuteSingleOrDefault(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, 2, skip, sort);
            return list.Count switch
            {
                0 => null,
                1 => list[0],
                _ => throw new InvalidOperationException("The source sequence contains more than one element."),
            };
        }

        var results = ExecuteAndMaterialise(query, 2, skip, sort);
        return results.Count switch
        {
            0 => null,
            1 => results[0],
            _ => throw new InvalidOperationException("The source sequence contains more than one element."),
        };
    }

    private object ExecuteLast(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, DefaultMaxResults, skip, sort);
            if (list.Count == 0)
                throw new InvalidOperationException("The source sequence is empty.");
            return list[list.Count - 1]!;
        }

        var results = ExecuteAndMaterialise(query, DefaultMaxResults, skip, sort);
        if (results.Count == 0)
            throw new InvalidOperationException("The source sequence is empty.");
        return results[^1]!;
    }

    private object? ExecuteLastOrDefault(Query query, LambdaExpression? selector, int take, int skip, SortField? sort = null)
    {
        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, DefaultMaxResults, skip, sort);
            return list.Count > 0 ? list[list.Count - 1] : null;
        }

        var results = ExecuteAndMaterialise(query, DefaultMaxResults, skip, sort);
        return results.Count > 0 ? results[^1] : null;
    }

    private object ExecuteElementAt(Query query, LambdaExpression? selector, int skip, SortField? sort, int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index), "ElementAt index must be non-negative.");

        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, index + 1, skip, sort);
            if (index >= list.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "ElementAt index is out of range.");
            return list[index]!;
        }

        var results = ExecuteAndMaterialise(query, index + 1, skip, sort);
        if (index >= results.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "ElementAt index is out of range.");
        return results[index]!;
    }

    private object? ExecuteElementAtOrDefault(Query query, LambdaExpression? selector, int skip, SortField? sort, int index)
    {
        if (index < 0)
            return default;

        if (selector is not null)
        {
            var list = (System.Collections.IList)ExecuteToList(query, selector, index + 1, skip, sort);
            return index < list.Count ? list[index] : null;
        }

        var results = ExecuteAndMaterialise(query, index + 1, skip, sort);
        return index < results.Count ? results[index] : null;
    }

    private int ExecuteCount(MethodCallExpression call)
    {
        var args = call.Arguments;

        // Count() with no predicate.
        if (args.Count == 1)
        {
            var (query, _, _, _, _) = ParseExpression(args[0]);
            // Count uses TotalHits — avoid SearchOptions budgeted path
            // which would allocate a huge collector for int.MaxValue.
            return _searcher.Search(query, topN: int.MaxValue).TotalHits;
        }

        // Count(predicate).
        if (args.Count == 2)
        {
            var predicateLambda = GetLambda(args[1]);
            var visitor = new LeanExpressionVisitor(_fieldResolver);
            var predicateQuery = visitor.Translate(predicateLambda);
            var (baseQuery, _, _, _, _) = ParseExpression(args[0]);
            return _searcher.Search(Combine(baseQuery, predicateQuery), topN: int.MaxValue).TotalHits;
        }

        throw new NotSupportedException("Count() with unsupported arguments.");
    }

    /// <summary>Default cap on results when no Take() is specified.</summary>
    private const int DefaultMaxResults = 10_000;

    /// <summary>
    /// Parses an expression tree into its constituent parts.
    /// </summary>
    private (Query Query, LambdaExpression? Selector, int Take, int Skip, SortField? Sort) ParseExpression(Expression expression)
    {
        Query? baseQuery = null;
        LambdaExpression? selector = null;
        int take = int.MaxValue;
        int skip = 0;
        SortField? sort = null;

        Expression current = expression;

        while (current is MethodCallExpression call)
        {
            switch (call.Method.Name)
            {
                case "Where":
                {
                    var whereLambda = GetLambda(call.Arguments[1]);
                    var visitor = new LeanExpressionVisitor(_fieldResolver);
                    var whereQuery = visitor.Translate(whereLambda);
                    baseQuery = baseQuery is null ? whereQuery : Combine(baseQuery, whereQuery);
                    current = call.Arguments[0];
                    break;
                }
                case "Select":
                {
                    if (selector is null)
                        selector = GetLambda(call.Arguments[1]);
                    current = call.Arguments[0];
                    break;
                }
                case "Take":
                {
                    take = GetIntConstant(call.Arguments[1]);
                    current = call.Arguments[0];
                    break;
                }
                case "Skip":
                {
                    skip = GetIntConstant(call.Arguments[1]);
                    current = call.Arguments[0];
                    break;
                }
                case "OrderBy":
                case "OrderByDescending":
                {
                    var orderLambda = GetLambda(call.Arguments[1]);
                    var desc = call.Method.Name == "OrderByDescending";
                    sort = BuildSortField(orderLambda, desc);
                    current = call.Arguments[0];
                    break;
                }
                default:
                    return Finalise(current, baseQuery, selector, take, skip, sort);
            }
        }

        return Finalise(current, baseQuery, selector, take, skip, sort);
    }

    /// <summary>
    /// Resolves the final expression state, unwrapping a captured
    /// <see cref="IQueryable"/> constant that may carry additional predicates.
    /// </summary>
    private (Query, LambdaExpression?, int, int, SortField?) Finalise(
        Expression current,
        Query? baseQuery,
        LambdaExpression? selector,
        int take,
        int skip,
        SortField? sort)
    {
        // Unwrap a captured IQueryable<T> variable (ConstantExpression wrapping the queryable).
        // This recovers predicates from earlier .Where() calls on the captured variable.
        if (current is ConstantExpression { Value: IQueryable captured })
        {
            var capturedExpr = captured.Expression;

            // Base case: the captured Expression is itself a ConstantExpression wrapping
            // the root queryable (no further predicates). Stop recursing.
            if (capturedExpr is ConstantExpression { Value: IQueryable })
                return (baseQuery ?? new Search.Queries.MatchAllDocsQuery(), selector, take, skip, sort);

            var (innerQuery, innerSelector, innerTake, innerSkip, innerSort) =
                ParseExpression(capturedExpr);

            var combinedQuery = baseQuery is null
                ? innerQuery
                : Combine(baseQuery, innerQuery);

            return (
                combinedQuery,
                selector ?? innerSelector,
                Math.Min(take, innerTake),
                skip + innerSkip,
                sort ?? innerSort
            );
        }

        return (
            baseQuery ?? new Search.Queries.MatchAllDocsQuery(),
            selector,
            take,
            skip,
            sort
        );
    }

    /// <summary>
    /// Builds a <see cref="SortField"/> from an ordering lambda such as
    /// <c>d => d.Year</c> or <c>d => d.Title</c>.
    /// </summary>
    private SortField BuildSortField(LambdaExpression orderLambda, bool descending)
    {
        if (orderLambda.Body is not MemberExpression member)
            throw new NotSupportedException(
                "OrderBy and OrderByDescending must select a single mapped field, e.g. .OrderBy(d => d.Year).");

        string propertyName = member.Member.Name;
        IFieldDescriptor? descriptor = null;

        if (_fieldResolver is not null)
            descriptor = _fieldResolver(propertyName);

        if (descriptor is null)
            throw new NotSupportedException(
                $"Property '{propertyName}' is not recognised as a mapped LeanCorpus field and cannot be used for ordering. " +
                "Ensure the property is decorated with a [LeanNumeric] or [LeanString] attribute, or add it to the field resolver delegate.");

        var sortType = descriptor.FieldType switch
        {
            FieldType.Numeric => SortFieldType.Numeric,
            FieldType.String or FieldType.Text => SortFieldType.String,
            _ => throw new NotSupportedException(
                $"Field '{descriptor.Name}' of type {descriptor.FieldType} cannot be used for ordering. " +
                "Only Numeric, String, and Text fields support sorting."),
        };

        return new SortField(sortType, descriptor.Name, descending);
    }

    /// <summary>
    /// Executes the search and materialises <typeparamref name="TDocument"/> instances.
    /// </summary>
    private List<TDocument> ExecuteAndMaterialise(Query query, int take, int skip, SortField? sort = null)
    {
        // Cap unbounded fetches to avoid OOM on match-all queries.
        int cappedTake = take == int.MaxValue ? DefaultMaxResults : take;
        int fetchCount = cappedTake + skip;

        TopDocs topDocs;
        if (_searchOptions is not null)
        {
            topDocs = sort is not null
                ? _searcher.Search(query, fetchCount, sort) // Sort + SearchOptions not currently combined
                : _searcher.Search(query, fetchCount, _searchOptions);
        }
        else if (sort is not null)
        {
            topDocs = _searcher.Search(query, fetchCount, sort);
        }
        else
        {
            topDocs = _searcher.Search(query, fetchCount);
        }
        var scoreDocs = topDocs.ScoreDocs;

        // Apply skip.
        if (skip > 0 && skip < scoreDocs.Length)
        {
            var dest = new ScoreDoc[scoreDocs.Length - skip];
            Array.Copy(scoreDocs, skip, dest, 0, dest.Length);
            scoreDocs = dest;
        }
        else if (skip >= scoreDocs.Length)
        {
            return [];
        }

        // Apply take.
        if (take < scoreDocs.Length)
        {
            var dest = new ScoreDoc[take];
            Array.Copy(scoreDocs, dest, take);
            scoreDocs = dest;
        }

        // Materialise each hit.
        var fieldNames = GetStoredFieldNames();
        var results = new List<TDocument>(scoreDocs.Length);
        foreach (var sd in scoreDocs)
        {
            var storedFields = _searcher.GetStoredFields(sd.DocId, fieldNames);
            var stored = StoredDocument.Create(storedFields, null);
            results.Add(_map.FromStoredDocument(stored));
        }

        return results;
    }

    /// <summary>
    /// Applies a Select projection to materialised documents.
    /// Uses a type switch over common projection element types to avoid
    /// <c>MakeGenericType</c>; falls back to it with an AOT suppression
    /// for uncommon types (which the trimmer preserves because the call
    /// site in <c>Execute&lt;TResult&gt;</c> references the exact type).
    /// The fallback compiles a boxed wrapper delegate instead of using
    /// <see cref="Delegate.DynamicInvoke"/> — one compilation per unique
    /// element type, not per element.
    /// </summary>
    [SuppressMessage("AOT", "IL3050", Justification = "The projected List<TElement> type is guaranteed by the Execute<TResult> call site.")]
    private static object ApplyProjection(List<TDocument> source, LambdaExpression selector)
    {
        var compiled = s_projectionCache.GetOrAdd(
            selector,
            static s => ((LambdaExpression)s).Compile(preferInterpretation: true));

        var elementType = selector.ReturnType;

        // Identity projection — Select(d => d) — is a no-op.
        if (elementType == typeof(TDocument))
            return source;

        // Fast paths for common projection types — avoids MakeGenericType.
        if (elementType == typeof(string))
            return ProjectToList(source, (Func<TDocument, string>)compiled);
        if (elementType == typeof(int))
            return ProjectToList(source, (Func<TDocument, int>)compiled);
        if (elementType == typeof(long))
            return ProjectToList(source, (Func<TDocument, long>)compiled);
        if (elementType == typeof(double))
            return ProjectToList(source, (Func<TDocument, double>)compiled);
        if (elementType == typeof(float))
            return ProjectToList(source, (Func<TDocument, float>)compiled);
        if (elementType == typeof(bool))
            return ProjectToList(source, (Func<TDocument, bool>)compiled);
        if (elementType == typeof(DateTime))
            return ProjectToList(source, (Func<TDocument, DateTime>)compiled);
        if (elementType == typeof(DateTimeOffset))
            return ProjectToList(source, (Func<TDocument, DateTimeOffset>)compiled);
        if (elementType == typeof(Guid))
            return ProjectToList(source, (Func<TDocument, Guid>)compiled);
        if (elementType == typeof(decimal))
            return ProjectToList(source, (Func<TDocument, decimal>)compiled);
        if (elementType == typeof(short))
            return ProjectToList(source, (Func<TDocument, short>)compiled);
        if (elementType == typeof(byte))
            return ProjectToList(source, (Func<TDocument, byte>)compiled);
        if (elementType == typeof(DateOnly))
            return ProjectToList(source, (Func<TDocument, DateOnly>)compiled);
        if (elementType == typeof(TimeOnly))
            return ProjectToList(source, (Func<TDocument, TimeOnly>)compiled);

        // Fallback for uncommon types. Compile a boxed wrapper once so that
        // per-element calls are Direct (no DynamicInvoke boxing).
        var wrapper = CompileBoxedWrapper(selector, compiled);

        var listType = typeof(List<>).MakeGenericType(elementType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;

        foreach (var doc in source)
            list.Add(wrapper(doc)!);

        return list;
    }

    /// <summary>
    /// Compiles a <c>Func&lt;TDocument, object?&gt;</c> that calls the
    /// selector delegate and boxes the result. Used only for the fallback
    /// projection path when the element type is not in the fast switch.
    /// The compiled wrapper is cached in <see cref="s_projectionCache"/>
    /// keyed by the original selector expression.
    /// </summary>
    private static Func<TDocument, object?> CompileBoxedWrapper(
        LambdaExpression selector, Delegate compiledDelegate)
    {
        return s_boxedWrapperCache.GetOrAdd(selector, _ =>
        {
            var param = Expression.Parameter(typeof(TDocument), "doc");
            var invoke = Expression.Invoke(Expression.Constant(compiledDelegate), param);
            var body = Expression.Convert(invoke, typeof(object));
            return Expression.Lambda<Func<TDocument, object?>>(body, param)
                .Compile(preferInterpretation: true);
        });
    }

    private static List<T> ProjectToList<T>(List<TDocument> source, Func<TDocument, T> projection)
    {
        var list = new List<T>(source.Count);
        foreach (var doc in source)
            list.Add(projection(doc));
        return list;
    }

    /// <summary>
    /// Returns the set of stored field names from the document map.
    /// Computed once and cached; passed to <see cref="IndexSearcher.GetStoredFields(int, ISet{string}?)"/>
    /// to avoid loading unnecessary fields from disk.
    /// </summary>
    private ISet<string>? GetStoredFieldNames()
    {
        if (_storedFieldNames is not null)
            return _storedFieldNames.Count > 0 ? _storedFieldNames : null;

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in _map.Fields)
        {
            if (f.IsStored)
                names.Add(f.Name);
        }

        _storedFieldNames = names;
        return names.Count > 0 ? names : null;
    }

    private static Query Combine(Query left, Query right)
    {
        return new Search.Queries.BooleanQuery.Builder()
            .Add(left, Search.Occur.Must)
            .Add(right, Search.Occur.Must)
            .Build();
    }

    private static LambdaExpression GetLambda(Expression expression)
    {
        // Unwrap Quote nodes.
        while (expression is UnaryExpression { NodeType: ExpressionType.Quote } quote)
            expression = quote.Operand;

        // Direct lambda (the common path; inline predicates).
        if (expression is LambdaExpression lambda)
            return lambda;

        // The expression is a constant wrapping a pre-built Expression<Func<...>>.
        if (expression is ConstantExpression { Value: LambdaExpression constLambda })
            return constLambda;

        // The expression is a field or property on a closure class or a static field
        // that holds a captured lambda. Instead of using reflection (FieldInfo.GetValue)
        // which is AOT-hostile, we compile a tiny field-access expression via the
        // interpreter — this is AOT-safe because the closure type is statically
        // referenced by the user's lambda body.
        if (expression is MemberExpression member)
        {
            // Compile a trivial "() => member" delegate via the interpreter path.
            var accessor = Expression.Lambda<Func<object?>>(
                Expression.Convert(member, typeof(object))
            ).Compile(preferInterpretation: true);

            if (accessor() is LambdaExpression capturedLambda)
                return capturedLambda;
        }

        throw new NotSupportedException(
            $"Expected a lambda expression but found '{expression.NodeType}'. " +
            "Pass a lambda such as 'd => d.Field == value'.");
    }

    private static int GetIntConstant(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            expression = convert.Operand;

        if (expression is ConstantExpression constant && constant.Value is int i)
            return i;

        throw new NotSupportedException(
            $"Expected an integer constant for Take/Skip but found '{expression.NodeType}'.");
    }
}
