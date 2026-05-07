using Rowles.LeanLucene.Document;

namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Defines per-field type expectations and validation rules for an index.
/// When set on <see cref="IndexWriterConfig.Schema"/>, documents are validated at
/// <see cref="IndexWriter.AddDocument"/> time.
/// </summary>
public sealed class IndexSchema
{
    private readonly Dictionary<string, FieldMapping> _mappings = new(StringComparer.Ordinal);

    /// <summary>All registered field mappings.</summary>
    public IReadOnlyDictionary<string, FieldMapping> Mappings => _mappings;

    /// <summary>
    /// When true, documents containing fields not defined in the schema are rejected.
    /// When false, unknown fields are silently accepted. Default: false.
    /// </summary>
    public bool StrictMode { get; init; }

    /// <summary>Adds a field mapping to the schema. Returns <c>this</c> for fluent chaining.</summary>
    public IndexSchema Add(FieldMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        _mappings[mapping.Name] = mapping;
        return this;
    }

    /// <summary>
    /// Validates a document against this schema.
    /// Throws <see cref="SchemaValidationException"/> on the first violation found.
    /// </summary>
    public void Validate(LeanDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Check for required fields
        foreach (var (name, mapping) in _mappings)
        {
            if (mapping.IsRequired && document.GetField(name) is null)
                throw new SchemaValidationException($"Required field '{name}' is missing.");
        }

        // Check each field in the document
        foreach (var field in document.Fields)
        {
            if (_mappings.TryGetValue(field.Name, out var mapping))
            {
                // Type check
                if (field.FieldType != mapping.FieldType)
                    throw new SchemaValidationException(
                        $"Field '{field.Name}' has type {field.FieldType} but schema expects {mapping.FieldType}.");
            }
            else if (StrictMode)
            {
                throw new SchemaValidationException(
                    $"Field '{field.Name}' is not defined in the schema (strict mode).");
            }
        }
    }
}
