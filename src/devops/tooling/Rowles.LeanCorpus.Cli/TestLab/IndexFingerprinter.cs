using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Index.Inspection;

namespace Rowles.LeanCorpus.Cli.TestLab;

internal sealed record IndexFingerprintResult(
    string Algorithm,
    string LogicalFingerprint,
    string PhysicalFingerprint,
    int? CommitGeneration,
    int DocumentCount,
    int LiveDocumentCount,
    int SegmentCount,
    string? RecipeIdentity);

internal sealed record IndexDiffResult(
    string BeforePath,
    string AfterPath,
    bool LogicalEquivalent,
    bool PhysicalEquivalent,
    bool ComparisonWasLogicalOnly,
    IndexFingerprintResult Before,
    IndexFingerprintResult After);

internal static class IndexFingerprinter
{
    internal static IndexFingerprintResult Calculate(string path)
    {
        using var inspector = new IndexInspector(path);
        IndexInspectionOverview overview = inspector.Overview;
        string logical = Logical(inspector, overview.DocumentCount);
        string physical = Physical(overview.DirectoryPath);
        string? recipeIdentity = ReadRecipeIdentity(overview.DirectoryPath);
        return new IndexFingerprintResult("leancorpus-index-v2", logical, physical,
            overview.CommitGeneration, overview.DocumentCount, overview.LiveDocumentCount,
            overview.SegmentCount, recipeIdentity);
    }

    internal static IndexDiffResult Diff(string beforePath, string afterPath, bool logicalOnly)
    {
        IndexFingerprintResult before = Calculate(beforePath);
        IndexFingerprintResult after = Calculate(afterPath);
        return new IndexDiffResult(Path.GetFullPath(beforePath), Path.GetFullPath(afterPath),
            string.Equals(before.LogicalFingerprint, after.LogicalFingerprint, StringComparison.Ordinal),
            string.Equals(before.PhysicalFingerprint, after.PhysicalFingerprint, StringComparison.Ordinal),
            logicalOnly, before, after);
    }

    private static string Logical(IndexInspector inspector, int maxDocumentId)
    {
        const int maximumFingerprintDocuments = 1_000_000;
        const int maximumTermsPerField = 100_000;
        if (maxDocumentId > maximumFingerprintDocuments)
            throw new InvalidOperationException($"Logical fingerprint is bounded to {maximumFingerprintDocuments:N0} documents.");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "logical-v2");
        IReadOnlyList<IndexInspectionField> fields = inspector.GetFields();
        var indexedTermsByDocument = Enumerable.Range(0, maxDocumentId)
            .Select(static _ => new List<IndexedTermFingerprint>())
            .ToArray();
        foreach (IndexInspectionField field in fields)
        {
            Append(hash, field.Name);
            Append(hash, field.Indexed ? "1" : "0");
            Append(hash, field.Stored ? "1" : "0");
            Append(hash, field.HasDocValues ? "1" : "0");
            Append(hash, field.HasOrdinalDocValues ? "1" : "0");
            Append(hash, field.HasTermVectors ? "1" : "0");
            Append(hash, field.VectorDimension?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "");
            Append(hash, field.VectorQuantisation?.ToString() ?? "");
            Append(hash, field.SpatialKind?.ToString() ?? "");
            if (!field.Indexed)
                continue;

            IReadOnlyList<IndexInspectionTerm> terms = inspector.GetTerms(field.Name, take: maximumTermsPerField);
            if (terms.Count == maximumTermsPerField)
                throw new InvalidOperationException($"Logical fingerprint is bounded to {maximumTermsPerField:N0} terms per field; '{field.Name}' reached that limit.");
            foreach (IndexInspectionTerm term in terms)
            {
                Append(hash, term.Term);
                Append(hash, term.DocumentFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Append(hash, term.TotalTermFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture));
                IReadOnlyList<IndexInspectionPosting> postings = inspector.GetPostings(field.Name, term.Term, maximumFingerprintDocuments);
                if (postings.Count != term.DocumentFrequency)
                    throw new InvalidOperationException($"Logical fingerprint could not read every live posting for '{field.Name}:{term.Term}'.");
                foreach (IndexInspectionPosting posting in postings)
                    indexedTermsByDocument[posting.DocumentId].Add(new IndexedTermFingerprint(field.Name, term.Term,
                        posting.Frequency, posting.Positions));
            }
        }

        var documentFingerprints = new List<byte[]>();
        for (int documentId = 0; documentId < maxDocumentId; documentId++)
        {
            IndexInspectionDocument document = inspector.GetDocument(documentId);
            if (!document.IsLive)
                continue;
            var stored = new SortedDictionary<string, IReadOnlyList<string>>(document.StoredFields.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            var binary = new SortedDictionary<string, IReadOnlyList<string>>(document.BinaryFieldHashes.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            var vectors = new SortedDictionary<string, IReadOnlyList<float>>(document.Vectors.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
            IndexedTermFingerprint[] indexedTerms = indexedTermsByDocument[documentId]
                .OrderBy(static item => item.Field, StringComparer.Ordinal)
                .ThenBy(static item => item.Term, StringComparer.Ordinal)
                .ToArray();
            byte[] serialised = JsonSerializer.SerializeToUtf8Bytes(new { stored, binary, docValues = document.DocValues, vectors, indexedTerms });
            documentFingerprints.Add(SHA256.HashData(serialised));
        }
        documentFingerprints.Sort(static (left, right) => left.AsSpan().SequenceCompareTo(right));
        foreach (byte[] fingerprint in documentFingerprints)
            hash.AppendData(fingerprint);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record IndexedTermFingerprint(string Field, string Term, int Frequency, IReadOnlyList<int> Positions);

    private static string Physical(string directoryPath)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "physical-v1");
        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, ".leancorpus", StringComparison.Ordinal))
                continue;
            Append(hash, fileName);
            using FileStream stream = File.OpenRead(filePath);
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string? ReadRecipeIdentity(string directoryPath)
    {
        string recipePath = Path.Combine(directoryPath, ".leancorpus", "recipe.json");
        if (!File.Exists(recipePath))
            return null;
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(recipePath));
        return document.RootElement.TryGetProperty("recipeIdentity", out JsonElement value) ? value.GetString() : null;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
