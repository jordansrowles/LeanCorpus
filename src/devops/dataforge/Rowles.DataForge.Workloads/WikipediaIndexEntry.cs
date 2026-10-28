using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rowles.DataForge.Workloads;

public sealed record WikipediaIndexEntry(long Offset, ulong PageId, string Title);
