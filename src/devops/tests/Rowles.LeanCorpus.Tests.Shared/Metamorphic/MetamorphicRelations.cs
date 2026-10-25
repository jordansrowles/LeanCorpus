namespace Rowles.LeanCorpus.Tests.Shared.Metamorphic;

public enum MetamorphicRelation
{
    Exact,
    SetEquivalent,
    OrderedEquivalent,
    Approximate,
    MonotonicSubset,
    Idempotent,
    Commutative,
    RoundTrip
}

public sealed record MetamorphicObservation(
    IReadOnlyList<string> OrderedIds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> StoredFields)
{
    public IReadOnlySet<string> IdSet { get; } = OrderedIds.ToHashSet(StringComparer.Ordinal);
}

public static class MetamorphicRelations
{
    public static bool Holds(
        MetamorphicRelation relation,
        MetamorphicObservation baseline,
        MetamorphicObservation transformed,
        double tolerance = 0d) => relation switch
    {
        MetamorphicRelation.Exact or MetamorphicRelation.OrderedEquivalent or MetamorphicRelation.RoundTrip =>
            baseline.OrderedIds.SequenceEqual(transformed.OrderedIds, StringComparer.Ordinal)
            && StoredFieldsEqual(baseline.StoredFields, transformed.StoredFields),
        MetamorphicRelation.SetEquivalent or MetamorphicRelation.Idempotent or MetamorphicRelation.Commutative =>
            baseline.IdSet.SetEquals(transformed.IdSet)
            && StoredFieldsEqual(baseline.StoredFields, transformed.StoredFields),
        MetamorphicRelation.MonotonicSubset => transformed.IdSet.IsSubsetOf(baseline.IdSet),
        MetamorphicRelation.Approximate => Math.Abs(baseline.OrderedIds.Count - transformed.OrderedIds.Count) <= tolerance,
        _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "Unknown metamorphic relation.")
    };

    public static string Describe(
        MetamorphicRelation relation,
        MetamorphicObservation baseline,
        MetamorphicObservation transformed) =>
        $"Relation={relation}; baseline=[{string.Join(',', baseline.OrderedIds)}]; transformed=[{string.Join(',', transformed.OrderedIds)}]";

    private static bool StoredFieldsEqual(
        IReadOnlyDictionary<string, IReadOnlyList<string>> left,
        IReadOnlyDictionary<string, IReadOnlyList<string>> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (id, leftFields) in left)
        {
            if (!right.TryGetValue(id, out var rightFields)
                || !leftFields.SequenceEqual(rightFields, StringComparer.Ordinal))
                return false;
        }

        return true;
    }
}
