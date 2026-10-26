using Rowles.LeanCorpus.Mapping;
using Rowles.LeanCorpus.Mapping.Attributes;

var product = new Product { Id = "package-42", Title = "Generated package map" };
var document = ProductIndex.ToDocument(product);
if (document.Fields.Count < 2 || !ProductIndex.CreateSchema().Mappings.ContainsKey("id"))
    throw new InvalidDataException("The generated document map or schema was not usable.");
if (ProductIndex.Map.DocumentName != "Product")
    throw new InvalidDataException("The generated mapper has the wrong document name.");
Console.WriteLine($"SOURCEGEN {ProductIndex.Map.DocumentName} {ProductIndex.CreateSchema().Mappings.Count}");

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }
}