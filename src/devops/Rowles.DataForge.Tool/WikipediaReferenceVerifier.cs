using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tool;

public sealed record WikipediaReferenceVerificationResult(
    string DatasetId,
    int DatasetVersion,
    int RecordCount,
    long LogicalByteCount,
    string ContentSha256,
    string ArtefactSha256,
    string DumpDate,
    int CandidateLimit);

/// <summary>Verifies a frozen Wikipedia artefact without requiring its source dump or network access.</summary>
public static class WikipediaReferenceVerifier
{
    private static readonly string[] RecordProperties =
    ["Id", "PageId", "RevisionId", "RevisionTimestampUtc", "Title", "SourceUrl", "RawWikitextSha256", "TextSha256", "Text"];

    public static WikipediaReferenceVerificationResult Verify(string datasetDirectoryOrManifest) =>
        Verify(datasetDirectoryOrManifest, WikipediaReferenceContract.TargetCount);

    internal static WikipediaReferenceVerificationResult Verify(string datasetDirectoryOrManifest, int expectedCount)
    {
        var verification = DataForgeVerifier.VerifyMaterialised(datasetDirectoryOrManifest);
        var manifest = verification.Manifest;
        if (manifest.SourceKind != DataForgeSourceKind.Imported || manifest.ProfileId is not null || manifest.ProfileVersion is not null || manifest.Seed is not null)
            throw new InvalidDataException("Wikipedia reference requires an imported manifest without generated profile or seed fields.");
        if (manifest.RecordCount != expectedCount)
            throw new InvalidDataException($"Wikipedia record count is {manifest.RecordCount}; expected {expectedCount}.");
        if (!manifest.Dependencies.Contains(new DataForgeDependencyVersion("SharpCompress", "0.50.4")))
            throw new InvalidDataException("Wikipedia manifest does not record SharpCompress 0.50.4.");
        var source = manifest.Source ?? throw new InvalidDataException("Wikipedia manifest is missing source metadata.");
        Require(source, "datasetId", WikipediaReferenceContract.DatasetId);
        Require(source, "datasetVersion", WikipediaReferenceContract.DatasetVersion.ToString(CultureInfo.InvariantCulture));
        Require(source, "wiki", WikipediaReferenceContract.Wiki);
        Require(source, "dumpDate", WikipediaReferenceContract.DumpDate);
        Require(source, "primaryFilename", WikipediaReferenceContract.PrimaryFilename);
        Require(source, "primaryBytes", WikipediaReferenceContract.PrimaryBytes.ToString(CultureInfo.InvariantCulture));
        Require(source, "primarySha1", WikipediaReferenceContract.PrimarySha1);
        Require(source, "indexFilename", WikipediaReferenceContract.IndexFilename);
        Require(source, "selectionAlgorithm", "page-id-sha256-v1");
        Require(source, "selectionSalt", "leancorpus-wikipedia-en-v1");
        Require(source, "normaliser", WikipediaTextNormaliserV1.Version);
        Require(source, "eligibilityVersion", "1");
        var candidateLimit = ParseCandidateLimit(source);
        ValidateSha1(source, "indexSha1");
        ValidateSha256(source, "primarySha256");
        ValidateSha256(source, "indexSha256");
        ValidateSha256(source, "checksumFileSha256");
        ValidateSha256(source, "dumpStatusSha256");

        var directory = Path.GetDirectoryName(verification.ManifestPath)!;
        var licencePath = Path.Combine(directory, "LICENSES", "CC-BY-SA-4.0.txt");
        var attributionPath = Path.Combine(directory, "LICENSES", "attribution.txt");
        VerifySupportFile(licencePath, RequireValue(source, "licenceFileSha256"));
        VerifySupportFile(attributionPath, RequireValue(source, "attributionFileSha256"));
        var licenceWords = File.ReadAllText(licencePath, new UTF8Encoding(false, true))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var licenceText = string.Join(' ', licenceWords);
        if (!licenceText.Contains("Attribution-ShareAlike 4.0 International Public License", StringComparison.Ordinal) ||
            !licenceText.Contains("Section 1 -- Definitions.", StringComparison.Ordinal))
            throw new InvalidDataException("Wikipedia licence file does not contain the CC BY-SA 4.0 legal text.");
        var attribution = File.ReadAllText(attributionPath, new UTF8Encoding(false, true));
        foreach (var required in new[]
                 {
                     "Dataset: leancorpus-wikipedia-en-v1",
                     "Source: English Wikipedia contributors",
                     "Source snapshot: enwiki 20260901",
                     "Licence: CC BY-SA 4.0",
                     "Per-record revision attribution: SourceUrl field in records.ndjson",
                     "Transformation: LeanCorpus DataForge Wikipedia visible-text normaliser v1"
                 })
            if (!attribution.Contains(required, StringComparison.Ordinal))
                throw new InvalidDataException($"Wikipedia attribution file is missing required line '{required}'.");

        VerifyRecords(verification.RecordsPath, expectedCount);
        return new WikipediaReferenceVerificationResult(
            WikipediaReferenceContract.DatasetId,
            WikipediaReferenceContract.DatasetVersion,
            manifest.RecordCount,
            manifest.LogicalByteCount,
            manifest.ContentSha256,
            manifest.ArtefactSha256,
            WikipediaReferenceContract.DumpDate,
            candidateLimit);
    }

    private static void VerifyRecords(string recordsPath, int expectedCount)
    {
        using var stream = new FileStream(recordsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var line = new MemoryStream(16 * 1024);
        var buffer = new byte[64 * 1024];
        var pageIds = new HashSet<ulong>();
        WikipediaCandidate? previous = null;
        var count = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            var offset = 0;
            while (offset < read)
            {
                var lineFeed = Array.IndexOf(buffer, (byte)'\n', offset, read - offset);
                var end = lineFeed < 0 ? read : lineFeed;
                line.Write(buffer, offset, end - offset);
                if (line.Length > 4_194_304)
                    throw new InvalidDataException($"Wikipedia NDJSON record {count + 1} exceeds the 4 MiB canonical bound.");
                if (lineFeed < 0)
                    break;
                if (line.Length == 0)
                    throw new InvalidDataException($"Wikipedia NDJSON contains an empty record at line {count + 1}.");
                VerifyRecord(line.GetBuffer().AsSpan(0, checked((int)line.Length)), pageIds, ref previous, count + 1);
                count++;
                if (count > expectedCount)
                    throw new InvalidDataException($"Wikipedia NDJSON contains more than {expectedCount} records.");
                line.SetLength(0);
                offset = lineFeed + 1;
            }
        }
        if (line.Length != 0 || count != expectedCount)
            throw new InvalidDataException($"Wikipedia NDJSON ended after {count} complete records; expected {expectedCount} records ending in LF.");
    }

    private static void VerifyRecord(ReadOnlySpan<byte> lineBytes, HashSet<ulong> pageIds, ref WikipediaCandidate? previous, int ordinal)
    {
        using var document = JsonDocument.Parse(lineBytes.ToArray());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.EnumerateObject().Select(static property => property.Name).SequenceEqual(RecordProperties, StringComparer.Ordinal))
            throw new InvalidDataException($"Wikipedia record {ordinal} has missing, duplicate, unknown or out-of-order properties.");
        var record = new WikipediaReferenceRecord(
            RequiredString(root, "Id"),
            RequiredUInt64(root, "PageId"),
            RequiredUInt64(root, "RevisionId"),
            RequiredString(root, "RevisionTimestampUtc"),
            RequiredString(root, "Title"),
            RequiredString(root, "SourceUrl"),
            RequiredString(root, "RawWikitextSha256"),
            RequiredString(root, "TextSha256"),
            RequiredString(root, "Text"));
        if (record.PageId == 0 || record.RevisionId == 0 || !pageIds.Add(record.PageId))
            throw new InvalidDataException($"Wikipedia record {ordinal} has a zero or duplicate page/revision ID.");
        if (record.Id != $"enwiki-{record.PageId.ToString(CultureInfo.InvariantCulture)}")
            throw new InvalidDataException($"Wikipedia record {ordinal} has an invalid ID field.");
        var expectedSource = $"https://en.wikipedia.org/w/index.php?oldid={record.RevisionId.ToString(CultureInfo.InvariantCulture)}";
        if (record.SourceUrl != expectedSource)
            throw new InvalidDataException($"Wikipedia record {ordinal} has an invalid revision source URL.");
        if (!DateTimeOffset.TryParseExact(record.RevisionTimestampUtc, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp) || timestamp.Offset != TimeSpan.Zero)
            throw new InvalidDataException($"Wikipedia record {ordinal} has a non-canonical UTC timestamp.");
        ValidateHash(record.RawWikitextSha256, "RawWikitextSha256", ordinal);
        ValidateHash(record.TextSha256, "TextSha256", ordinal);
        var textBytes = WikipediaTextNormaliserV1.StrictUtf8ByteCount(record.Text);
        if (textBytes is < 256 or > WikipediaTextNormaliserV1.MaximumOutputUtf8Bytes || record.Text.Contains('\r'))
            throw new InvalidDataException($"Wikipedia record {ordinal} text violates the normalised text bounds.");
        if (record.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 40)
            throw new InvalidDataException($"Wikipedia record {ordinal} has fewer than 40 whitespace-delimited tokens.");
        var actualTextHash = Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(record.Text))).ToLowerInvariant();
        if (record.TextSha256 != actualTextHash)
            throw new InvalidDataException($"Wikipedia record {ordinal} TextSha256 does not match its text.");

        var candidate = new WikipediaCandidate(new WikipediaIndexEntry(0, record.PageId, record.Title));
        if (previous is not null && previous.CompareTo(candidate) >= 0)
            throw new InvalidDataException($"Wikipedia record {ordinal} is not in strict selection-key order.");
        previous = candidate;

        using var canonical = new MemoryStream(lineBytes.Length + 1);
        using (var writer = new CanonicalJsonWriter(canonical))
        {
            WikipediaReferenceBuilder.WriteRecord(writer, record);
            writer.WriteLine();
        }
        if (!canonical.ToArray().AsSpan().SequenceEqual(JoinLineFeed(lineBytes)))
            throw new InvalidDataException($"Wikipedia record {ordinal} is not canonical DataForge JSON.");
    }

    private static byte[] JoinLineFeed(ReadOnlySpan<byte> bytes)
    {
        var result = new byte[bytes.Length + 1];
        bytes.CopyTo(result);
        result[^1] = (byte)'\n';
        return result;
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new InvalidDataException($"Wikipedia record property '{name}' must be a string.");

    private static ulong RequiredUInt64(JsonElement root, string name) =>
        root.GetProperty(name).GetUInt64();

    private static void ValidateRecordHash(string value, string field) =>
        ValidateHash(value, field, 0);

    private static void ValidateHash(string value, string field, int ordinal)
    {
        if (value.Length != 64 || value.Any(static character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException(ordinal == 0 ? $"Wikipedia source field '{field}' is not a lowercase SHA-256." : $"Wikipedia record {ordinal} field '{field}' is not a lowercase SHA-256.");
    }

    private static int ParseCandidateLimit(IReadOnlyDictionary<string, string> source)
    {
        if (!int.TryParse(RequireValue(source, "candidateLimitUsed"), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) ||
            limit is < WikipediaCandidateSelector.InitialCandidateLimit or > WikipediaCandidateSelector.MaximumCandidateLimit ||
            (limit & (limit - 1)) != 0)
            throw new InvalidDataException("Wikipedia manifest candidateLimitUsed is invalid.");
        return limit;
    }

    private static void VerifySupportFile(string path, string expectedHash)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Wikipedia support file '{path}' is missing.", path);
        ValidateRecordHash(expectedHash, Path.GetFileName(path));
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.SequentialScan);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (actual != expectedHash)
            throw new InvalidDataException($"Wikipedia support file hash mismatch for '{Path.GetFileName(path)}'.");
    }

    private static void ValidateSha1(IReadOnlyDictionary<string, string> source, string field)
    {
        var value = RequireValue(source, field);
        if (value.Length != 40 || value.Any(static character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Wikipedia source field '{field}' is not a lowercase SHA-1.");
    }

    private static void ValidateSha256(IReadOnlyDictionary<string, string> source, string field) =>
        ValidateRecordHash(RequireValue(source, field), field);

    private static string RequireValue(IReadOnlyDictionary<string, string> source, string key) =>
        source.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"Wikipedia manifest is missing source field '{key}'.");

    private static void Require(IReadOnlyDictionary<string, string> source, string key, string expected)
    {
        if (RequireValue(source, key) != expected)
            throw new InvalidDataException($"Wikipedia source field '{key}' is not the pinned v1 value.");
    }
}
