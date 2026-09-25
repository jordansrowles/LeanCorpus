using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.DataForge.Workloads;

public sealed record WikipediaCandidateSelection(IReadOnlyList<WikipediaCandidate> Candidates, long IndexEntriesScanned, IReadOnlyList<long> UniqueOffsets);
