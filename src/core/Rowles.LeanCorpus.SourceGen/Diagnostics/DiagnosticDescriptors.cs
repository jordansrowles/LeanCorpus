using Microsoft.CodeAnalysis;

namespace Rowles.LeanCorpus.SourceGen.Diagnostics;

internal static class DiagnosticDescriptors
{
    private const string Category = "LeanCorpus.Mapping";

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        id: "LCGEN001",
        title: "Unsupported property type",
        messageFormat: "Property '{0}' has unsupported type '{1}' for LeanCorpus mapping",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateFieldName = new(
        id: "LCGEN002",
        title: "Duplicate generated field name",
        messageFormat: "Field name '{0}' is used by more than one property on '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidFieldName = new(
        id: "LCGEN003",
        title: "Invalid field name",
        messageFormat: "Field name '{0}' on property '{1}' is null, empty, or contains invalid characters",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FromStoredNotEmitted = new(
        id: "LCGEN004",
        title: "FromStoredDocument cannot be generated",
        messageFormat: "FromStoredDocument for '{0}' will throw at runtime because required member '{1}' is not stored or has an unsupported shape",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingAttributes = new(
        id: "LCGEN005",
        title: "Conflicting field attributes",
        messageFormat: "Property '{0}' has more than one Lean field attribute; only one is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingNumericEncoding = new(
        id: "LCGEN006",
        title: "Missing numeric encoding",
        messageFormat: "Property '{0}' of type '{1}' requires an explicit LeanNumericEncoding on [LeanNumeric]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingVectorDimension = new(
        id: "LCGEN007",
        title: "Missing vector dimension",
        messageFormat: "Property '{0}' marked [LeanVector] must declare a positive Dimension",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedCollectionShape = new(
        id: "LCGEN008",
        title: "Unsupported collection shape",
        messageFormat: "Property '{0}' has unsupported collection shape '{1}'; only IReadOnlyList<string>, string[], and float[] (for vectors) are supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidGeoMapping = new(
        id: "LCGEN009",
        title: "Invalid geo-point mapping",
        messageFormat: "Property '{0}' marked [LeanGeoPoint] must be of type LeanGeoLocation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
