using System.Text;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Compares valid field names by the byte order of their strict UTF-8 encoding.</summary>
internal sealed class PackedBkdFieldNameComparer : IComparer<string>
{
    internal static PackedBkdFieldNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        PackedBkdFormat.ValidateFieldName(left);
        PackedBkdFormat.ValidateFieldName(right);
        var leftRunes = left.EnumerateRunes();
        var rightRunes = right.EnumerateRunes();
        while (leftRunes.MoveNext() && rightRunes.MoveNext())
        {
            int comparison = leftRunes.Current.Value.CompareTo(rightRunes.Current.Value);
            if (comparison != 0)
                return comparison;
        }

        if (leftRunes.MoveNext()) return 1;
        if (rightRunes.MoveNext()) return -1;
        return 0;
    }
}
