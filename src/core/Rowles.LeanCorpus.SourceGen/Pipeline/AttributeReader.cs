using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Rowles.LeanCorpus.SourceGen.Diagnostics;
using Rowles.LeanCorpus.SourceGen.Models;

namespace Rowles.LeanCorpus.SourceGen.Pipeline;

internal static class AttributeReader
{
    public const string LeanDocumentAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanDocumentAttribute";
    public const string LeanIgnoreAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanIgnoreAttribute";
    public const string LeanTextAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanTextAttribute";
    public const string LeanStringAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanStringAttribute";
    public const string LeanNumericAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanNumericAttribute";
    public const string LeanVectorAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanVectorAttribute";
    public const string LeanGeoPointAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanGeoPointAttribute";
    public const string LeanStoredAttribute = "Rowles.LeanCorpus.Mapping.Attributes.LeanStoredAttribute";

    private const string LeanGeoLocationTypeName = "Rowles.LeanCorpus.Mapping.LeanGeoLocation";

    public static DocumentModel? Read(INamedTypeSymbol typeSymbol, AttributeData documentAttribute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string documentName = typeSymbol.Name;
        bool strictSchema = true;
        foreach (var named in documentAttribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Name":
                    if (named.Value.Value is string n && !string.IsNullOrEmpty(n))
                        documentName = n;
                    break;
                case "StrictSchema":
                    if (named.Value.Value is bool s)
                        strictSchema = s;
                    break;
            }
        }

        var diagnostics = new List<Diagnostic>();

        string ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();
        string fqn = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isPartial = IsPartial(typeSymbol);
        string accessibility = typeSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal",
        };

        if (typeSymbol.ContainingType is not null || typeSymbol.TypeParameters.Length > 0)
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.UnsupportedDocumentTarget,
                typeSymbol.Locations.FirstOrDefault(),
                typeSymbol.ToDisplayString()));

            return new DocumentModel(
                TypeName: typeSymbol.Name,
                FullyQualifiedTypeName: fqn,
                Namespace: ns,
                DocumentName: documentName,
                StrictSchema: strictSchema,
                IsValueType: typeSymbol.IsValueType,
                IsPartial: isPartial,
                Accessibility: accessibility,
                Fields: new List<FieldModel>(),
                Diagnostics: diagnostics,
                CanGenerateFromStored: false,
                FromStoredBlockerProperty: typeSymbol.Name);
        }

        var fields = new List<FieldModel>();

        foreach (var member in typeSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not IPropertySymbol property) continue;

            // [LeanIgnore]
            if (HasAttribute(property, LeanIgnoreAttribute)) continue;

            // Find field attributes
            var fieldAttrs = property.GetAttributes()
                .Where(a => IsLeanFieldAttribute(a))
                .ToImmutableArray();

            if (fieldAttrs.Length == 0) continue;

            if (property.IsStatic || property.IsIndexer || !CanReadFromGeneratedCode(property))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.InaccessibleMappedMember,
                    property.Locations.FirstOrDefault(),
                    property.Name));
                continue;
            }

            if (fieldAttrs.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.ConflictingAttributes,
                    property.Locations.FirstOrDefault(),
                    property.Name));
                continue;
            }

            var fieldModel = BuildFieldModel(property, fieldAttrs[0], diagnostics);
            if (fieldModel != null) fields.Add(fieldModel);
        }

        // Duplicate field names
        var dupGroups = fields.GroupBy(f => f.FieldName).Where(g => g.Count() > 1).ToList();
        foreach (var group in dupGroups)
        {
            foreach (var dup in group)
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateFieldName,
                    dup.Location,
                    group.Key, typeSymbol.Name));
            }
        }
        if (dupGroups.Count > 0)
        {
            fields = fields.Where(f => !dupGroups.Any(g => g.Key == f.FieldName)).ToList();
        }

        // FromStoredDocument feasibility: every emitted member must be storable, round-trippable, and assignable.
        bool canFromStored = true;
        string? blocker = null;
        foreach (var f in fields)
        {
            if (!CanRoundTrip(f))
            {
                canFromStored = false;
                blocker = f.PropertyName;
                break;
            }
            if (!f.CanAssignFromGeneratedCode)
            {
                canFromStored = false;
                blocker = f.PropertyName;
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.FromStoredConstructionNotAvailable,
                    f.Location,
                    typeSymbol.Name, blocker));
                break;
            }
        }
        if (!canFromStored && blocker != null)
        {
            if (!diagnostics.Any(d => d.Id == DiagnosticDescriptors.FromStoredConstructionNotAvailable.Id))
            {
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.FromStoredNotEmitted,
                    typeSymbol.Locations.FirstOrDefault(),
                    typeSymbol.Name, blocker));
            }
        }

        if (canFromStored && !CanConstructFromStored(typeSymbol))
        {
            canFromStored = false;
            blocker = typeSymbol.Name;
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.FromStoredConstructionNotAvailable,
                typeSymbol.Locations.FirstOrDefault(),
                typeSymbol.Name, blocker));
        }

        if (canFromStored)
        {
            var mappedProperties = fields.Select(f => f.PropertyName).ToImmutableHashSet();
            foreach (var member in typeSymbol.GetMembers())
            {
                if (member is not IPropertySymbol property) continue;
                if (property.IsStatic || mappedProperties.Contains(property.Name)) continue;
                if (!property.IsRequired) continue;

                canFromStored = false;
                blocker = property.Name;
                diagnostics.Add(Diagnostic.Create(
                    DiagnosticDescriptors.FromStoredConstructionNotAvailable,
                    property.Locations.FirstOrDefault(),
                    typeSymbol.Name, blocker));
                break;
            }
        }

        return new DocumentModel(
            TypeName: typeSymbol.Name,
            FullyQualifiedTypeName: fqn,
            Namespace: ns,
            DocumentName: documentName,
            StrictSchema: strictSchema,
            IsValueType: typeSymbol.IsValueType,
            IsPartial: isPartial,
            Accessibility: accessibility,
            Fields: fields,
            Diagnostics: diagnostics,
            CanGenerateFromStored: canFromStored,
            FromStoredBlockerProperty: blocker);
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var node = syntaxRef.GetSyntax();
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax tds)
            {
                foreach (var mod in tds.Modifiers)
                {
                    if (mod.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool IsLeanFieldAttribute(AttributeData a)
    {
        var name = a.AttributeClass?.ToDisplayString();
        return name is LeanTextAttribute or LeanStringAttribute or LeanNumericAttribute
            or LeanVectorAttribute or LeanGeoPointAttribute or LeanStoredAttribute;
    }

    private static bool HasAttribute(ISymbol symbol, string fullName)
    {
        foreach (var a in symbol.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() == fullName) return true;
        }
        return false;
    }

    private static bool CanReadFromGeneratedCode(IPropertySymbol property)
        => property.GetMethod is { } getter && IsAccessibleFromGeneratedCode(getter.DeclaredAccessibility);

    private static bool CanAssignFromGeneratedCode(IPropertySymbol property)
        => property.SetMethod is { } setter && IsAccessibleFromGeneratedCode(setter.DeclaredAccessibility);

    private static bool IsAccessibleFromGeneratedCode(Accessibility accessibility)
        => accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

    private static bool CanConstructFromStored(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.IsValueType) return true;
        if (typeSymbol.IsAbstract) return false;

        foreach (var ctor in typeSymbol.InstanceConstructors)
        {
            if (ctor.Parameters.Length == 0 && IsAccessibleFromGeneratedCode(ctor.DeclaredAccessibility))
                return true;
        }

        return false;
    }

    private static FieldModel? BuildFieldModel(IPropertySymbol property, AttributeData attr, List<Diagnostic> diagnostics)
    {
        string attrName = attr.AttributeClass!.ToDisplayString();
        string? fieldName = attr.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
        if (string.IsNullOrEmpty(fieldName) || ContainsInvalidNameChar(fieldName!))
        {
            diagnostics.Add(Diagnostic.Create(
                DiagnosticDescriptors.InvalidFieldName,
                property.Locations.FirstOrDefault(),
                fieldName ?? "<null>", property.Name));
            return null;
        }

        bool isRequired = property.IsRequired
            || (property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated && !property.Type.IsValueType && !IsNullableValueType(property.Type))
            || (property.Type.IsValueType && !IsNullableValueType(property.Type));

        // attribute-level "Required" overrides
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == "Required" && na.Value.Value is bool rb) isRequired = rb;
        }

        bool stored = true;
        foreach (var na in attr.NamedArguments)
        {
            if (na.Key == "Stored" && na.Value.Value is bool sb) stored = sb;
        }

        bool isNullable = property.Type.NullableAnnotation == NullableAnnotation.Annotated || IsNullableValueType(property.Type);
        var underlying = UnwrapNullable(property.Type);

        string propTypeFqn = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var loc = property.Locations.FirstOrDefault();
        bool canAssign = CanAssignFromGeneratedCode(property);

        switch (attrName)
        {
            case LeanTextAttribute:
            case LeanStringAttribute:
            {
                bool isText = attrName == LeanTextAttribute;
                var (shape, ok) = ResolveStringShape(underlying);
                if (!ok)
                {
                    if (shape == ValueShape.SingleValue)
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedPropertyType, loc, property.Name, propTypeFqn));
                    else
                        diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedCollectionShape, loc, property.Name, propTypeFqn));
                    return null;
                }
                return new FieldModel(property.Name, fieldName!, propTypeFqn,
                    isText ? FieldKind.Text : FieldKind.String,
                    shape, NumericKind.None, "None", 0, stored, true, isRequired, isNullable, canAssign, loc);
            }
            case LeanNumericAttribute:
            {
                string encoding = "None";
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Encoding" && na.Value.Value is int e)
                        encoding = EncodingToString(e);

                var numericKind = ClassifyNumeric(underlying);
                if (numericKind == NumericKind.None)
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedPropertyType, loc, property.Name, propTypeFqn));
                    return null;
                }

                if (numericKind is NumericKind.DateTimeOffset or NumericKind.DateOnly or NumericKind.TimeOnly or NumericKind.Decimal
                    && encoding == "None")
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MissingNumericEncoding, loc, property.Name, propTypeFqn));
                    return null;
                }

                // decimal-as-string flips to stored
                FieldKind fk = (numericKind == NumericKind.Decimal && encoding == "DecimalAsString")
                    ? FieldKind.StoredString
                    : FieldKind.Numeric;

                if (fk == FieldKind.StoredString && !stored)
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.InvalidDecimalStringStorage, loc, property.Name));
                    return null;
                }

                return new FieldModel(property.Name, fieldName!, propTypeFqn,
                    fk, ValueShape.SingleValue, numericKind, encoding, 0, stored, fk == FieldKind.Numeric, isRequired, isNullable, canAssign, loc);
            }
            case LeanVectorAttribute:
            {
                int dim = 0;
                foreach (var na in attr.NamedArguments)
                    if (na.Key == "Dimension" && na.Value.Value is int d) dim = d;

                if (dim <= 0)
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.MissingVectorDimension, loc, property.Name));
                    return null;
                }

                if (!IsFloatArray(underlying))
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedCollectionShape, loc, property.Name, propTypeFqn));
                    return null;
                }

                return new FieldModel(property.Name, fieldName!, propTypeFqn,
                    FieldKind.Vector, ValueShape.FloatArray, NumericKind.None, "None", dim, true, false, isRequired, isNullable, canAssign, loc);
            }
            case LeanGeoPointAttribute:
            {
                if (underlying.ToDisplayString() != LeanGeoLocationTypeName)
                {
                    diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.InvalidGeoMapping, loc, property.Name));
                    return null;
                }
                return new FieldModel(property.Name, fieldName!, propTypeFqn,
                    FieldKind.GeoPoint, ValueShape.SingleValue, NumericKind.None, "None", 0, true, true, isRequired, isNullable, canAssign, loc);
            }
            case LeanStoredAttribute:
            {
                if (IsString(underlying))
                {
                    return new FieldModel(property.Name, fieldName!, propTypeFqn,
                        FieldKind.StoredString, ValueShape.SingleValue, NumericKind.None, "None", 0, true, false, isRequired, isNullable, canAssign, loc);
                }
                if (IsByteArray(underlying))
                {
                    return new FieldModel(property.Name, fieldName!, propTypeFqn,
                        FieldKind.StoredBinary, ValueShape.ByteArray, NumericKind.None, "None", 0, true, false, isRequired, isNullable, canAssign, loc);
                }
                diagnostics.Add(Diagnostic.Create(DiagnosticDescriptors.UnsupportedPropertyType, loc, property.Name, propTypeFqn));
                return null;
            }
        }

        return null;
    }

    private static (ValueShape Shape, bool Ok) ResolveStringShape(ITypeSymbol type)
    {
        if (IsString(type)) return (ValueShape.SingleValue, true);
        if (type is IArrayTypeSymbol arr && IsString(arr.ElementType)) return (ValueShape.StringArray, true);
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            string def = named.ConstructedFrom.ToDisplayString();
            if (def == "System.Collections.Generic.IReadOnlyList<T>" && IsString(named.TypeArguments[0]))
                return (ValueShape.StringList, true);
        }
        // mark as collection-shape error if it looks like a collection
        if (type.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"))
            return (ValueShape.StringList, false);
        return (ValueShape.SingleValue, false);
    }

    private static bool IsString(ITypeSymbol t) => t.SpecialType == SpecialType.System_String;
    private static bool IsByteArray(ITypeSymbol t)
        => t is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Byte;
    private static bool IsFloatArray(ITypeSymbol t)
        => t is IArrayTypeSymbol arr && arr.ElementType.SpecialType == SpecialType.System_Single;

    private static NumericKind ClassifyNumeric(ITypeSymbol t)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                return NumericKind.Integral;
            case SpecialType.System_Single:
            case SpecialType.System_Double:
                return NumericKind.FloatingPoint;
            case SpecialType.System_Decimal:
                return NumericKind.Decimal;
        }
        string name = t.ToDisplayString();
        return name switch
        {
            "System.DateTimeOffset" => NumericKind.DateTimeOffset,
            "System.DateOnly" => NumericKind.DateOnly,
            "System.TimeOnly" => NumericKind.TimeOnly,
            _ => NumericKind.None,
        };
    }

    private static string EncodingToString(int value) => value switch
    {
        0 => "None",
        1 => "UnixMilliseconds",
        2 => "UnixSeconds",
        3 => "UtcTicks",
        4 => "DateOnlyDayNumber",
        5 => "TimeOnlyTicks",
        6 => "DecimalAsString",
        _ => "None",
    };

    private static bool IsNullableValueType(ITypeSymbol t)
        => t is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static ITypeSymbol UnwrapNullable(ITypeSymbol t)
    {
        if (t is INamedTypeSymbol n && n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return n.TypeArguments[0];
        if (t.NullableAnnotation == NullableAnnotation.Annotated && t.IsReferenceType)
            return t.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return t;
    }

    private static bool ContainsInvalidNameChar(string s)
    {
        foreach (char c in s)
        {
            if (c == '\0' || char.IsControl(c)) return true;
        }
        return false;
    }

    private static bool CanRoundTrip(FieldModel f)
    {
        // Vectors live in the vector store rather than StoredDocument.
        if (f.FieldKind == FieldKind.Vector) return false;
        if (!f.IsStored && f.FieldKind != FieldKind.StoredBinary) return false;
        if (f.ValueShape == ValueShape.StringList || f.ValueShape == ValueShape.StringArray) return true;
        return true;
    }
}
