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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_writeLock)
        {
            _pendingDeletes.Add((query.Field, query.Term, isSoftDelete: false));
            _contentChangedSinceCommit = true;
        }
    }

    private readonly List<string> _deleteQualifiedTermsBuffer = new(64);

    private void ApplyPendingDeletions(List<SegmentInfo> segments)
    {
        if (_pendingDeletes.Count == 0) return;

        _deleteQualifiedTermsBuffer.Clear();
        if (_deleteQualifiedTermsBuffer.Capacity < _pendingDeletes.Count)
            _deleteQualifiedTermsBuffer.Capacity = _pendingDeletes.Count;
        var softDeleteQualifiedTerms = new List<string>(_pendingDeletes.Count);
        foreach (var (field, term, isSoftDelete) in _pendingDeletes)
        {
            var qt = string.Concat(field, "\x00", term);
            if (isSoftDelete)
                softDeleteQualifiedTerms.Add(qt);
            else
                _deleteQualifiedTermsBuffer.Add(qt);
        }
        var qualifiedTerms = _deleteQualifiedTermsBuffer;

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

            foreach (var qualifiedTerm in qualifiedTerms)
            {
                if (!dicReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
                    continue;

                ReadPostingsAtOffsetInto(posInput, offset, postingsVersion, liveDocs, ref changed, softDelete: false);
            }

            if (softDeleteQualifiedTerms.Count > 0)
            {
                long nowMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var qualifiedTerm in softDeleteQualifiedTerms)
                {
                    if (!dicReader.TryGetPostingsOffset(qualifiedTerm, out long offset))
                        continue;

                    ReadPostingsAtOffsetInto(posInput, offset, postingsVersion, liveDocs, ref changed, softDelete: true, nowMillis);
                }
            }

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
        using var pe = PostingsEnum.Create(input, offset, postingsVersion);
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
}
