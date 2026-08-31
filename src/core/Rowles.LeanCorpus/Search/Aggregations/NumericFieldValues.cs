using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Describes the numeric DocValues representation used by a field.</summary>
internal readonly record struct NumericFieldAccessor(
    bool IsInt64,
    bool IsSortedNumeric,
    bool IsSingleNumeric);

/// <summary>Contains the values read for one numeric document.</summary>
internal readonly struct NumericDocumentValues
{
    private NumericDocumentValues(
        bool isInt64,
        long int64Value,
        IReadOnlyList<long>? int64Values,
        double doubleValue,
        IReadOnlyList<double>? doubleValues)
    {
        IsInt64 = isInt64;
        Int64Value = int64Value;
        Int64Values = int64Values;
        DoubleValue = doubleValue;
        DoubleValues = doubleValues;
    }

    public bool IsInt64 { get; }

    public long Int64Value { get; }

    public IReadOnlyList<long>? Int64Values { get; }

    public double DoubleValue { get; }

    public IReadOnlyList<double>? DoubleValues { get; }

    public static NumericDocumentValues Single(long value)
        => new(isInt64: true, int64Value: value, int64Values: null, doubleValue: 0, doubleValues: null);

    public static NumericDocumentValues Multiple(IReadOnlyList<long> values)
        => new(isInt64: true, int64Value: 0, int64Values: values, doubleValue: 0, doubleValues: null);

    public static NumericDocumentValues Single(double value)
        => new(isInt64: false, int64Value: 0, int64Values: null, doubleValue: value, doubleValues: null);

    public static NumericDocumentValues Multiple(IReadOnlyList<double> values)
        => new(isInt64: false, int64Value: 0, int64Values: null, doubleValue: 0, doubleValues: values);
}

/// <summary>Shares numeric field representation and document-value access logic.</summary>
internal static class NumericFieldValues
{
    public static NumericFieldAccessor ResolveFieldAccessor(
        string fieldName,
        IReadOnlyList<SegmentReader> readers)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(readers);
        NumericFieldAccessor? resolved = null;
        foreach (var reader in readers)
        {
            bool fieldIsPresent = reader.Info.FieldNames.Contains(fieldName, StringComparer.Ordinal);
            bool hasNumeric = reader.HasNumericField(fieldName);
            if (!fieldIsPresent && !hasNumeric)
                continue;

            if (!hasNumeric)
                throw new InvalidOperationException(
                    $"Numeric field '{fieldName}' has an incompatible non-numeric representation in segment '{reader.Info.SegmentId}'.");

            var current = DetermineSegmentAccessor(reader, fieldName);
            if (resolved is null)
                resolved = current;
            else if (resolved.Value != current)
                throw new InvalidOperationException(
                    $"Numeric field '{fieldName}' has incompatible DocValues representations across segments.");
        }

        return resolved ?? default;
    }

    private static NumericFieldAccessor DetermineSegmentAccessor(SegmentReader reader, string fieldName)
    {
        // Sorted-numeric takes priority because it is the multi-value form.
        if (reader.GetSortedNumericDocValues(fieldName) is not null)
            return new NumericFieldAccessor(IsInt64: false, IsSortedNumeric: true, IsSingleNumeric: false);
        if (reader.GetSortedInt64DocValues(fieldName) is not null)
            return new NumericFieldAccessor(IsInt64: true, IsSortedNumeric: true, IsSingleNumeric: false);

        if (reader.HasInt64Index(fieldName) || reader.GetInt64DocValues(fieldName) is not null)
            return new NumericFieldAccessor(IsInt64: true, IsSortedNumeric: false, IsSingleNumeric: true);
        if (reader.HasNumericIndex(fieldName) || reader.GetNumericDocValues(fieldName) is not null)
            return new NumericFieldAccessor(IsInt64: false, IsSortedNumeric: false, IsSingleNumeric: true);

        throw new InvalidOperationException($"Numeric field '{fieldName}' has no readable numeric representation.");
    }

    public static bool TryRead(
        SegmentReader reader,
        string fieldName,
        int localDocId,
        NumericFieldAccessor accessor,
        out NumericDocumentValues values)
    {
        if (!reader.HasNumericField(fieldName))
        {
            values = default;
            return false;
        }

        // Segment layouts may legitimately differ when old and new segments coexist.
        // Do not let the first segment's representation silently discard values from a
        // later segment with an incompatible numeric shape.
        var segmentAccessor = ResolveFieldAccessor(fieldName, [reader]);
        if (segmentAccessor.IsInt64 != accessor.IsInt64
            || segmentAccessor.IsSortedNumeric != accessor.IsSortedNumeric)
        {
            throw new InvalidOperationException(
                $"Numeric field '{fieldName}' has incompatible DocValues representations across segments.");
        }

        return TryReadCore(reader, fieldName, localDocId, segmentAccessor, out values);
    }

    private static bool TryReadCore(
        SegmentReader reader,
        string fieldName,
        int localDocId,
        NumericFieldAccessor accessor,
        out NumericDocumentValues values)
    {
        if (accessor.IsInt64)
        {
            if (accessor.IsSortedNumeric
                && reader.TryGetSortedInt64DocValues(fieldName, localDocId, out var int64Values))
            {
                values = NumericDocumentValues.Multiple(int64Values);
                return true;
            }

            if (accessor.IsSingleNumeric
                && reader.TryGetInt64Value(fieldName, localDocId, out long int64Value))
            {
                values = NumericDocumentValues.Single(int64Value);
                return true;
            }
        }
        else
        {
            if (accessor.IsSortedNumeric
                && reader.TryGetSortedNumericDocValues(fieldName, localDocId, out var doubleValues))
            {
                values = NumericDocumentValues.Multiple(doubleValues);
                return true;
            }

            if (accessor.IsSingleNumeric
                && reader.TryGetNumericValue(fieldName, localDocId, out double doubleValue))
            {
                values = NumericDocumentValues.Single(doubleValue);
                return true;
            }
        }

        values = default;
        return false;
    }
}
