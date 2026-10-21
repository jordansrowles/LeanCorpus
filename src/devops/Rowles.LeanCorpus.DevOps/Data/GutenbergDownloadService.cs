using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record GutenbergDownloadRequest
{
    public required string RepositoryRoot { get; init; }

    public required int BookCount { get; init; }

    public string? OutputDirectory { get; init; }
}

internal sealed record GutenbergBookRecord(int Id, string Sha256, long Length, string? SourceUrl, bool WasPresent);

internal sealed record GutenbergDownloadResult(
    CommandResult CommandResult,
    string OutputDirectory,
    string ManifestPath,
    int RequestedCount,
    IReadOnlyList<GutenbergBookRecord> Books,
    IReadOnlyList<int> FailedBookIds,
    string? CatalogueSha256);

internal sealed class GutenbergDownloadService
{
    private const string CatalogueUrl = "https://www.gutenberg.org/cache/epub/feeds/pg_catalog.csv.gz";
    private const int MaximumCatalogueBytes = 128 * 1024 * 1024;
    private static readonly TimeSpan DefaultRequestDelay = TimeSpan.FromSeconds(1);
    private static readonly int[] SeedBookIds =
    [
        9, 11, 12, 16, 23, 26, 35, 43, 45, 46, 47, 55, 61, 74, 84, 98, 113, 158, 174, 209, 215, 244,
        345, 514, 768, 776, 829, 844, 863, 1080, 1184, 1232, 1260, 1342, 1399, 1400, 1497, 1512, 1513, 1661,
        1952, 2097, 2148, 2542, 2554, 2591, 2701, 2814, 3207, 3268, 3825, 4280, 4300, 4517, 5200, 5348,
        5740, 5765, 6130, 6593, 6761, 7370, 7849, 8800, 10148, 10676, 11231, 15399, 16328, 16389, 18328,
        19337, 19942, 20203, 22381, 25344, 27827, 28054, 30254, 36, 42671
    ];

    private static readonly string[] BookUrlTemplates =
    [
        "https://www.gutenberg.org/files/{0}/{0}-0.txt",
        "https://www.gutenberg.org/files/{0}/{0}.txt",
        "https://www.gutenberg.org/cache/epub/{0}/pg{0}.txt"
    ];

    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task> _delay;
    private readonly Action<string>? _progress;

    public GutenbergDownloadService(
        HttpClient httpClient,
        Action<string>? progress = null,
        Func<CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _progress = progress;
        _delay = delay ?? (cancellationToken => Task.Delay(DefaultRequestDelay, cancellationToken));
    }

    public async Task<GutenbergDownloadResult> DownloadAsync(
        GutenbergDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.BookCount < 1)
        {
            return Failure(OutcomeKind.ValidationFailure, "Gutenberg BookCount must be a positive integer.", request, []);
        }
        if (string.IsNullOrWhiteSpace(request.RepositoryRoot))
        {
            return Failure(OutcomeKind.ValidationFailure, "A repository root is required to resolve the Gutenberg data directory.", request, []);
        }

        var repositoryRoot = Path.GetFullPath(request.RepositoryRoot);
        var dataRoot = Path.Combine(repositoryRoot, "artifacts", "benchmark", "cache", "data");
        var outputDirectory = Path.GetFullPath(request.OutputDirectory is null
            ? Path.Combine(dataRoot, "gutenberg-ebooks")
            : Path.IsPathRooted(request.OutputDirectory)
                ? request.OutputDirectory
                : Path.Combine(repositoryRoot, request.OutputDirectory));
        var cataloguePath = Path.Combine(dataRoot, ".gutenberg-catalog.csv");
        var manifestPath = Path.Combine(outputDirectory, "gutenberg-manifest.json");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(dataRoot);

            string? catalogueSha256 = null;
            var bookIds = new List<int>(request.BookCount);
            bookIds.AddRange(SeedBookIds.Take(Math.Min(request.BookCount, SeedBookIds.Length)));
            if (request.BookCount > SeedBookIds.Length)
            {
                catalogueSha256 = await EnsureCatalogueAsync(cataloguePath, cancellationToken).ConfigureAwait(false);
                var exclusions = SeedBookIds.ToHashSet();
                using var catalogueReader = new StreamReader(cataloguePath, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
                var remaining = request.BookCount - bookIds.Count;
                bookIds.AddRange(ReadCatalogueIds(catalogueReader, exclusions, remaining));
            }

            var books = new List<GutenbergBookRecord>(bookIds.Count);
            var failedBookIds = new List<int>();
            var sawNetworkFailure = false;
            for (var index = 0; index < bookIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = bookIds[index];
                var destination = Path.Combine(outputDirectory, $"{id}.txt");
                if (File.Exists(destination) && new FileInfo(destination).Length > 0)
                {
                    books.Add(new GutenbergBookRecord(id, await Sha256Async(destination, cancellationToken).ConfigureAwait(false),
                        new FileInfo(destination).Length, null, WasPresent: true));
                    _progress?.Invoke($"[{index + 1}/{bookIds.Count}] Skipping {id}; the file is already present.");
                    continue;
                }

                var downloaded = await DownloadBookAsync(id, destination, index + 1, bookIds.Count, cancellationToken).ConfigureAwait(false);
                if (downloaded.Record is not null)
                {
                    books.Add(downloaded.Record);
                }
                else
                {
                    failedBookIds.Add(id);
                    sawNetworkFailure |= downloaded.NetworkFailure;
                    _progress?.Invoke($"[{index + 1}/{bookIds.Count}] Could not download Gutenberg book {id}: {downloaded.Error}");
                }

                if (index + 1 < bookIds.Count)
                {
                    await _delay(cancellationToken).ConfigureAwait(false);
                }
            }

            await WriteManifestAsync(manifestPath, new GutenbergManifest
            {
                RequestedCount = request.BookCount,
                SelectedCount = bookIds.Count,
                DownloadedCount = books.Count(static book => !book.WasPresent),
                ExistingCount = books.Count(static book => book.WasPresent),
                FailedBookIds = failedBookIds,
                CatalogueSha256 = catalogueSha256,
                Books = books
            }, cancellationToken).ConfigureAwait(false);

            var catalogueShortfall = Math.Max(0, request.BookCount - bookIds.Count);
            var incomplete = failedBookIds.Count != 0 || catalogueShortfall != 0;
            var outcome = !incomplete
                ? OutcomeKind.Success
                : sawNetworkFailure ? OutcomeKind.InfrastructureFailure : OutcomeKind.CommandFailure;
            var message = !incomplete
                ? $"Gutenberg data is ready: {books.Count} books in '{outputDirectory}'."
                : $"Gutenberg data is incomplete: {books.Count} of {request.BookCount} requested books are available.";
            return new GutenbergDownloadResult(new CommandResult(outcome, message,
                    Details: !incomplete ? manifestPath : $"Failed book IDs: {string.Join(", ", failedBookIds)}. Catalogue shortfall: {catalogueShortfall}. Manifest: {manifestPath}"),
                outputDirectory, manifestPath, request.BookCount, books, failedBookIds, catalogueSha256);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or ArgumentException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not prepare Gutenberg benchmark data.", request, [], exception.Message,
                outputDirectory, manifestPath);
        }
    }

    internal static IReadOnlyList<int> ReadCatalogueIds(TextReader reader, ISet<int> excludedIds, int count)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(excludedIds);
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var headerLine = reader.ReadLine();
        if (headerLine is null)
            throw new InvalidDataException("The Gutenberg catalogue is empty.");
        var headers = ParseCsvLine(headerLine);
        var idColumn = headers.FindIndex(static header => string.Equals(header, "Text#", StringComparison.Ordinal));
        var languageColumn = headers.FindIndex(static header => string.Equals(header, "Language", StringComparison.Ordinal));
        if (idColumn < 0 || languageColumn < 0)
            throw new InvalidDataException("The Gutenberg catalogue must contain Text# and Language columns.");

        var ids = new SortedSet<int>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = ParseCsvLine(line);
            if (fields.Count <= Math.Max(idColumn, languageColumn) ||
                !string.Equals(fields[languageColumn], "en", StringComparison.Ordinal) ||
                !int.TryParse(fields[idColumn], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0 || excludedIds.Contains(id))
            {
                continue;
            }
            ids.Add(id);
        }

        return ids.Take(count).ToArray();
    }

    private async Task<string> EnsureCatalogueAsync(string cataloguePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(cataloguePath) || new FileInfo(cataloguePath).Length == 0)
        {
            var compressedPath = cataloguePath + ".gz.partial";
            var expandedPath = cataloguePath + ".partial";
            try
            {
                using var response = await SendGetAsync(CatalogueUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"The Gutenberg catalogue request returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
                if (response.Content.Headers.ContentLength is long length && length > MaximumCatalogueBytes)
                    throw new InvalidDataException("The compressed Gutenberg catalogue exceeds its size bound.");

                await using (var compressedOutput = new FileStream(compressedPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                {
                    await CopyBoundedAsync(input, compressedOutput, MaximumCatalogueBytes, cancellationToken).ConfigureAwait(false);
                }

                await using (var compressedInput = new FileStream(compressedPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                                 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var gzip = new System.IO.Compression.GZipStream(compressedInput, System.IO.Compression.CompressionMode.Decompress))
                await using (var expandedOutput = new FileStream(expandedPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await CopyBoundedAsync(gzip, expandedOutput, MaximumCatalogueBytes, cancellationToken).ConfigureAwait(false);
                    await expandedOutput.FlushAsync(cancellationToken).ConfigureAwait(false);
                    expandedOutput.Flush(flushToDisk: true);
                }

                using (var reader = new StreamReader(expandedPath, new UTF8Encoding(false, true)))
                    _ = ReadCatalogueIds(reader, new HashSet<int>(), 1);
                File.Move(expandedPath, cataloguePath, overwrite: true);
            }
            finally
            {
                TryDelete(compressedPath);
                TryDelete(expandedPath);
            }
        }

        return await Sha256Async(cataloguePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(GutenbergBookRecord? Record, string? Error, bool NetworkFailure)> DownloadBookAsync(
        int id,
        string destination,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        var partialPath = destination + ".partial";
        var errors = new List<string>();
        var sawNetworkFailure = false;
        foreach (var template in BookUrlTemplates)
        {
            var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, template, id);
            _progress?.Invoke($"[{index}/{total}] Fetching Gutenberg book {id}: {url}");
            try
            {
                using var response = await SendGetAsync(url, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    errors.Add($"HTTP 404 at {url}");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"HTTP {(int)response.StatusCode} at {url}");
                    sawNetworkFailure |= (int)response.StatusCode >= 500;
                    continue;
                }

                long length;
                string sha256;
                await using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                                 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[64 * 1024];
                    length = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                    {
                        length = checked(length + read);
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                    sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
                }

                if (length == 0)
                {
                    TryDelete(partialPath);
                    errors.Add($"Empty response at {url}");
                    continue;
                }

                File.Move(partialPath, destination, overwrite: true);
                return (new GutenbergBookRecord(id, sha256, length, url, WasPresent: false), null, false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                sawNetworkFailure = true;
                errors.Add($"{url}: {exception.Message}");
            }
            finally
            {
                TryDelete(partialPath);
            }
        }

        return (null, string.Join("; ", errors), sawNetworkFailure);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LeanCorpusDataBot", "1.0"));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];
            if (value == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (value == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(value);
            }
        }

        if (quoted)
            throw new InvalidDataException("The Gutenberg catalogue contains an unterminated quoted field.");
        fields.Add(field.ToString());
        return fields;
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
                throw new InvalidDataException($"The Gutenberg catalogue exceeded its {maximumBytes} byte bound.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    private static async Task WriteManifestAsync(string path, GutenbergManifest manifest, CancellationToken cancellationToken)
    {
        var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, options, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static GutenbergDownloadResult Failure(
        OutcomeKind outcome,
        string message,
        GutenbergDownloadRequest request,
        IReadOnlyList<int> failedBookIds,
        string? details = null,
        string? outputDirectory = null,
        string? manifestPath = null)
    {
        var root = string.IsNullOrWhiteSpace(request.RepositoryRoot) ? Environment.CurrentDirectory : Path.GetFullPath(request.RepositoryRoot);
        var output = outputDirectory ?? Path.Combine(root, "artifacts", "benchmark", "cache", "data", "gutenberg-ebooks");
        return new GutenbergDownloadResult(new CommandResult(outcome, message, Details: details), output,
            manifestPath ?? Path.Combine(output, "gutenberg-manifest.json"), request.BookCount, [], failedBookIds, null);
    }

    private sealed record GutenbergManifest
    {
        public int SchemaVersion { get; init; } = 1;

        public string Source { get; init; } = "Project Gutenberg";

        public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

        public int RequestedCount { get; init; }

        public int SelectedCount { get; init; }

        public int DownloadedCount { get; init; }

        public int ExistingCount { get; init; }

        public IReadOnlyList<int> FailedBookIds { get; init; } = [];

        public string? CatalogueSha256 { get; init; }

        public IReadOnlyList<GutenbergBookRecord> Books { get; init; } = [];
    }
}
