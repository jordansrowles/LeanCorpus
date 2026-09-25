using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge.Workloads;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tool;

internal static class WikipediaReferenceSource
{
    public static WikipediaDumpSource Verify(string cacheDirectory)
    {
        var root = Path.GetFullPath(cacheDirectory);
        var checksumPath = Path.Combine(root, WikipediaReferenceContract.ChecksumFilename);
        var statusPath = Path.Combine(root, WikipediaReferenceContract.StatusFilename);
        var primaryPath = Path.Combine(root, WikipediaReferenceContract.PrimaryFilename);
        var indexPath = Path.Combine(root, WikipediaReferenceContract.IndexFilename);
        var metadataPath = Path.Combine(root, "source.json");
        foreach (var required in new[] { checksumPath, statusPath, primaryPath, indexPath, metadataPath })
            if (!File.Exists(required))
                throw new FileNotFoundException($"Pinned Wikipedia source file is missing: '{required}'. Run './devops dataforge reference download'.", required);

        var source = JsonSerializer.Deserialize<WikipediaDumpSource>(File.ReadAllBytes(metadataPath))
            ?? throw new InvalidDataException("Wikipedia source.json is empty or invalid.");
        if (source.Wiki != WikipediaReferenceContract.Wiki || source.DumpDate != WikipediaReferenceContract.DumpDate ||
            source.PrimaryFilename != WikipediaReferenceContract.PrimaryFilename || source.PrimaryBytes != WikipediaReferenceContract.PrimaryBytes ||
            source.PrimarySha1 != WikipediaReferenceContract.PrimarySha1 || source.IndexFilename != WikipediaReferenceContract.IndexFilename)
            throw new InvalidDataException("Wikipedia source.json does not match the immutable v1 source pin.");
        if (new FileInfo(primaryPath).Length != WikipediaReferenceContract.PrimaryBytes)
            throw new InvalidDataException("Pinned Wikipedia primary dump length does not match the v1 source pin.");

        var publishedPrimarySha1 = WikipediaReferenceDownloader.ReadPublishedSha1(checksumPath, WikipediaReferenceContract.PrimaryFilename);
        var publishedIndexSha1 = WikipediaReferenceDownloader.ReadPublishedSha1(checksumPath, WikipediaReferenceContract.IndexFilename);
        if (publishedPrimarySha1 != WikipediaReferenceContract.PrimarySha1 || publishedIndexSha1 != source.IndexSha1)
            throw new InvalidDataException("Pinned Wikimedia SHA-1 file disagrees with the verified source metadata.");
        WikipediaReferenceDownloader.ValidateDumpStatus(statusPath);

        var primarySha1 = Sha1(primaryPath);
        var primarySha256 = Sha256(primaryPath);
        var indexSha1 = Sha1(indexPath);
        var indexSha256 = Sha256(indexPath);
        var checksumSha256 = Sha256(checksumPath);
        var statusSha256 = Sha256(statusPath);
        if (primarySha1 != WikipediaReferenceContract.PrimarySha1 || primarySha256 != source.PrimarySha256 ||
            indexSha1 != source.IndexSha1 || indexSha256 != source.IndexSha256 ||
            checksumSha256 != source.ChecksumFileSha256 || statusSha256 != source.DumpStatusSha256)
            throw new InvalidDataException("Wikipedia source checksum verification failed.");
        return source;
    }

    public static WikipediaCandidateSelection ReadIndex(string indexPath, int candidateLimit)
    {
        using var file = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using var decompressor = BZip2Stream.Create(file, CompressionMode.Decompress, decompressConcatenated: false, leaveOpen: true);
        using var reader = new StreamReader(decompressor, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, 64 * 1024, leaveOpen: true);
        return WikipediaCandidateSelector.Select(reader, candidateLimit);
    }

    private static string Sha1(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
    }

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed class BoundedReadStream(Stream inner, long length, bool leaveOpen = true) : Stream
{
    private long remaining = length;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (remaining == 0) return 0;
        var read = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
        remaining -= read;
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        if (remaining == 0) return 0;
        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        remaining -= read;
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (remaining == 0) return 0;
        var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken).ConfigureAwait(false);
        remaining -= read;
        return read;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen) inner.Dispose();
        base.Dispose(disposing);
    }
}
