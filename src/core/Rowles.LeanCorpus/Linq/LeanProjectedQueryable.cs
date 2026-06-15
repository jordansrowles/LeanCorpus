using System.Collections;
using System.Linq.Expressions;

namespace Rowles.LeanCorpus.Linq;

/// <summary>
/// A LINQ queryable for projected results where the element type
/// differs from the original document type.
/// Created by <see cref="LeanQueryProvider{TDocument}.CreateQuery{TElement}"/>
/// when a <c>Select</c> changes the element type.
/// </summary>
/// <typeparam name="TElement">The projected element type.</typeparam>
internal sealed class LeanProjectedQueryable<TElement> : IOrderedQueryable<TElement>
{
    private readonly IQueryProvider _provider;
    private readonly Expression _expression;

    internal LeanProjectedQueryable(IQueryProvider provider, Expression expression)
    {
        _provider = provider;
        _expression = expression;
    }

    /// <inheritdoc/>
    public Type ElementType => typeof(TElement);

    /// <inheritdoc/>
    public Expression Expression => _expression;

    /// <inheritdoc/>
    public IQueryProvider Provider => _provider;

    /// <inheritdoc/>
    public IEnumerator<TElement> GetEnumerator()
        => _provider.Execute<IEnumerable<TElement>>(_expression).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
