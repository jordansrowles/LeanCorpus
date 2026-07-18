namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Controls which DocValues representations are written for a <see cref="StringField"/>.</summary>
[Flags]
public enum StringDocValues
{
    /// <summary>Do not write DocValues for the field.</summary>
    None = 0,

    /// <summary>Write one sortable value per document.</summary>
    Sorted = 1,

    /// <summary>Write zero or more sortable values per document.</summary>
    SortedSet = 2,

    /// <summary>Write the UTF-8 value as binary DocValues.</summary>
    Binary = 4
}
