using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

internal static class IndexSchemaValidator
{
    private static readonly HashSet<string> BuiltInAnalysers = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard", "keyword", "en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko", "sk"
    };

    internal static void Validate(IndexSchema schema, IndexTopologySettings topology, MutableIndexSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(topology);
        if (topology.ShardCount != 1 || topology.ReplicaCount != 0)
            throw new ArgumentException("Community Server requires exactly one shard and zero replicas.", nameof(topology));
        if (schema.Fields is null || schema.Fields.Count == 0 || schema.Analysis is null)
            throw new ArgumentException("An index schema must contain at least one field.", nameof(schema));

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (IndexFieldDefinition field in schema.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name) || field.Name.Length > 256 || field.Name.Any(char.IsControl))
                throw new ArgumentException($"Field name '{field.Name}' is invalid.", nameof(schema));
            if (field.Name is ServerDocumentMapper.DocumentIdField or ServerDocumentMapper.RawDocumentField)
                throw new ArgumentException($"Field name '{field.Name}' is reserved by the server.", nameof(schema));
            if (!names.Add(field.Name))
                throw new ArgumentException($"Field '{field.Name}' is defined more than once.", nameof(schema));
            if (!Enum.IsDefined(field.Type))
                throw new ArgumentException($"Field '{field.Name}' has an unknown type.", nameof(schema));
            if (field.Type == IndexFieldType.Binary && field.Indexed)
                throw new ArgumentException($"Binary field '{field.Name}' cannot be indexed.", nameof(schema));
            if (field.Type == IndexFieldType.Vector && (field.VectorDimensions is not > 0))
                throw new ArgumentException($"Vector field '{field.Name}' must define positive dimensions.", nameof(schema));
            if (field.Type == IndexFieldType.Vector && field.MultiValued)
                throw new ArgumentException($"Vector field '{field.Name}' cannot be multi-valued.", nameof(schema));
            if (field.Type == IndexFieldType.Vector && !field.Indexed)
                throw new ArgumentException($"Vector field '{field.Name}' must be indexed.", nameof(schema));
            if (field.Type != IndexFieldType.Vector && field.VectorDimensions is not null)
                throw new ArgumentException($"Only vector field '{field.Name}' may define dimensions.", nameof(schema));
            if (field.Type != IndexFieldType.Text && field.Analyser is not null)
                throw new ArgumentException($"Only text field '{field.Name}' may define an analyser.", nameof(schema));
            if (field.Analyser is not null && !BuiltInAnalysers.Contains(field.Analyser) && !schema.Analysis.ContainsKey(field.Analyser))
                throw new ArgumentException($"Analyser '{field.Analyser}' for field '{field.Name}' is not defined.", nameof(schema));
        }

        foreach ((string name, AnalysisDefinition definition) in schema.Analysis)
        {
            if (string.IsNullOrWhiteSpace(name) || definition is null || string.IsNullOrWhiteSpace(definition.Tokeniser))
                throw new ArgumentException("Analysis definitions require a name and tokeniser.", nameof(schema));
            if (name.Length > 128 || name.Any(char.IsControl) || name is ServerDocumentMapper.DocumentIdField or ServerDocumentMapper.RawDocumentField)
                throw new ArgumentException($"Analysis name '{name}' is invalid.", nameof(schema));
        }

        if (settings?.DefaultField is { } defaultField)
        {
            if (!names.Contains(defaultField))
                throw new ArgumentException($"Default field '{defaultField}' is not present in the schema.", nameof(settings));
            IndexFieldDefinition defaultDefinition = schema.Fields.First(field => string.Equals(field.Name, defaultField, StringComparison.Ordinal));
            if (!defaultDefinition.Indexed || defaultDefinition.Type != IndexFieldType.Text)
                throw new ArgumentException($"Default field '{defaultField}' must be an indexed text field.", nameof(settings));
        }
        if (settings?.MaximumQueryClauses is <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "Maximum query clauses must be positive.");
        if (settings?.CommitInterval is { } commit && commit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(settings), "Commit interval must be positive.");
        if (settings?.RefreshInterval is { } refresh && refresh <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(settings), "Refresh interval must be positive.");
    }
}
