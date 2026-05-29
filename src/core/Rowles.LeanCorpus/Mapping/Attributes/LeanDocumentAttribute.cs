using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Marks a class or struct as a LeanCorpus document model. The source generator
/// emits a paired <c>{TypeName}Index</c> static class with field descriptors,
/// <c>ToDocument</c>, <c>FromStoredDocument</c>, and <c>CreateSchema</c> members.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class LeanDocumentAttribute : Attribute
{
    /// <summary>Overrides the logical document name used in diagnostics. Defaults to the type name.</summary>
    public string? Name { get; init; }

    /// <summary>Whether the generated schema is strict, rejecting fields it does not know about.</summary>
    public bool StrictSchema { get; init; } = true;
}
