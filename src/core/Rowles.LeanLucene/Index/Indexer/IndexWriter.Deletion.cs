using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index.Indexer;

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
            _pendingDeletes.Add((query.Field, query.Term));
            _contentChangedSinceCommit = true;
        }
    }

    private void ApplyPendingDeletions(List<SegmentInfo> segments)
    {
        if (_pendingDeletes.Count == 0) return;

        var qualifiedTerms = new List<string>(_pendingDeletes.Count);
        foreach (var (field, term) in _pendingDeletes)
        {
            qualifiedTerms.Add(string.Concat(field, "\x00", term));
        }

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

                ReadPostingsAtOffsetInto(posInput, offset, postingsVersion, liveDocs, ref changed);
            }

            if (changed)
            {
                var newDelPath = basePath + $"_gen_{pendingGen}.del";
                LiveDocs.Serialise(newDelPath, liveDocs, _config.DurableCommits);
                seg.DelGeneration = pendingGen;
                seg.LiveDocCount = liveDocs.LiveCount;
                // Rewrite the .seg metadata file so the updated DelGeneration is
                // durable before the commit file that references this segment.
                seg.WriteTo(basePath + ".seg");
            }
        }

        _pendingDeletes.Clear();
    }

    /// <summary>
    /// Reads doc IDs from postings at the given offset using a memory-mapped IndexInput,
    /// and marks matching live docs as deleted.
    /// </summary>
    private static void ReadPostingsAtOffsetInto(IndexInput input, long offset, byte postingsVersion, LiveDocs liveDocs, ref bool changed)
    {
        using var pe = PostingsEnum.Create(input, offset, postingsVersion);
        while (pe.MoveNext())
        {
            int docId = pe.DocId;
            if (liveDocs.IsLive(docId))
            {
                liveDocs.Delete(docId);
                changed = true;
            }
        }
    }
}
