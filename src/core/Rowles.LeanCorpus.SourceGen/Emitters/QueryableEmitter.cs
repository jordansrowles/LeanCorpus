using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Rowles.LeanCorpus.SourceGen.Models;

namespace Rowles.LeanCorpus.SourceGen.Emitters;

/// <summary>
/// Emits LINQ queryable support into the generated <c>{Type}Index</c> class:
/// a zero-allocation field-descriptor resolver switch expression and a
/// convenience <c>AsQueryable(IndexSearcher)</c> entry point.
/// </summary>
internal static class QueryableEmitter
{
    /// <summary>
    /// Emits the field-descriptor resolver and <c>AsQueryable</c> method.
    /// Appends into the existing <c>{Type}Index</c> static class body,
    /// before the closing brace.
    /// </summary>
    public static void Emit(StringBuilder sb, DocumentModel doc)
    {
        // Belt-and-suspenders guard: the generator already skips documents
        // with zero fields, but this emitter may be called from other paths.
        if (doc.Fields.Count == 0)
            return;

        string typeFq = doc.FullyQualifiedTypeName;

        // Field descriptor resolver — switch expression over property names.
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Maps C# property names to field descriptors for the LINQ expression visitor.");
        sb.AppendLine("    /// The JIT compiles this switch expression to a jump table (zero allocation).");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal static global::Rowles.LeanCorpus.Linq.IFieldDescriptor? GetFieldDescriptor(string propertyName)");
        sb.AppendLine("        => propertyName switch");
        sb.AppendLine("        {");
        foreach (var f in doc.Fields)
        {
            string propName = Id(f.PropertyName);
            sb.Append("            \"")
              .Append(EscapeSwitchString(propName.StartsWith("@") ? propName.Substring(1) : propName))
              .Append("\" => Fields.")
              .Append(Id(f.PropertyName))
              .AppendLine(",");
        }
        sb.AppendLine("            _ => null");
        sb.AppendLine("        };");
        sb.AppendLine();

        // AsQueryable convenience method.
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Returns a LINQ <see cref=\"global::Rowles.LeanCorpus.Linq.LeanQueryable{T}\"/>");
        sb.AppendLine("    /// that queries the index via the supplied <paramref name=\"searcher\"/>.");
        sb.Append("    /// Lambda predicates (e.g. <c>.Where(d =&gt; d.")
          .Append(Id(doc.Fields[0].PropertyName))
          .AppendLine(" == value)</c>) are translated into");
        sb.AppendLine("    /// LeanCorpus query objects and executed directly against the index.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static global::Rowles.LeanCorpus.Linq.LeanQueryable<" + typeFq + "> AsQueryable(");
        sb.AppendLine("        global::Rowles.LeanCorpus.Search.Searcher.IndexSearcher searcher)");
        sb.AppendLine("        => new(searcher, Map, GetFieldDescriptor);");
    }

    private static string Id(string name)
        => SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            ? "@" + name
            : name;

    private static string EscapeSwitchString(string s)
    {
        // Escape backslashes and double-quotes for the switch pattern string.
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
