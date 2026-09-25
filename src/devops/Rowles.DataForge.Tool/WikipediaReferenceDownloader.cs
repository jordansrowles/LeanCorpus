using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rowles.DataForge.Tool;

public sealed record WikipediaDumpSource(
    string Wiki,
    string DumpDate,
    string PrimaryFilename,
    long PrimaryBytes,
    string PrimarySha1,
    string PrimarySha256,
    string IndexFilename,
    string IndexSha1,
    string IndexSha256,
    string ChecksumFileSha256,
    string DumpStatusSha256);

public static class WikipediaReferenceContract
{
    public const string Wiki = "enwiki";
    public const string DumpDate = "20260901";
    public const string DatasetId = "leancorpus-wikipedia-en";
    public const int DatasetVersion = 1;
    public const int TargetCount = 20_000;
    public const long PrimaryBytes = 26_797_495_184;
    public const string PrimarySha1 = "e0a53c30c3a3b444018df95704d3d101cd618440";
    public const string PrimaryFilename = "enwiki-20260901-pages-articles-multistream.xml.bz2";
    public const string IndexFilename = "enwiki-20260901-pages-articles-multistream-index.txt.bz2";
    public const string ChecksumFilename = "enwiki-20260901-sha1sums.txt";
    public const string StatusFilename = "dumpstatus.json";
    public const string DumpBaseUrl = "https://dumps.wikimedia.org/enwiki/20260901/";

    public static string CachePath(string repositoryRoot) => Path.Combine(repositoryRoot, "artifacts", "dataforge", "cache", "wikipedia", "enwiki", DumpDate);

    public static string ReferencePath(string repositoryRoot) => Path.Combine(repositoryRoot, "artifacts", "dataforge", "reference", "leancorpus-wikipedia-en-v1");
}

/// <summary>Downloads only the immutable files named by the Wikipedia v1 source contract.</summary>
public sealed class WikipediaReferenceDownloader(HttpClient httpClient)
{
    private const long GiB = 1024L * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private const string UserAgent = "Rowles.DataForge.WikipediaReference/1.0";
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<WikipediaDumpSource> DownloadAsync(string cacheDirectory, bool force = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        var root = Path.GetFullPath(cacheDirectory);
        Directory.CreateDirectory(root);
        var checksumPath = Path.Combine(root, WikipediaReferenceContract.ChecksumFilename);
        var statusPath = Path.Combine(root, WikipediaReferenceContract.StatusFilename);
        await DownloadSmallAsync(WikipediaReferenceContract.ChecksumFilename, checksumPath, 4 * 1024 * 1024, cancellationToken).ConfigureAwait(false);
        await DownloadSmallAsync(WikipediaReferenceContract.StatusFilename, statusPath, 32 * 1024 * 1024, cancellationToken).ConfigureAwait(false);

        var indexSha1 = ReadPublishedSha1(checksumPath, WikipediaReferenceContract.IndexFilename);
        var primarySha1 = ReadPublishedSha1(checksumPath, WikipediaReferenceContract.PrimaryFilename);
        if (!string.Equals(primarySha1, WikipediaReferenceContract.PrimarySha1, StringComparison.Ordinal))
            throw new InvalidDataException($"Official primary SHA-1 mismatch: pinned {WikipediaReferenceContract.PrimarySha1}, published {primarySha1}.");
        ValidateDumpStatus(statusPath);

        EnsureFreeSpace(root, WikipediaReferenceContract.PrimaryBytes + 5 * GiB);
        var primaryPath = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
        var indexPath = Path.Combine(root, WikipediaReferenceContract.IndexFilename);
        await DownloadVerifiedFileAsync(WikipediaReferenceContract.PrimaryFilename, primaryPath, WikipediaReferenceContract.PrimaryBytes,
            primarySha1, force, cancellationToken).ConfigureAwait(false);
        await DownloadVerifiedFileAsync(WikipediaReferenceContract.IndexFilename, indexPath, null, indexSha1, force, cancellationToken).ConfigureAwait(false);

        var source = new WikipediaDumpSource(
            WikipediaReferenceContract.Wiki,
            WikipediaReferenceContract.DumpDate,
            WikipediaReferenceContract.PrimaryFilename,
            WikipediaReferenceContract.PrimaryBytes,
            primarySha1,
            await Sha256Async(primaryPath, cancellationToken).ConfigureAwait(false),
            WikipediaReferenceContract.IndexFilename,
            indexSha1,
            await Sha256Async(indexPath, cancellationToken).ConfigureAwait(false),
            await Sha256Async(checksumPath, cancellationToken).ConfigureAwait(false),
            await Sha256Async(statusPath, cancellationToken).ConfigureAwait(false));
        await WriteSourceMetadataAsync(root, source, cancellationToken).ConfigureAwait(false);
        return source;
    }

    public static string ReadPublishedSha1(string checksumPath, string filename)
    {
        using var reader = new StreamReader(checksumPath, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2 || !string.Equals(fields[^1].TrimStart('*'), filename, StringComparison.Ordinal))
                continue;
            var sha1 = fields[0].ToLowerInvariant();
            if (sha1.Length != 40 || sha1.Any(static value => !Uri.IsHexDigit(value)))
                throw new InvalidDataException($"Invalid published SHA-1 for '{filename}'.");
            return sha1;
        }
        throw new InvalidDataException($"Pinned checksum file has no SHA-1 entry for '{filename}'.");
    }

    private async Task DownloadSmallAsync(string filename, string destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var partialPath = destination + ".partial";
        using var response = await SendPinnedGetAsync(filename, null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);
        if (response.Content.Headers.ContentLength is long declared && declared > maximumBytes)
            throw new InvalidDataException($"Wikimedia metadata file '{filename}' exceeds its size bound.");
        await using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            await CopyBoundedAsync(input, output, maximumBytes, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }
        File.Move(partialPath, destination, overwrite: true);
    }

    public async Task DownloadVerifiedFileAsync(string filename, string destination, long? expectedLength, string expectedSha1,
        bool force, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (filename is not (WikipediaReferenceContract.PrimaryFilename or WikipediaReferenceContract.IndexFilename))
            throw new ArgumentException("Only the pinned Wikipedia primary dump and multistream index can use verified large-file download.", nameof(filename));
        if (File.Exists(destination))
        {
            var existingLength = new FileInfo(destination).Length;
            if ((!expectedLength.HasValue || existingLength == expectedLength.Value) &&
                string.Equals(await Sha1Async(destination, cancellationToken).ConfigureAwait(false), expectedSha1, StringComparison.Ordinal))
                return;
            var badHash = await Sha1Async(destination, cancellationToken).ConfigureAwait(false);
            if (force)
                File.Delete(destination);
            else
                File.Move(destination, destination + ".invalid-" + badHash[..12], overwrite: true);
        }

        var partialPath = destination + ".partial";
        var resumeAt = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        var response = await SendPinnedGetAsync(filename, resumeAt > 0 ? resumeAt : null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && resumeAt > 0)
        {
            response.Dispose();
            File.Delete(partialPath);
            resumeAt = 0;
            response = await SendPinnedGetAsync(filename, null, cancellationToken).ConfigureAwait(false);
        }
        using (response)
        {
        EnsureSuccess(response);

        var append = false;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != resumeAt)
                throw new InvalidDataException($"Wikimedia returned an incorrect range start while resuming '{filename}'.");
            append = resumeAt > 0;
        }
        else if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidDataException($"Wikimedia returned unexpected status {(int)response.StatusCode} for '{filename}'.");
        }

        if (expectedLength is long expected && response.Content.Headers.ContentRange?.Length is long rangeLength && rangeLength != expected)
            throw new InvalidDataException($"Wikimedia declared {rangeLength} bytes for '{filename}', expected {expected}.");

        await using (var output = new FileStream(partialPath, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None,
                         1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            var remaining = expectedLength is long size ? size - (append ? resumeAt : 0) : long.MaxValue;
            if (remaining < 0)
                throw new InvalidDataException($"Partial file '{filename}' already exceeds the expected byte count.");
            await CopyBoundedAsync(input, output, remaining, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }

        var actualLength = new FileInfo(partialPath).Length;
        if (expectedLength.HasValue && actualLength != expectedLength.Value)
            throw new InvalidDataException($"Downloaded '{filename}' has {actualLength} bytes, expected {expectedLength.Value}. The partial file is retained for resume.");

        var actualSha1 = await Sha1Async(partialPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualSha1, expectedSha1, StringComparison.Ordinal))
        {
            if (force)
                File.Delete(partialPath);
            else
                File.Move(partialPath, partialPath + ".invalid-" + actualSha1[..12], overwrite: true);
            throw new InvalidDataException($"SHA-1 mismatch for '{filename}': expected {expectedSha1}, actual {actualSha1}.");
        }
        File.Move(partialPath, destination, overwrite: true);
        }
    }

    private async Task<HttpResponseMessage> SendPinnedGetAsync(string filename, long? rangeStart, CancellationToken cancellationToken)
    {
        var current = new Uri(WikipediaReferenceContract.DumpBaseUrl + Uri.EscapeDataString(filename));
        for (var redirects = 0; redirects <= MaximumRedirects; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            if (rangeStart.HasValue)
                request.Headers.Range = new RangeHeaderValue(rangeStart.Value, null);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new InvalidDataException($"Wikimedia returned an empty redirect for '{filename}'.");
            var redirected = location.IsAbsoluteUri ? location : new Uri(current, location);
            if (redirected.Scheme != Uri.UriSchemeHttps || !redirected.Host.Equals("dumps.wikimedia.org", StringComparison.OrdinalIgnoreCase) ||
                !redirected.AbsolutePath.StartsWith("/enwiki/20260901/", StringComparison.Ordinal))
                throw new InvalidDataException($"Wikimedia redirected '{filename}' outside the pinned HTTPS dump path.");
            current = redirected;
        }
        throw new InvalidDataException($"Wikimedia exceeded {MaximumRedirects} redirects for '{filename}'.");
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            copied = checked(copied + read);
            if (copied > maximumBytes)
                throw new InvalidDataException($"Wikimedia response exceeded its {maximumBytes} byte bound.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
            throw new HttpRequestException($"Wikimedia returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    internal static void ValidateDumpStatus(string statusPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(statusPath), new JsonDocumentOptions { MaxDepth = 64 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Pinned Wikimedia dumpstatus.json is not a JSON object.");
        var json = File.ReadAllText(statusPath, new UTF8Encoding(false, true));
        if (!json.Contains(WikipediaReferenceContract.PrimaryFilename, StringComparison.Ordinal) ||
            !json.Contains(WikipediaReferenceContract.IndexFilename, StringComparison.Ordinal))
            throw new InvalidDataException("Pinned Wikimedia dump status does not identify both required source files.");
    }

    private static void EnsureFreeSpace(string directory, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
                throw new IOException("The source cache has no volume root.");
            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < requiredBytes)
                throw new IOException($"Wikipedia source download requires {requiredBytes.ToString(CultureInfo.InvariantCulture)} free bytes; {drive.AvailableFreeSpace.ToString(CultureInfo.InvariantCulture)} are available.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (exception.Message.StartsWith("Wikipedia source download requires", StringComparison.Ordinal))
                throw;
            Console.Error.WriteLine($"Warning: could not determine free space for the Wikipedia source cache: {exception.Message}");
        }
    }

    private static async Task<string> Sha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task WriteSourceMetadataAsync(string root, WikipediaDumpSource source, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, "source.json");
        var partial = path + ".partial";
        await using (var stream = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, source, new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
        File.Move(partial, path, overwrite: true);
    }
}
