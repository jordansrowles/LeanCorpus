using System.Globalization;
using System.Text;
using System.Xml;
using Rowles.DataForge.Workloads;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Rowles.DataForge.Tool;

internal sealed record WikipediaPageRevision(
    ulong PageId,
    string Title,
    int NamespaceId,
    bool HasRedirect,
    ulong RevisionId,
    string? RevisionTimestamp,
    string? RawText,
    bool RawTextTooLarge);

internal sealed class WikipediaPageExtractor(long maximumMemberBytes = 128L * 1024 * 1024)
{
    private const int MaximumRawTextCharacters = 4 * 1024 * 1024;

    public IReadOnlyList<long> ExtractEach(
        string primaryPath,
        IReadOnlyList<long> offsets,
        IReadOnlyCollection<WikipediaCandidate> candidates,
        Action<WikipediaPageRevision> acceptPage)
    {
        ArgumentNullException.ThrowIfNull(acceptPage);
        if (maximumMemberBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumMemberBytes));
        var requestedByOffset = candidates.GroupBy(static candidate => candidate.Entry.Offset)
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        var orderedOffsets = offsets;
        var processedOffsets = new List<long>(requestedByOffset.Count);
        using var dump = new FileStream(primaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.RandomAccess);
        var lastOffset = dump.Length;

        foreach (var (offset, entries) in requestedByOffset.OrderBy(static pair => pair.Key))
        {
            processedOffsets.Add(offset);
            var nextOffset = FindNextOffset(orderedOffsets, offset, lastOffset);
            if (offset < 0 || nextOffset <= offset || nextOffset > dump.Length)
                throw new InvalidDataException($"Invalid multistream bounds [{offset}, {nextOffset}) for candidate pages {string.Join(',', entries.Select(static item => item.Entry.PageId))}.");
            dump.Position = offset;
            using var bounded = new BoundedReadStream(dump, nextOffset - offset, leaveOpen: true);
            using var decompressor = BZip2Stream.Create(bounded, CompressionMode.Decompress, decompressConcatenated: false, leaveOpen: true);
            using var decompressed = ReadMemberBounded(decompressor, offset, entries.Select(static item => item.Entry.PageId));
            var requestedIds = entries.Select(static item => item.Entry.PageId).ToHashSet();
            foreach (var page in WikipediaPageXmlParser.Parse(decompressed, requestedIds))
            {
                if (!requestedIds.Remove(page.PageId))
                    throw new InvalidDataException($"Wikipedia page {page.PageId.ToString(CultureInfo.InvariantCulture)} appears more than once in the requested source stream at offset {offset.ToString(CultureInfo.InvariantCulture)}.");
                acceptPage(page);
            }
            foreach (var missingPageId in requestedIds)
                throw new InvalidDataException($"Index candidate page {missingPageId.ToString(CultureInfo.InvariantCulture)} was not found in its advertised bzip2 stream at offset {offset.ToString(CultureInfo.InvariantCulture)}.");
        }
        return processedOffsets;
    }

    private MemoryStream ReadMemberBounded(Stream decompressor, long offset, IEnumerable<ulong> pageIds)
    {
        var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = decompressor.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + read > maximumMemberBytes)
            {
                output.Dispose();
                throw new InvalidDataException($"Bzip2 member at offset {offset.ToString(CultureInfo.InvariantCulture)} exceeds the {maximumMemberBytes.ToString(CultureInfo.InvariantCulture)} byte decompressed bound for pages {string.Join(',', pageIds)}.");
            }
            output.Write(buffer, 0, read);
        }
        output.Position = 0;
        return output;
    }

    private static long FindNextOffset(IReadOnlyList<long> offsets, long offset, long fileLength)
    {
        var low = 0;
        var high = offsets.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var value = offsets[middle];
            if (value == offset)
                return middle + 1 < offsets.Count ? offsets[middle + 1] : fileLength;
            if (value < offset) low = middle + 1;
            else high = middle - 1;
        }
        throw new InvalidDataException($"Candidate offset {offset.ToString(CultureInfo.InvariantCulture)} is absent from the validated index offset table.");
    }
}

internal static class WikipediaPageXmlParser
{
    private const int MaximumRawTextCharacters = 4 * 1024 * 1024;
    private static readonly XmlReaderSettings Settings = new()
    {
        Async = false,
        CheckCharacters = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = false,
        IgnoreWhitespace = false,
        XmlResolver = null
    };

    public static IEnumerable<WikipediaPageRevision> Parse(Stream xmlFragment, IReadOnlySet<ulong> requestedPageIds)
    {
        ArgumentNullException.ThrowIfNull(xmlFragment);
        ArgumentNullException.ThrowIfNull(requestedPageIds);
        using var reader = XmlReader.Create(xmlFragment, Settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "page")
                continue;
            using var subtree = reader.ReadSubtree();
            var page = ReadPage(subtree, requestedPageIds);
            if (page is not null)
                yield return page;
        }
    }

    private static WikipediaPageRevision? ReadPage(XmlReader reader, IReadOnlySet<ulong> requestedPageIds)
    {
        ulong pageId = 0;
        ulong revisionId = 0;
        var title = string.Empty;
        var namespaceId = int.MinValue;
        var hasRedirect = false;
        string? timestamp = null;
        string? rawText = null;
        var rawTextTooLarge = false;
        var inRevision = false;
        var revisionDepth = -1;

        var readNext = true;
        while (true)
        {
            if (readNext && !reader.Read())
                break;
            readNext = true;
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.Depth == 1)
                {
                    switch (reader.LocalName)
                    {
                        case "title": title = reader.ReadElementContentAsString(); readNext = false; continue;
                        case "ns":
                            if (!int.TryParse(reader.ReadElementContentAsString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out namespaceId))
                                throw new InvalidDataException("Wikipedia page namespace is not an invariant integer.");
                            readNext = false;
                            continue;
                        case "id":
                            if (!ulong.TryParse(reader.ReadElementContentAsString(), NumberStyles.None, CultureInfo.InvariantCulture, out pageId))
                                throw new InvalidDataException("Wikipedia page ID is not an unsigned invariant integer.");
                            if (!requestedPageIds.Contains(pageId))
                                return null;
                            readNext = false;
                            continue;
                        case "redirect": hasRedirect = true; continue;
                        case "revision":
                            inRevision = true;
                            revisionDepth = reader.Depth;
                            revisionId = 0;
                            timestamp = null;
                            rawText = null;
                            rawTextTooLarge = false;
                            if (reader.IsEmptyElement) inRevision = false;
                            continue;
                    }
                }

                if (inRevision && reader.Depth == revisionDepth + 1)
                {
                    switch (reader.LocalName)
                    {
                        case "id":
                            if (!ulong.TryParse(reader.ReadElementContentAsString(), NumberStyles.None, CultureInfo.InvariantCulture, out revisionId))
                                throw new InvalidDataException("Wikipedia revision ID is not an unsigned invariant integer.");
                            readNext = false;
                            continue;
                        case "timestamp": timestamp = reader.ReadElementContentAsString(); readNext = false; continue;
                        case "text":
                            rawText = ReadBoundedText(reader, out rawTextTooLarge);
                            continue;
                    }
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && inRevision && reader.Depth == revisionDepth && reader.LocalName == "revision")
            {
                inRevision = false;
            }
        }

        if (pageId == 0 || !requestedPageIds.Contains(pageId))
            return null;
        return new WikipediaPageRevision(pageId, title, namespaceId, hasRedirect, revisionId, timestamp, rawText, rawTextTooLarge);
    }

    private static string? ReadBoundedText(XmlReader reader, out bool tooLarge)
    {
        tooLarge = false;
        if (reader.IsEmptyElement)
            return string.Empty;
        var elementDepth = reader.Depth;
        var text = new StringBuilder(Math.Min(4096, MaximumRawTextCharacters));
        var chunk = new char[8192];
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == elementDepth && reader.LocalName == "text")
                return tooLarge ? null : text.ToString();
            if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
                continue;
            int read;
            while ((read = reader.ReadValueChunk(chunk, 0, chunk.Length)) != 0)
            {
                if (tooLarge)
                    continue;
                if (text.Length + read > MaximumRawTextCharacters)
                {
                    tooLarge = true;
                    text.Clear();
                    continue;
                }
                text.Append(chunk, 0, read);
            }
        }
        throw new XmlException("Wikipedia revision text is not closed before the end of its page stream.");
    }
}
