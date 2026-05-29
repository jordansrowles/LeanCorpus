using Rowles.LeanCorpus.Document.Fields;

namespace Rowles.LeanCorpus.Mapping.Attributes;

/// <summary>
/// Excludes a property from generated document mapping. The property is ignored entirely;
/// no field descriptor, schema entry, or mapping code is emitted for it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class LeanIgnoreAttribute : Attribute
{
}
