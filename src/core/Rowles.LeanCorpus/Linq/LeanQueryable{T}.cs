using System.Collections;
using System.Linq.Expressions;
using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Linq;

/// <summary>
/// LINQ queryable over a LeanCorpus index. Construct directly or obtain via
/// the source-generator-emitted <c>AsQueryable(IndexSearcher)</c> method.
/// </summary>
/// <typeparam name="TDocument">The mapped document model type.</typeparam>
public sealed class LeanQueryable<TDocument> : IOrderedQueryable<TDocument>
{
    private readonly LeanQueryProvider<TDocument> _provider;

    /// <summary>
    /// Initialises a new <see cref="LeanQueryable{TDocument}"/>.
    /// </summary>
    /// <param name="searcher">The index searcher that executes queries.</param>
    /// <param name="map">The typed document map for materialisation.</param>
    /// <param name="fieldResolver">
    /// Maps C# property names to field descriptors.
    /// When <c>null</c>, member accesses cannot be resolved and will throw.
    /// </param>
    public LeanQueryable(
        IndexSearcher searcher,
        LeanDocumentMap<TDocument> map,
        Func<string, IFieldDescriptor?>? fieldResolver)
        : this(searcher, map, fieldResolver, null)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="LeanQueryable{TDocument}"/> with optional
    /// <see cref="SearchOptions"/> for timeout, cancellation, and result budget.
    /// </summary>
    public LeanQueryable(
        IndexSearcher searcher,
        LeanDocumentMap<TDocument> map,
        Func<string, IFieldDescriptor?>? fieldResolver,
        SearchOptions? searchOptions)
    {
        _provider = new LeanQueryProvider<TDocument>(searcher, map, fieldResolver, searchOptions);
        Expression = Expression.Constant(this);
    }

    // Internal constructor used by CreateQuery to chain expressions.
    internal LeanQueryable(LeanQueryProvider<TDocument> provider, Expression expression)
    {
        _provider = provider;
        Expression = expression;
    }

    /// <inheritdoc/>
    public Type ElementType => typeof(TDocument);

    /// <inheritdoc/>
    public Expression Expression { get; }

    /// <inheritdoc/>
    public IQueryProvider Provider => _provider;

    /// <inheritdoc/>
    public IEnumerator<TDocument> GetEnumerator() => _provider.ExecuteEnumerable(Expression).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
