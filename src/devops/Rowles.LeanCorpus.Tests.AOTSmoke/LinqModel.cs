using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Linq;
using Rowles.LeanCorpus.Mapping;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

internal sealed class NativeSmokeDoc
{
    public string? Title { get; set; }
    public int Year { get; set; }
    public string? Status { get; set; }
    public bool IsPublished { get; set; }
}

internal static class NativeSmokeFields
{
    public static readonly LeanField<NativeSmokeDoc, string> Title       = new("title",       FieldType.Text,    true, true, true);
    public static readonly LeanField<NativeSmokeDoc, int>    Year        = new("year",        FieldType.Numeric, true, true, true);
    public static readonly LeanField<NativeSmokeDoc, string> Status      = new("status",      FieldType.String,  true, true, true);
    public static readonly LeanField<NativeSmokeDoc, bool>   IsPublished = new("isPublished", FieldType.String,  true, true, true);
}

internal sealed class NativeSmokeDocMap : LeanDocumentMap<NativeSmokeDoc>
{
    public override string DocumentName => "doc";
    public override bool StrictSchema => true;
    public override IReadOnlyList<LeanFieldBinding<NativeSmokeDoc>> Fields { get; } = new[]
    {
        new LeanFieldBinding<NativeSmokeDoc>("title",       FieldType.Text,    true, true, true),
        new LeanFieldBinding<NativeSmokeDoc>("year",        FieldType.Numeric, true, true, true),
        new LeanFieldBinding<NativeSmokeDoc>("status",      FieldType.String,  true, true, true),
        new LeanFieldBinding<NativeSmokeDoc>("isPublished", FieldType.String,  true, true, true),
    };
    public override LeanDocument ToDocument(NativeSmokeDoc d) => throw new NotSupportedException();
    public override NativeSmokeDoc FromStoredDocument(StoredDocument d) => new()
    {
        Title       = d.GetFirst("title"),
        Year        = d.GetFirst("year") is { } s && int.TryParse(s, out var y) ? y : 0,
        Status      = d.GetFirst("status"),
        IsPublished = d.GetFirst("isPublished") == "true",
    };
    public override IndexSchema CreateSchema(bool strict)
    {
        var s = new IndexSchema { StrictMode = strict };
        foreach (var f in Fields) s.Add(new FieldMapping(f.Name, f.FieldType) { IsStored = f.IsStored, IsIndexed = f.IsIndexed, IsRequired = f.IsRequired });
        return s;
    }
}
