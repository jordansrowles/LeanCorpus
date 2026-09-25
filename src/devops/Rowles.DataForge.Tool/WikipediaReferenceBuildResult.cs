using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tool;

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

