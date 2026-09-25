using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tool;

public sealed record WikipediaReferenceRecord(
    string Id,
    ulong PageId,
    ulong RevisionId,
    string RevisionTimestampUtc,
    string Title,
    string SourceUrl,
    string RawWikitextSha256,
    string TextSha256,
    string Text);

public sealed record WikipediaReferenceBuildResult(
    string OutputDirectory,
    int CandidateLimit,
    long IndexEntriesScanned,
    int CandidateIdsInspected,
    int EligibleCount,
    int SelectedCount,
    int UniqueOffsetsRead,
    DataForgeManifest Manifest,
    IReadOnlyDictionary<string, long> RejectionCounts,
    long ElapsedMilliseconds);

/// <summary>Builds the immutable Wikipedia v1 imported DataForge artefact from verified local source files.</summary>
public static class WikipediaReferenceBuilder
{
    private const string SelectionAlgorithm = "page-id-sha256-v1";
    private const string SelectionSalt = "leancorpus-wikipedia-en-v1";
    private const string SharpCompressVersion = "0.50.4";

    public static WikipediaReferenceBuildResult Build(string cacheDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException($"Wikipedia v1 output '{output}' already exists. It is immutable and will not be overwritten.");
        var source = WikipediaReferenceSource.Verify(cacheDirectory);
        return BuildCore(cacheDirectory, output, source, WikipediaReferenceContract.TargetCount,
            WikipediaCandidateSelector.InitialCandidateLimit, WikipediaCandidateSelector.MaximumCandidateLimit);
    }

    internal static WikipediaReferenceBuildResult BuildFixture(
        string cacheDirectory,
        string outputDirectory,
        int targetCount,
        int initialCandidateLimit = WikipediaCandidateSelector.InitialCandidateLimit,
        int maximumCandidateLimit = WikipediaCandidateSelector.MaximumCandidateLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCount);
        ValidateCandidateLimits(initialCandidateLimit, maximumCandidateLimit);
        var cache = Path.GetFullPath(cacheDirectory);
        var primaryPath = Path.Combine(cache, WikipediaReferenceContract.PrimaryFilename);
        var indexPath = Path.Combine(cache, WikipediaReferenceContract.IndexFilename);
        if (!File.Exists(primaryPath) || !File.Exists(indexPath))
            throw new FileNotFoundException("Miniature Wikipedia fixture is missing its primary dump or multistream index.");
        var source = new WikipediaDumpSource(
            WikipediaReferenceContract.Wiki,
            WikipediaReferenceContract.DumpDate,
            WikipediaReferenceContract.PrimaryFilename,
            WikipediaReferenceContract.PrimaryBytes,
            WikipediaReferenceContract.PrimarySha1,
            Sha256(primaryPath),
            WikipediaReferenceContract.IndexFilename,
            Sha1(indexPath),
            Sha256(indexPath),
            Sha256(indexPath),
            Sha256(primaryPath));
        return BuildCore(cache, outputDirectory, source, targetCount, initialCandidateLimit, maximumCandidateLimit);
    }

    private static WikipediaReferenceBuildResult BuildCore(
        string cacheDirectory,
        string outputDirectory,
        WikipediaDumpSource source,
        int targetCount,
        int initialCandidateLimit,
        int maximumCandidateLimit)
    {
        var timer = Stopwatch.StartNew();
        var output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException($"Wikipedia v1 output '{output}' already exists. It is immutable and will not be overwritten.");
        var parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Output directory must have a parent directory.", nameof(outputDirectory));
        Directory.CreateDirectory(parent);
        var temporary = output + ".building-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary);

        try
        {
            var indexPath = Path.Combine(Path.GetFullPath(cacheDirectory), WikipediaReferenceContract.IndexFilename);
            var primaryPath = Path.Combine(Path.GetFullPath(cacheDirectory), WikipediaReferenceContract.PrimaryFilename);
            var cached = new Dictionary<ulong, CandidateSnapshot>();
            var offsetsRead = new HashSet<long>();
            var extractor = new WikipediaPageExtractor();
            var candidateLimit = initialCandidateLimit;
            var passes = 0;
            long indexEntriesScanned = 0;
            WikipediaCandidateSelection selection;
            WikipediaCandidate[] selected;

            while (true)
            {
                passes++;
                selection = WikipediaReferenceSource.ReadIndex(indexPath, candidateLimit);
                indexEntriesScanned = selection.IndexEntriesScanned;
                var unseen = selection.Candidates.Where(candidate => !cached.ContainsKey(candidate.Entry.PageId)).ToArray();
                foreach (var offset in extractor.ExtractEach(primaryPath, selection.UniqueOffsets, unseen, page =>
                         {
                             if (!cached.TryAdd(page.PageId, EvaluatePage(page)))
                                 throw new InvalidDataException($"Wikipedia page {page.PageId.ToString(CultureInfo.InvariantCulture)} was inspected more than once.");
                         }))
                    offsetsRead.Add(offset);

                var eligible = selection.Candidates.Where(candidate => cached[candidate.Entry.PageId].IsEligible).ToArray();
                if (eligible.Length >= targetCount)
                {
                    selected = eligible[..targetCount];
                    break;
                }
                if (candidateLimit == maximumCandidateLimit)
                    throw new InvalidDataException($"Only {eligible.Length} eligible Wikipedia pages were found among {candidateLimit} candidates; v1 requires exactly {targetCount}.");
                candidateLimit = Math.Min(candidateLimit * 2, maximumCandidateLimit);
            }

            var selectedCache = selected.ToDictionary(static candidate => candidate.Entry.PageId,
                candidate => cached[candidate.Entry.PageId]);
            var staged = new Dictionary<ulong, StageFrame>(selected.Length);
            var stagingPath = Path.Combine(temporary, "selected.records.stage");
            var finalRecordsPath = Path.Combine(temporary, "records.ndjson");
            using (var staging = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 64 * 1024, FileOptions.SequentialScan))
            {
                foreach (var offset in extractor.ExtractEach(primaryPath, selection.UniqueOffsets, selected, page =>
                         {
                             var refreshed = EvaluatePage(page);
                             if (!selectedCache.TryGetValue(page.PageId, out var expected) || !expected.ContentEquals(refreshed))
                                 throw new InvalidDataException($"Wikipedia page {page.PageId.ToString(CultureInfo.InvariantCulture)} changed between eligibility selection and final extraction.");
                             var record = ToRecord(page, refreshed);
                             using var bytes = new MemoryStream();
                             using (var writer = new CanonicalJsonWriter(bytes))
                                 WriteRecord(writer, record);
                             if (bytes.Length is < 1 or > 4_194_304)
                                 throw new InvalidDataException($"Wikipedia record {record.Id} exceeds the canonical record size bound.");
                             var frameOffset = staging.Position;
                             Span<byte> length = stackalloc byte[sizeof(int)];
                             BinaryPrimitives.WriteInt32LittleEndian(length, checked((int)bytes.Length));
                             staging.Write(length);
                             bytes.Position = 0;
                             bytes.CopyTo(staging);
                             if (!staged.TryAdd(page.PageId, new StageFrame(frameOffset, checked((int)bytes.Length))))
                                 throw new InvalidDataException($"Duplicate selected Wikipedia page {page.PageId.ToString(CultureInfo.InvariantCulture)}.");
                         }))
                    offsetsRead.Add(offset);
                staging.Flush(flushToDisk: true);
            }

            if (staged.Count != selected.Length)
                throw new InvalidDataException($"Final extraction staged {staged.Count} Wikipedia records; expected {selected.Length}.");
            var logicalBytes = WriteOrderedRecords(stagingPath, finalRecordsPath, selected, staged);
            File.Delete(stagingPath);

            var supportPath = Path.Combine(temporary, "LICENSES");
            Directory.CreateDirectory(supportPath);
            var licenceSource = Path.Combine(Path.GetDirectoryName(typeof(WikipediaReferenceBuilder).Assembly.Location) ?? string.Empty,
                "Licenses", "CC-BY-SA-4.0.txt");
            if (!File.Exists(licenceSource))
                licenceSource = Path.Combine(Directory.GetCurrentDirectory(), "src", "devops", "Rowles.DataForge.Tool", "Licenses", "CC-BY-SA-4.0.txt");
            if (!File.Exists(licenceSource))
                throw new FileNotFoundException("The repository CC BY-SA 4.0 legal text is missing.", licenceSource);
            var licenceDestination = Path.Combine(supportPath, "CC-BY-SA-4.0.txt");
            File.Copy(licenceSource, licenceDestination);
            var attributionPath = Path.Combine(supportPath, "attribution.txt");
            var attribution = string.Join('\n',
                "Dataset: leancorpus-wikipedia-en-v1",
                "Source: English Wikipedia contributors",
                "Source snapshot: enwiki 20260901",
                "Licence: CC BY-SA 4.0",
                "Licence text: LICENSES/CC-BY-SA-4.0.txt",
                "Licence URL: https://creativecommons.org/licenses/by-sa/4.0/",
                "Per-record revision attribution: SourceUrl field in records.ndjson",
                "Transformation: LeanCorpus DataForge Wikipedia visible-text normaliser v1",
                string.Empty);
            File.WriteAllText(attributionPath, attribution, new UTF8Encoding(false));

            var sourceMetadata = CreateManifestSource(source, candidateLimit, licenceDestination, attributionPath);
            var summaries = CreateSummaries(selected.Select(candidate => cached[candidate.Entry.PageId]).ToArray(), candidateLimit, CountRejections(cached.Values));
            var contentHash = Sha256(finalRecordsPath);
            var manifest = new DataForgeManifest(
                DataForgeVersions.ManifestSchemaVersion,
                DataForgeVersions.CanonicalFormatVersion,
                DataForgeVersions.DataForgeVersion,
                DataForgeSourceKind.Imported,
                ProfileId: null,
                ProfileVersion: null,
                Seed: null,
                RecordCount: selected.Length,
                Parameters: [],
                Dependencies: [new DataForgeDependencyVersion("SharpCompress", SharpCompressVersion)],
                LogicalByteCount: logicalBytes,
                ContentSha256: contentHash,
                ArtefactSha256: contentHash,
                Summaries: summaries,
                Source: sourceMetadata);
            var manifestPath = Path.Combine(temporary, "manifest.json");
            using (var stream = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.SequentialScan))
            {
                DataForgeManifestCodec.Write(stream, manifest);
                stream.Flush(flushToDisk: true);
            }

            timer.Stop();
            var rejectionCounts = CountRejections(cached.Values);
            WriteBuildEvidence(temporary, new BuildEvidence(
                WikipediaReferenceContract.DatasetId,
                WikipediaReferenceContract.DatasetVersion,
                candidateLimit,
                selection.IndexEntriesScanned,
                indexEntriesScanned * passes,
                cached.Count,
                cached.Values.Count(static snapshot => snapshot.IsEligible),
                selected.Length,
                offsetsRead.Count,
                passes,
                timer.ElapsedMilliseconds,
                rejectionCounts));

            _ = WikipediaReferenceVerifier.Verify(temporary, targetCount, initialCandidateLimit);
            Directory.Move(temporary, output);
            return new WikipediaReferenceBuildResult(output, candidateLimit, indexEntriesScanned, cached.Count,
                cached.Values.Count(static snapshot => snapshot.IsEligible), selected.Length, offsetsRead.Count,
                manifest, rejectionCounts, timer.ElapsedMilliseconds);
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    private static void ValidateCandidateLimits(int initialCandidateLimit, int maximumCandidateLimit)
    {
        if (initialCandidateLimit is < 1 or > WikipediaCandidateSelector.MaximumCandidateLimit ||
            (initialCandidateLimit & (initialCandidateLimit - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(initialCandidateLimit));
        if (maximumCandidateLimit < initialCandidateLimit ||
            maximumCandidateLimit > WikipediaCandidateSelector.MaximumCandidateLimit ||
            (maximumCandidateLimit & (maximumCandidateLimit - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCandidateLimit));
    }

    public static void WriteRecord(CanonicalJsonWriter writer, WikipediaReferenceRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);
        writer.WriteStartObject();
        writer.WritePropertyName("Id"); writer.WriteStringValue(record.Id);
        writer.WritePropertyName("PageId"); writer.WriteUInt64Value(record.PageId);
        writer.WritePropertyName("RevisionId"); writer.WriteUInt64Value(record.RevisionId);
        writer.WritePropertyName("RevisionTimestampUtc"); writer.WriteStringValue(record.RevisionTimestampUtc);
        writer.WritePropertyName("Title"); writer.WriteStringValue(record.Title);
        writer.WritePropertyName("SourceUrl"); writer.WriteStringValue(record.SourceUrl);
        writer.WritePropertyName("RawWikitextSha256"); writer.WriteStringValue(record.RawWikitextSha256);
        writer.WritePropertyName("TextSha256"); writer.WriteStringValue(record.TextSha256);
        writer.WritePropertyName("Text"); writer.WriteStringValue(record.Text);
        writer.WriteEndObject();
    }

    private static CandidateSnapshot EvaluatePage(WikipediaPageRevision page)
    {
        DateTimeOffset? timestamp = TryParseUtcTimestamp(page.RevisionTimestamp, out var parsed) ? parsed : null;
        var preflight = WikipediaEligibility.Evaluate(page.NamespaceId, page.PageId, page.HasRedirect, page.RevisionId, timestamp, rawText: null);
        if (preflight.Rejection != WikipediaEligibilityRejection.MissingText)
            return CandidateSnapshot.Rejected(preflight.Rejection);
        if (page.RawTextTooLarge)
            return CandidateSnapshot.Rejected(WikipediaEligibilityRejection.RawTooLarge);
        var eligibility = WikipediaEligibility.Evaluate(page.NamespaceId, page.PageId, page.HasRedirect,
            page.RevisionId, timestamp, page.RawText);
        if (!eligibility.IsEligible || eligibility.Text is null || page.RawText is null)
            return CandidateSnapshot.Rejected(eligibility.Rejection);
        var canonicalTimestamp = timestamp!.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var rawHash = HashUtf8(page.RawText);
        var textHash = HashUtf8(eligibility.Text);
        return new CandidateSnapshot(
            WikipediaEligibilityRejection.None,
            page.PageId,
            page.RevisionId,
            canonicalTimestamp,
            page.Title,
            rawHash,
            textHash,
            eligibility.RawUtf8Bytes,
            eligibility.TextUtf8Bytes,
            eligibility.TokenCount);
    }

    private static WikipediaReferenceRecord ToRecord(WikipediaPageRevision page, CandidateSnapshot snapshot)
    {
        if (page.RawText is null)
            throw new InvalidDataException($"Selected Wikipedia page {page.PageId.ToString(CultureInfo.InvariantCulture)} has no raw revision text.");
        var text = WikipediaTextNormaliserV1.Normalise(page.RawText);
        return new WikipediaReferenceRecord(
            $"enwiki-{page.PageId.ToString(CultureInfo.InvariantCulture)}",
            snapshot.PageId,
            snapshot.RevisionId,
            snapshot.RevisionTimestampUtc,
            snapshot.Title,
            $"https://en.wikipedia.org/w/index.php?oldid={snapshot.RevisionId.ToString(CultureInfo.InvariantCulture)}",
            snapshot.RawWikitextSha256,
            snapshot.TextSha256,
            text);
    }

    private static long WriteOrderedRecords(string stagingPath, string destinationPath, IReadOnlyList<WikipediaCandidate> selected,
        IReadOnlyDictionary<ulong, StageFrame> frames)
    {
        using var staging = new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
        using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        var lengthBytes = new byte[sizeof(int)];
        long total = 0;
        foreach (var candidate in selected)
        {
            var frame = frames[candidate.Entry.PageId];
            staging.Position = frame.Offset;
            staging.ReadExactly(lengthBytes);
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length != frame.Length || length is < 1 or > 4_194_304)
                throw new InvalidDataException("Wikipedia selected-record staging frame is invalid.");
            var remaining = length;
            while (remaining > 0)
            {
                var read = staging.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0) throw new EndOfStreamException("Wikipedia selected-record staging frame ended early.");
                output.Write(buffer, 0, read);
                remaining -= read;
            }
            output.WriteByte((byte)'\n');
            total += length + 1L;
        }
        output.Flush(flushToDisk: true);
        return total;
    }

    private static IReadOnlyDictionary<string, string> CreateManifestSource(WikipediaDumpSource source, int candidateLimit,
        string licencePath, string attributionPath) => new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["datasetId"] = WikipediaReferenceContract.DatasetId,
        ["datasetVersion"] = WikipediaReferenceContract.DatasetVersion.ToString(CultureInfo.InvariantCulture),
        ["wiki"] = source.Wiki,
        ["dumpDate"] = source.DumpDate,
        ["primaryFilename"] = source.PrimaryFilename,
        ["primaryBytes"] = source.PrimaryBytes.ToString(CultureInfo.InvariantCulture),
        ["primarySha1"] = source.PrimarySha1,
        ["primarySha256"] = source.PrimarySha256,
        ["indexFilename"] = source.IndexFilename,
        ["indexSha1"] = source.IndexSha1,
        ["indexSha256"] = source.IndexSha256,
        ["checksumFileSha256"] = source.ChecksumFileSha256,
        ["dumpStatusSha256"] = source.DumpStatusSha256,
        ["selectionAlgorithm"] = SelectionAlgorithm,
        ["selectionSalt"] = SelectionSalt,
        ["candidateLimitUsed"] = candidateLimit.ToString(CultureInfo.InvariantCulture),
        ["normaliser"] = WikipediaTextNormaliserV1.Version,
        ["eligibilityVersion"] = "1",
        ["licenceFileSha256"] = Sha256(licencePath),
        ["attributionFileSha256"] = Sha256(attributionPath)
    };

    private static IReadOnlyList<DataForgeSummary> CreateSummaries(IReadOnlyList<CandidateSnapshot> selected, int candidateLimit,
        IReadOnlyDictionary<string, long> rejectionCounts)
    {
        var byteLengths = selected.Select(static item => item.TextUtf8Bytes).Order().ToArray();
        var tokenCounts = selected.Select(static item => item.TokenCount).Order().ToArray();
        var summaries = new List<DataForgeSummary>
        {
            new("normalisedTextUtf8Bytes.min", byteLengths[0].ToString(CultureInfo.InvariantCulture)),
            new("normalisedTextUtf8Bytes.median", Percentile(byteLengths, 0.50).ToString(CultureInfo.InvariantCulture)),
            new("normalisedTextUtf8Bytes.p90", Percentile(byteLengths, 0.90).ToString(CultureInfo.InvariantCulture)),
            new("normalisedTextUtf8Bytes.p99", Percentile(byteLengths, 0.99).ToString(CultureInfo.InvariantCulture)),
            new("normalisedTextUtf8Bytes.max", byteLengths[^1].ToString(CultureInfo.InvariantCulture)),
            new("whitespaceTokens.min", tokenCounts[0].ToString(CultureInfo.InvariantCulture)),
            new("whitespaceTokens.median", Percentile(tokenCounts, 0.50).ToString(CultureInfo.InvariantCulture)),
            new("whitespaceTokens.p90", Percentile(tokenCounts, 0.90).ToString(CultureInfo.InvariantCulture)),
            new("whitespaceTokens.p99", Percentile(tokenCounts, 0.99).ToString(CultureInfo.InvariantCulture)),
            new("whitespaceTokens.max", tokenCounts[^1].ToString(CultureInfo.InvariantCulture)),
            new("uniqueTitleCount", selected.Select(static item => item.Title).Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture)),
            new("candidateLimitUsed", candidateLimit.ToString(CultureInfo.InvariantCulture))
        };
        foreach (var rejection in rejectionCounts)
            summaries.Add(new DataForgeSummary("rejected." + rejection.Key, rejection.Value.ToString(CultureInfo.InvariantCulture)));
        return summaries;
    }

    private static int Percentile(int[] sorted, double percentile) => sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1)];

    private static Dictionary<string, long> CountRejections(IEnumerable<CandidateSnapshot> snapshots) => snapshots
        .Where(static snapshot => !snapshot.IsEligible)
        .GroupBy(static snapshot => snapshot.Rejection.ToString(), StringComparer.Ordinal)
        .OrderBy(static group => group.Key, StringComparer.Ordinal)
        .ToDictionary(static group => group.Key, static group => (long)group.Count(), StringComparer.Ordinal);

    private static void WriteBuildEvidence(string directory, BuildEvidence evidence)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, options);
        using var stream = new FileStream(Path.Combine(directory, "build-evidence.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.WriteByte((byte)'\n');
        stream.Flush(flushToDisk: true);
    }

    private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value) || !(value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal)))
            return false;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp) && timestamp.Offset == TimeSpan.Zero;
    }

    private static string HashUtf8(string value)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Sha1(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    private sealed record CandidateSnapshot(
        WikipediaEligibilityRejection Rejection,
        ulong PageId,
        ulong RevisionId,
        string RevisionTimestampUtc,
        string Title,
        string RawWikitextSha256,
        string TextSha256,
        int RawUtf8Bytes,
        int TextUtf8Bytes,
        int TokenCount)
    {
        public bool IsEligible => Rejection == WikipediaEligibilityRejection.None;

        public bool ContentEquals(CandidateSnapshot other) =>
            Rejection == other.Rejection && PageId == other.PageId && RevisionId == other.RevisionId &&
            RevisionTimestampUtc == other.RevisionTimestampUtc && Title == other.Title &&
            RawWikitextSha256 == other.RawWikitextSha256 && TextSha256 == other.TextSha256 &&
            RawUtf8Bytes == other.RawUtf8Bytes && TextUtf8Bytes == other.TextUtf8Bytes && TokenCount == other.TokenCount;

        public static CandidateSnapshot Rejected(WikipediaEligibilityRejection rejection) => new(
            rejection, 0, 0, string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, 0);
    }

    private sealed record StageFrame(long Offset, int Length);
    private sealed record BuildEvidence(
        string DatasetId,
        int DatasetVersion,
        int CandidateLimitUsed,
        long IndexEntriesScanned,
        long TotalIndexEntriesScanned,
        int UniqueCandidatesInspected,
        int EligibleCandidates,
        int SelectedCount,
        int UniqueMultistreamOffsetsRead,
        int CandidatePasses,
        long ElapsedMilliseconds,
        IReadOnlyDictionary<string, long> RejectionCounts);
}
