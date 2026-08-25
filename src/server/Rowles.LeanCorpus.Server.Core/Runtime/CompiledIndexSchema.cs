using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using ServerFieldType = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldType;
using ServerIndexSchema = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexSchema;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

internal sealed class CompiledIndexSchema
{
    private CompiledIndexSchema(
        ServerIndexSchema source,
        Rowles.LeanCorpus.Index.Indexer.IndexSchema engineSchema,
        IReadOnlyDictionary<string, CompiledFieldDefinition> fields)
    {
        Source = source;
        EngineSchema = engineSchema;
        Fields = fields;
    }

    internal ServerIndexSchema Source { get; }

    internal Rowles.LeanCorpus.Index.Indexer.IndexSchema EngineSchema { get; }

    internal IReadOnlyDictionary<string, CompiledFieldDefinition> Fields { get; }

    internal static CompiledIndexSchema Create(ServerIndexSchema schema, Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexTopologySettings topology, Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.MutableIndexSettings settings)
    {
        IndexSchemaValidator.Validate(schema, topology, settings);
        Dictionary<string, CompiledFieldDefinition> fields = new(StringComparer.Ordinal);
        Rowles.LeanCorpus.Index.Indexer.IndexSchema engine = new() { StrictMode = true };
        engine.Add(new FieldMapping(ServerDocumentMapper.DocumentIdField, FieldType.String) { IsStored = true, IsIndexed = true });
        engine.Add(new FieldMapping(ServerDocumentMapper.RawDocumentField, FieldType.Text) { IsStored = true, IsIndexed = true });

        foreach (var definition in schema.Fields)
        {
            IAnalyser? analyser = definition.Type == ServerFieldType.Text ? ResolveAnalyser(definition.Analyser, schema) : null;
            FieldType engineType = !definition.Indexed && definition.Type != ServerFieldType.Binary
                ? FieldType.Stored
                : ToEngineType(definition.Type);
            CompiledFieldDefinition compiled = new(definition, analyser);
            fields.Add(definition.Name, compiled);
            engine.Add(new FieldMapping(definition.Name, engineType)
            {
                IsStored = definition.Stored,
                IsIndexed = definition.Indexed,
                Analyser = analyser
            });
        }

        return new CompiledIndexSchema(schema, engine, fields);
    }

    private static FieldType ToEngineType(ServerFieldType type) => type switch
    {
        ServerFieldType.Text => FieldType.Text,
        ServerFieldType.Keyword => FieldType.String,
        ServerFieldType.Int64 => FieldType.Int64,
        ServerFieldType.Double => FieldType.Numeric,
        ServerFieldType.Boolean => FieldType.String,
        ServerFieldType.DateTime => FieldType.Int64,
        ServerFieldType.Binary => FieldType.Binary,
        ServerFieldType.Vector => FieldType.Vector,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown server field type.")
    };

    private static IAnalyser ResolveAnalyser(string? name, ServerIndexSchema schema)
    {
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, "standard", StringComparison.OrdinalIgnoreCase))
            return new StandardAnalyser();
        if (string.Equals(name, "keyword", StringComparison.OrdinalIgnoreCase))
            return new KeywordAnalyser();
        if (schema.Analysis.ContainsKey(name))
            return new StandardAnalyser();
        return AnalyserFactory.Create(name);
    }
}

internal sealed class CompiledFieldDefinition(
    Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldDefinition source,
    IAnalyser? analyser)
{
    internal Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexFieldDefinition Source { get; } = source;
    internal IAnalyser? Analyser { get; } = analyser;
}
