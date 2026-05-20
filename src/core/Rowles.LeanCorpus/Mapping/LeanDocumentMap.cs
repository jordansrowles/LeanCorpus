using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Mapping;

/// <summary>
/// Abstract base produced by the LeanCorpus source generator for typed document mapping.
/// Subclasses are emitted automatically from <c>[LeanDocument]</c>-decorated types and
/// route to direct-construction code paths with no reflection.
/// </summary>
/// <typeparam name="TDocument">The mapped document model type.</typeparam>
public abstract class LeanDocumentMap<TDocument>
{
    /// <summary>The field bindings exposed by this map, in declaration order.</summary>
    public abstract IReadOnlyList<LeanFieldBinding<TDocument>> Fields { get; }

    /// <summary>The logical document name used in diagnostics and schema construction.</summary>
    public abstract string DocumentName { get; }

    /// <summary>Whether the generated schema is built with <see cref="IndexSchema.StrictMode"/>.</summary>
    public abstract bool StrictSchema { get; }

    /// <summary>
    /// Projects a typed model into a <see cref="LeanDocument"/>.
    /// </summary>
    /// <param name="value">The model instance to project.</param>
    /// <returns>A populated <see cref="LeanDocument"/>.</returns>
    public abstract LeanDocument ToDocument(TDocument value);

    /// <summary>
    /// Materialises a typed model from a <see cref="StoredDocument"/> snapshot.
    /// </summary>
    /// <param name="document">The stored-field snapshot.</param>
    /// <returns>A populated model instance.</returns>
    /// <exception cref="NotSupportedException">Thrown when the generator could not produce a materialiser for this type.</exception>
    public abstract TDocument FromStoredDocument(StoredDocument document);

    /// <summary>
    /// Builds an <see cref="IndexSchema"/> from the generated field bindings.
    /// </summary>
    /// <param name="strict">When <c>true</c>, the schema rejects fields it does not know about.</param>
    /// <returns>A populated <see cref="IndexSchema"/>.</returns>
    public abstract IndexSchema CreateSchema(bool strict = true);
}
