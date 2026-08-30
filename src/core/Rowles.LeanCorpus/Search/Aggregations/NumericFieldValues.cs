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
        // Determine the field type from the first segment that contains the field,
        // regardless of which documents have values.
        foreach (var reader in readers)
        {
            if (!reader.HasNumericField(fieldName))
                continue;

            // Sorted-numeric takes priority because it is the multi-value form.
            if (reader.GetSortedNumericDocValues(fieldName) is not null)
                return new NumericFieldAccessor(IsInt64: false, IsSortedNumeric: true, IsSingleNumeric: false);
            if (reader.GetSortedInt64DocValues(fieldName) is not null)
                return new NumericFieldAccessor(IsInt64: true, IsSortedNumeric: true, IsSingleNumeric: false);

            // Either sparse .num/.numl index or dense .dvn/.dvnl array.
            if (reader.GetInt64DocValues(fieldName) is not null || reader.GetNumericDocValues(fieldName) is null)
                return new NumericFieldAccessor(IsInt64: true, IsSortedNumeric: false, IsSingleNumeric: true);
            return new NumericFieldAccessor(IsInt64: false, IsSortedNumeric: false, IsSingleNumeric: true);
        }

        return default;
    }

    public static bool TryRead(
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
