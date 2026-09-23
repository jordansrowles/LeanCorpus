namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Describes the relationship between a query cell and a packed BKD cell.</summary>
internal enum PackedBkdCellRelation : byte
{
    Outside,
    Inside,
    Crosses,
}
