using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Linq;

/// <summary>
/// Extension methods that add LINQ queryable support to
/// <see cref="LeanDocumentMap{TDocument}"/> and related types.
/// </summary>
public static class LinqExtensions
{
    /// <summary>
    /// Wraps a <see cref="LeanDocumentMap{TDocument}"/> and
    /// <see cref="IndexSearcher"/> as an <see cref="IQueryable{T}"/>,
    /// enabling LINQ query syntax and lambda predicates.
    /// </summary>
    /// <typeparam name="TDocument">The mapped document model type.</typeparam>
    /// <param name="map">The typed document map.</param>
    /// <param name="searcher">The index searcher that executes queries.</param>
    /// <param name="fieldResolver">
    /// Maps C# property names to field descriptors. The source generator emits
    /// a switch-expression resolver automatically. When <c>null</c>, member
    /// accesses in predicates cannot be resolved and will throw.
    /// </param>
    /// <returns>An <see cref="IQueryable{T}"/> over the index.</returns>
    public static LeanQueryable<TDocument> AsQueryable<TDocument>(
        this LeanDocumentMap<TDocument> map,
        IndexSearcher searcher,
        Func<string, IFieldDescriptor?>? fieldResolver = null)
    {
        return new LeanQueryable<TDocument>(searcher, map, fieldResolver);
    }

    /// <summary>
    /// Wraps a <see cref="LeanDocumentMap{TDocument}"/> and
    /// <see cref="IndexSearcher"/> as an <see cref="IQueryable{T}"/>,
    /// with <see cref="SearchOptions"/> for timeout, cancellation,
    /// and result-byte budget.
    /// </summary>
    public static LeanQueryable<TDocument> AsQueryable<TDocument>(
        this LeanDocumentMap<TDocument> map,
        IndexSearcher searcher,
        Func<string, IFieldDescriptor?>? fieldResolver,
        SearchOptions searchOptions)
    {
        return new LeanQueryable<TDocument>(searcher, map, fieldResolver, searchOptions);
    }
}
