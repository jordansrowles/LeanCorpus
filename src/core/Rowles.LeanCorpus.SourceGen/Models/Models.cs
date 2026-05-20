using Microsoft.CodeAnalysis;

namespace Rowles.LeanCorpus.SourceGen.Models;

/// <summary>
/// The kind of LeanCorpus field a property maps to.
/// </summary>
internal enum FieldKind
{
    Text,
    String,
    Numeric,
    Vector,
    GeoPoint,
    StoredString,
    StoredBinary,
}

/// <summary>
/// The CLR shape of the property value, used by emitters to pick the right projection.
/// </summary>
internal enum ValueShape
{
    SingleValue,
    StringList,
    StringArray,
    FloatArray,
    ByteArray,
}

/// <summary>
/// The CLR primitive category for numeric-mapped properties.
/// </summary>
internal enum NumericKind
{
    None,
    Integral,
    FloatingPoint,
    Decimal,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
}

internal sealed record FieldModel(
    string PropertyName,
    string FieldName,
    string PropertyTypeFullName,
    FieldKind FieldKind,
    ValueShape ValueShape,
    NumericKind NumericKind,
    string NumericEncoding,
    int VectorDimension,
    bool IsStored,
    bool IsIndexed,
    bool IsRequired,
    bool IsNullable,
    Location? Location);

internal sealed record DocumentModel(
    string TypeName,
    string FullyQualifiedTypeName,
    string Namespace,
    string DocumentName,
    bool StrictSchema,
    bool IsValueType,
    bool IsPartial,
    string Accessibility,
    System.Collections.Generic.IReadOnlyList<FieldModel> Fields,
    System.Collections.Generic.IReadOnlyList<Diagnostic> Diagnostics,
    bool CanGenerateFromStored,
    string? FromStoredBlockerProperty);
