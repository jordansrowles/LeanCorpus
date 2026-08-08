using System.Security.Cryptography;
using System.Text;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Calculates a stable hash for immutable schema and topology settings.</summary>
internal static class SchemaHash
{
    internal static string Compute(IndexSchema schema, IndexTopologySettings topology)
    {
        StringBuilder text = new();
        foreach (IndexFieldDefinition field in schema.Fields.OrderBy(field => field.Name, StringComparer.Ordinal))
            text.Append(field.Name).Append('\0').Append(field.Type).Append('\0').Append(field.Indexed).Append('\0').Append(field.Stored).Append('\0').Append(field.MultiValued).Append('\0').Append(field.Analyser).Append('\0').Append(field.VectorDimensions).Append('\n');

        foreach ((string name, AnalysisDefinition analysis) in schema.Analysis.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append(name).Append('\0').Append(analysis.Tokeniser).Append('\0').Append(string.Join('\0', analysis.CharacterFilters)).Append('\0').Append(string.Join('\0', analysis.TokenFilters)).Append('\n');

        text.Append(topology.ShardCount).Append('\0').Append(topology.ReplicaCount);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
