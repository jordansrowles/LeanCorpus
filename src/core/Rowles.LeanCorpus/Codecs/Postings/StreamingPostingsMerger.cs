using Rowles.LeanCorpus.Codecs.CodecKit;
using System.Buffers;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Postings;

/// <summary>
/// Streaming k-way merge of per-segment postings into a single merged segment.
/// Iterates terms in sorted order across all source segments without ever
/// materialising the full term-doc map in memory. Position data is streamed
/// doc-by-doc from source cursors directly to the merged output.
/// </summary>
internal static class StreamingPostingsMerger
{
    /// <summary>
    /// Result of a streaming merge: the sorted term list and the per-term
    /// .pos offsets needed to write the .dic file.
    /// </summary>
    internal readonly record struct Result(List<string> SortedTerms, Dictionary<string, long> Offsets);

    /// <summary>
    /// One source segment for the merge. The DocIdMap maps source local
    /// doc IDs to merged doc IDs; entries containing -1 are dropped (deleted).
    /// </summary>
    internal sealed class Source
    {
        internal required Func<string, IndexInput> OpenInput { get; init; }
        internal required int[] DocIdMap { get; init; }
    }

    internal static Result Merge(IReadOnlyList<Source> sources, string posOutputPath, string dicOutputPath)
    {
        var cursors = new List<Cursor>(sources.Count);
        var cursorNorms = new List<NormsData>(sources.Count);
        try
        {
            foreach (var s in sources)
            {
                var c = Cursor.Open(s);
                if (c.HasMore)
                {
                    cursors.Add(c);
                    cursorNorms.Add(NormsReader.Read(s.OpenInput(".nrm")));
                }
                else
                {
                    c.Dispose();
                }
            }

            // Write the current streaming header and sequential v4 term records.
            using var posOutput = new IndexOutput(posOutputPath, dropPageCache: true);
            var descriptor = CodecCatalog.Default.GetFile("leancorpus.postings.data");
            using var frame = CodecFileWriter.Begin(posOutput, descriptor);
            var bodyOutput = frame.Output;
            using var blockWriter = new BlockPostingsWriter(bodyOutput);

            var sortedTerms = new List<string>();
            var offsets = new Dictionary<string, long>(StringComparer.Ordinal);

            // Min-heap of cursor indices, ordered by current term and then source order.
            var heap = new PriorityQueue<int, (string Term, int Idx)>(cursors.Count, TermAndIndexComparer.Instance);
            for (int i = 0; i < cursors.Count; i++)
                heap.Enqueue(i, (cursors[i].CurrentTerm, i));

            var participants = new List<int>(cursors.Count);

            while (heap.Count > 0)
            {
                heap.TryPeek(out _, out var minPriority);
                string currentTerm = minPriority.Term;

                participants.Clear();
                while (heap.Count > 0 && heap.TryPeek(out _, out var topPriority) &&
                       string.CompareOrdinal(topPriority.Term, currentTerm) == 0)
                {
                    participants.Add(heap.Dequeue());
                }

                participants.Sort();

                bool hasFreqs = false;
                bool hasPositions = false;
                bool hasPayloads = false;
                foreach (int idx in participants)
                {
                    cursors[idx].PeekFlags(out bool f, out bool p, out bool pl);
                    hasFreqs |= f;
                    hasPositions |= p;
                    hasPayloads |= pl;
                }

                long bodyOffset = bodyOutput.Position;
                blockWriter.StartTerm();

                string fieldName = QualifiedTermHelpers.GetFieldName(currentTerm).ToString();

                var decodedSources = new List<DecodedPostingSource>(participants.Count);
                var orderedPostings = new List<(DecodedPostingSource Source, int PostingIndex)>();
                var postingQueue = new PriorityQueue<DecodedPostingSource, (int DocId, int SourceOrdinal)>();
                try
                {
                    foreach (int idx in participants)
                    {
                        Cursor cursor = cursors[idx];
                        cursor.DecodeCurrentPostings(out int[] oldIds, out int count, out int[] freqs);
                        var source = new DecodedPostingSource(cursor, idx, oldIds, freqs, count);
                        decodedSources.Add(source);
                        if (source.TryAdvance())
                            postingQueue.Enqueue(source, (source.CurrentDocId, source.SourceOrdinal));
                    }

                    while (postingQueue.TryDequeue(out DecodedPostingSource? source, out _))
                    {
                        int oldId = source.CurrentOldDocId;
                        var norms = cursorNorms[source.SourceOrdinal].Norms;
                        norms.TryGetValue(fieldName, out var fieldNormBytes);
                        byte norm = fieldNormBytes is not null && (uint)oldId < (uint)fieldNormBytes.Length
                            ? fieldNormBytes[oldId]
                            : (byte)0;
                        blockWriter.AddPosting(source.CurrentDocId, hasFreqs ? source.CurrentFrequency : 1, norm);
                        orderedPostings.Add((source, source.CurrentPostingIndex));

                        if (source.TryAdvance())
                            postingQueue.Enqueue(source, (source.CurrentDocId, source.SourceOrdinal));
                    }

                    var meta = blockWriter.FinishTerm();

                    // Each source position stream is sequential in source doc order. The posting
                    // merge order preserves that order within each source, so discard deleted
                    // posting positions and emit live positions in the same destination order.
                    if (hasPositions)
                    {
                        foreach ((DecodedPostingSource source, int postingIndex) in orderedPostings)
                        {
                            if (!source.Cursor.HasDecodedPositions)
                                continue;

                            while (source.NextPositionPostingIndex < postingIndex)
                            {
                                source.Cursor.SkipDocPositions(hasPayloads);
                                source.NextPositionPostingIndex++;
                            }

                            source.Cursor.WriteDocPositions(bodyOutput, hasPayloads);
                            source.NextPositionPostingIndex++;
                        }
                    }

                    long metadataOffset = bodyOutput.Position;
                    bodyOutput.WriteInt64(bodyOffset);
                    bodyOutput.WriteInt32(meta.DocFreq);
                    bodyOutput.WriteInt64(meta.SkipOffset);
                    bodyOutput.WriteBoolean(hasFreqs);
                    bodyOutput.WriteBoolean(hasPositions);
                    bodyOutput.WriteBoolean(hasPayloads);

                    if (meta.DocFreq > 0)
                    {
                        sortedTerms.Add(currentTerm);
                        offsets[currentTerm] = metadataOffset;
                    }
                }
                finally
                {
                    foreach (DecodedPostingSource source in decodedSources)
                        source.Dispose();
                }

                foreach (int idx in participants)
                {
                    cursors[idx].Advance();
                    if (cursors[idx].HasMore)
                        heap.Enqueue(idx, (cursors[idx].CurrentTerm, idx));
                }
            }

            frame.Complete();
            // Metadata offsets are absolute file positions, so no rekeying is needed.
            TermDictionaryWriter.Write(dicOutputPath, sortedTerms, offsets, dropPageCache: true);
            return new Result(sortedTerms, offsets);
        }
        finally
        {
            foreach (var c in cursors) c.Dispose();
        }
    }

    private sealed class TermAndIndexComparer : IComparer<(string Term, int Idx)>
    {
        internal static readonly TermAndIndexComparer Instance = new();
        public int Compare((string Term, int Idx) x, (string Term, int Idx) y)
        {
            int c = string.CompareOrdinal(x.Term, y.Term);
            return c != 0 ? c : x.Idx.CompareTo(y.Idx);
        }
    }

    private sealed class DecodedPostingSource : IDisposable
    {
        private readonly int[] _oldIds;
        private readonly int[] _frequencies;
        private readonly int _count;
        private int _nextIndex;

        internal Cursor Cursor { get; }
        internal int SourceOrdinal { get; }
        internal int CurrentPostingIndex { get; private set; }
        internal int CurrentOldDocId { get; private set; }
        internal int CurrentDocId { get; private set; }
        internal int CurrentFrequency { get; private set; }
        internal int NextPositionPostingIndex { get; set; }

        internal DecodedPostingSource(Cursor cursor, int sourceOrdinal, int[] oldIds, int[] frequencies, int count)
        {
            Cursor = cursor;
            SourceOrdinal = sourceOrdinal;
            _oldIds = oldIds;
            _frequencies = frequencies;
            _count = count;
        }

        internal bool TryAdvance()
        {
            int[] docIdMap = Cursor.Source.DocIdMap;
            while (_nextIndex < _count)
            {
                int postingIndex = _nextIndex++;
                int oldDocId = _oldIds[postingIndex];
                if ((uint)oldDocId >= (uint)docIdMap.Length)
                    continue;
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0)
                    continue;

                CurrentPostingIndex = postingIndex;
                CurrentOldDocId = oldDocId;
                CurrentDocId = newDocId;
                CurrentFrequency = _frequencies[postingIndex];
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            ArrayPool<int>.Shared.Return(_oldIds);
            ArrayPool<int>.Shared.Return(_frequencies);
        }
    }

    private sealed class Cursor : IDisposable
    {
        internal Source Source { get; }
        private readonly TermDictionaryReader _dic;
        private readonly IndexInput _pos;
        private readonly List<(string Term, long Offset)> _terms;
        private int _index;

        // State for streaming position reads
        private bool _decodedHasPositions;

        private Cursor(Source src, TermDictionaryReader dic, IndexInput pos, List<(string, long)> terms)
        {
            Source = src;
            _dic = dic;
            _pos = pos;
            _terms = terms;
            _index = 0;
        }

        internal static Cursor Open(Source src)
        {
            TermDictionaryReader? dic = null;
            IndexInput? pos = null;
            try
            {
                dic = TermDictionaryReader.Open(src.OpenInput(".dic"));
                pos = src.OpenInput(".pos");
                pos.Prefetch();
                PostingsFileHeader.ReadVersion(pos);
                var terms = dic.EnumerateAllTerms();
                return new Cursor(src, dic, pos, terms);
            }
            catch
            {
                pos?.Dispose();
                dic?.Dispose();
                throw;
            }
        }

        internal bool HasMore => _index < _terms.Count;
        internal string CurrentTerm => _terms[_index].Item1;
        internal long CurrentOffset => _terms[_index].Item2;
        internal bool HasDecodedPositions => _decodedHasPositions;

        internal void Advance() => _index++;

        internal void PeekFlags(out bool hasFreqs, out bool hasPositions, out bool hasPayloads)
        {
            PostingsEnum.ReadTermMetadata(_pos, CurrentOffset, out _, out _, out _,
                out hasFreqs, out hasPositions, out hasPayloads);
        }

        /// <summary>
        /// Decodes doc IDs and frequencies for the current term and positions the source
        /// stream at its first per-document position block when present.
        /// Callers must return <paramref name="oldIds"/> and <paramref name="freqs"/> to
        /// <see cref="ArrayPool{T}.Shared"/> when done.
        /// </summary>
        internal void DecodeCurrentPostings(out int[] oldIds, out int count, out int[] freqs)
        {
            PostingsEnum.ReadTermMetadata(_pos, CurrentOffset, out long docStart, out count,
                out long skipOffset, out bool hasFreqs, out bool hasPositions, out _);
            var enumv = BlockPostingsEnum.Create(_pos, docStart, skipOffset, count);
            oldIds = ArrayPool<int>.Shared.Rent(count);
            freqs = ArrayPool<int>.Shared.Rent(count);
            int idx = 0;
            while (enumv.NextDoc() != BlockPostingsEnum.NoMoreDocs)
            {
                oldIds[idx] = enumv.DocId;
                freqs[idx] = hasFreqs ? enumv.Freq : 1;
                idx++;
            }

            _decodedHasPositions = hasPositions;

            if (hasPositions)
            {
                // Position source stream at the start of position data (past skip data).
                _pos.Seek(skipOffset);
                int skipCount = _pos.ReadInt32();
                _pos.Seek(_pos.Position + (long)skipCount * 15);
            }
        }

        /// <summary>
        /// Streams one doc's position block from the source file to <paramref name="output"/>.
        /// Must be called exactly <c>count</c> times after <see cref="DecodeCurrentPostings"/>,
        /// in the same order as the doc IDs returned by that method.
        /// </summary>
        internal void WriteDocPositions(ISequentialIndexOutput output, bool hasPayloads)
            => CopyDocPositions(output, hasPayloads);

        /// <summary>Consumes one source document's position block without emitting it.</summary>
        internal void SkipDocPositions(bool hasPayloads)
            => CopyDocPositions(output: null, hasPayloads);

        private void CopyDocPositions(ISequentialIndexOutput? output, bool hasPayloads)
        {
            int posCount = _pos.ReadVarInt();
            output?.WriteVarInt(posCount);

            for (int i = 0; i < posCount; i++)
            {
                byte b;
                do
                {
                    b = _pos.ReadByte();
                    output?.WriteByte(b);
                } while ((b & 0x80) != 0);
            }

            if (hasPayloads)
            {
                for (int i = 0; i < posCount; i++)
                {
                    int payloadLen = _pos.ReadVarInt();
                    output?.WriteVarInt(payloadLen);
                    if (payloadLen > 0 && output is not null)
                        output.WriteBytes(_pos.ReadBytes(payloadLen));
                    else if (payloadLen > 0)
                        _pos.Seek(_pos.Position + payloadLen);
                }
            }
        }

        public void Dispose()
        {
            _pos.Dispose();
            _dic.Dispose();
        }
    }
}
