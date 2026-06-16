using System.Diagnostics;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Queues a term-based deletion. Documents matching <paramref name="query"/> are deleted
    /// on the next <see cref="Commit"/> call.
    /// </summary>
    /// <param name="query">The term query identifying documents to delete.</param>
    public void DeleteDocuments(TermQuery query)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteQueue);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            _pendingDeletes.Add((query.Field, query.Term, isSoftDelete: false));
            _contentChangedSinceCommit = true;
        }
    }

    private void ApplyPendingDeletions(List<SegmentInfo> segments)
    {
        using var activity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.DeleteApply);
        if (_pendingDeletes.Count == 0) return;

        // Group hard deletes by field for single-pass FST prefix scans per segment.
        // This avoids O(N_terms) individual FST walks — instead we do one prefix scan
        // per unique field, matching against a HashSet of bare terms.
        var hardDeleteTermsByField = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var softDeleteTermsByField = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        long softDeleteTimestamp = 0;

        foreach (var (field, term, isSoftDelete) in _pendingDeletes)
        {
            var dict = isSoftDelete ? softDeleteTermsByField : hardDeleteTermsByField;
            if (!dict.TryGetValue(field, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                dict[field] = set;
            }
            set.Add(term);
        }

        if (softDeleteTermsByField.Count > 0)
            softDeleteTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // The pending commit will be at _commitGeneration + 1; generation-versioned del files
        // are named for the generation they become durable in, so they never overwrite files
        // that older commits still reference.
        int pendingGen = _commitGeneration + 1;

        foreach (var seg in segments)
        {
            var basePath = Path.Combine(_directory.DirectoryPath, seg.SegmentId);
            var dicPath = basePath + ".dic";
            var posPath = basePath + ".pos";

            if (!File.Exists(dicPath) || !File.Exists(posPath))
                continue;

            using var dicReader = TermDictionaryReader.Open(dicPath);

            // Resolve the existing del file: prefer the generation-versioned path, fall back
            // to the legacy unversioned path so old on-disk indexes continue to load.
            string existingDelPath = seg.DelGeneration.HasValue
                ? basePath + $"_gen_{seg.DelGeneration.Value}.del"
                : basePath + ".del";

            var liveDocs = File.Exists(existingDelPath)
                ? LiveDocs.Deserialise(existingDelPath, seg.DocCount)
                : new LiveDocs(seg.DocCount);

            bool changed = false;
            using var posInput = new IndexInput(posPath);
            byte postingsVersion = PostingsEnum.ValidateFileHeader(posInput);

            // Single FST prefix scan per unique field instead of per-term FST walks.
            ApplyDeletesByField(dicReader, posInput, postingsVersion, liveDocs,
                hardDeleteTermsByField, softDelete: false, 0, ref changed);
            ApplyDeletesByField(dicReader, posInput, postingsVersion, liveDocs,
                softDeleteTermsByField, softDelete: true, softDeleteTimestamp, ref changed);

            if (changed)
            {
                var newDelPath = basePath + $"_gen_{pendingGen}.del";
                LiveDocs.Serialise(newDelPath, liveDocs, _config.DurableCommits);
                seg.DelGeneration = pendingGen;
                seg.LiveDocCount = liveDocs.LiveCount;
                seg.EarliestSoftDeleteTimestamp = liveDocs.EarliestSoftDeleteTimestamp;
                // Rewrite the .seg metadata file so the updated DelGeneration is
                // durable before the commit file that references this segment.
                seg.WriteTo(basePath + ".seg");
            }
        }

        _pendingDeletes.Clear();
    }

    /// <summary>
    /// Reads doc IDs from postings at the given offset using a memory-mapped IndexInput,
    /// and marks matching live docs as deleted (hard-delete) or soft-deleted.
    /// </summary>
    private static void ReadPostingsAtOffsetInto(
        IndexInput input, long offset, byte postingsVersion, LiveDocs liveDocs,
        ref bool changed, bool softDelete = false, long softDeleteTimestamp = 0)
    {
        using var pe = PostingsEnum.Create(input, offset);
        while (pe.MoveNext())
        {
            int docId = pe.DocId;
            if (liveDocs.IsLive(docId))
            {
                if (softDelete)
                    liveDocs.SoftDelete(docId, softDeleteTimestamp);
                else
                    liveDocs.Delete(docId);
                changed = true;
            }
        }
    }

    /// <summary>
    /// Applies pending deletions for one field's delete set via per-term FST lookups
    /// with stack-allocated qualified terms. Avoids the full prefix scan and its
    /// associated string allocations when a field has many more unique terms than deletes.
    /// </summary>
    private static void ApplyDeletesByField(
        TermDictionaryReader dicReader, IndexInput posInput, byte postingsVersion,
        LiveDocs liveDocs, Dictionary<string, HashSet<string>> termsByField,
        bool softDelete, long softDeleteTimestamp, ref bool changed)
    {
        // Reusable stack buffer for qualified-term construction.
        // Qualified terms are "field\0term" — in practice always ≤ 256 chars.
        Span<char> stackBuf = stackalloc char[256];

        foreach (var (field, terms) in termsByField)
        {
            foreach (var term in terms)
            {
                int fieldLen = field.Length;
                int termLen = term.Length;
                int totalLen = fieldLen + 1 + termLen;
                Span<char> qualified = totalLen <= 256
                    ? stackBuf.Slice(0, totalLen)
                    : new char[totalLen];
                field.AsSpan().CopyTo(qualified);
                qualified[fieldLen] = '\0';
                term.AsSpan().CopyTo(qualified.Slice(fieldLen + 1));

                if (!dicReader.TryGetPostingsOffset(qualified, out long offset))
                    continue;

                ReadPostingsAtOffsetInto(posInput, offset, postingsVersion,
                    liveDocs, ref changed, softDelete, softDeleteTimestamp);
            }
        }
    }
}
