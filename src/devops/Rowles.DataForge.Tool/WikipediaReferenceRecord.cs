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
