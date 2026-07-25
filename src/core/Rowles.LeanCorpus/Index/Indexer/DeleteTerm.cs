using System.Text;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// A compact delete command: field identified by ordinal (avoids repeated string
/// hashing), term stored as UTF-8 bytes, and the qualified-term prefix
/// (<c>fieldName + \0</c>) pre-encoded for direct byte-level lookup.
/// Hard deletes (IsSoftDelete=false) always win over soft deletes targeting
/// the same field+term.
/// </summary>
internal readonly record struct DeleteTerm(
    int FieldOrdinal,
    byte[] TermUtf8,
    byte[] FieldPrefixUtf8,
    bool IsSoftDelete
)
{
    /// <summary>Combine prefix + term into qualified UTF-8 bytes for FST lookup.</summary>
    public byte[] BuildQualifiedTermBytes()
    {
        var result = new byte[FieldPrefixUtf8.Length + TermUtf8.Length];
        FieldPrefixUtf8.CopyTo(result, 0);
        TermUtf8.CopyTo(result, FieldPrefixUtf8.Length);
        return result;
    }
}
