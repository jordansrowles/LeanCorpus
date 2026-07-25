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
        internal required string DicPath { get; init; }
        internal required string PosPath { get; init; }
        internal required string NormsPath { get; init; }
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
                    cursorNorms.Add(NormsReader.Read(s.NormsPath));
                }
                else
                {
                    c.Dispose();
                }
            }

            // Write the current streaming header and sequential v4 term records.
            using var posOutput = new IndexOutput(posOutputPath, dropPageCache: true);
            using var scope = CodecFileHeader.BeginStreamingWrite(posOutput, CodecConstants.PostingsVersion);
            using var blockWriter = new BlockPostingsWriter(posOutput);

            var sortedTerms = new List<string>();
            var offsets = new Dictionary<string, long>(StringComparer.Ordinal);

            // Min-heap of cursor indices, ordered by current term (then by source order
            // so the lowest-numbered segment wins ties — keeps doc IDs monotonic).
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

                long bodyOffset = posOutput.Position;
                blockWriter.StartTerm();

                string fieldName = QualifiedTermHelpers.GetFieldName(currentTerm).ToString();

                foreach (int idx in participants)
                {
                    var cursor = cursors[idx];
                    cursor.DecodeCurrentPostings(out var oldIds, out int count, out var freqs,
                        out bool curHasPositions, out bool curHasPayloads);
                    try
                    {
                        var docMap = cursor.Source.DocIdMap;
                        var norms = cursorNorms[idx].Norms;
                        norms.TryGetValue(fieldName, out var fieldNormBytes);
                        for (int j = 0; j < count; j++)
                        {
                            int oldId = oldIds[j];
                            if ((uint)oldId >= (uint)docMap.Length) continue;
                            int newId = docMap[oldId];
                            if (newId < 0) continue;
                            byte norm = fieldNormBytes is not null && (uint)oldId < (uint)fieldNormBytes.Length
                                ? fieldNormBytes[oldId]
                                : (byte)0;
                            blockWriter.AddPosting(newId, hasFreqs ? freqs[j] : 1, norm);
                        }
                    }
                    finally
                    {
                        ArrayPool<int>.Shared.Return(oldIds);
                        ArrayPool<int>.Shared.Return(freqs);
                    }
                }

                var meta = blockWriter.FinishTerm();

                // Stream position data doc-by-doc from source cursors.
                // Docs are already in merged doc-ID order (participants sorted by segment index,
                // segment 0 remapped IDs < segment 1 < ...), so no sort is needed.
                if (hasPositions)
                {
                    foreach (int idx in participants)
                    {
                        var cursor = cursors[idx];
                        cursor.WritePositionsForTerm(posOutput, hasPayloads);
                    }
                }

                long metadataOffset = posOutput.Position;
                posOutput.WriteInt64(bodyOffset);
                posOutput.WriteInt32(meta.DocFreq);
                posOutput.WriteInt64(meta.SkipOffset);
                posOutput.WriteBoolean(hasFreqs);
                posOutput.WriteBoolean(hasPositions);
                posOutput.WriteBoolean(hasPayloads);

                if (meta.DocFreq > 0)
                {
                    sortedTerms.Add(currentTerm);
                    offsets[currentTerm] = metadataOffset;
                }

                foreach (int idx in participants)
                {
                    cursors[idx].Advance();
                    if (cursors[idx].HasMore)
                        heap.Enqueue(idx, (cursors[idx].CurrentTerm, idx));
                }
            }

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

    private sealed class Cursor : IDisposable
    {
        internal Source Source { get; }
        private readonly TermDictionaryReader _dic;
        private readonly IndexInput _pos;
        private readonly List<(string Term, long Offset)> _terms;
        private int _index;

        // State for streaming position reads
        private int _decodedDocCount;
        private bool _decodedHasPositions;
        private bool _decodedHasPayloads;
        private long _decodedPosDataStart;

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
            var dic = TermDictionaryReader.Open(src.DicPath);
            var pos = new IndexInput(src.PosPath);
            pos.Prefetch();
            PostingsFileHeader.ReadVersion(pos);
            var terms = dic.EnumerateAllTerms();
            return new Cursor(src, dic, pos, terms);
        }

        internal bool HasMore => _index < _terms.Count;
        internal string CurrentTerm => _terms[_index].Item1;
        internal long CurrentOffset => _terms[_index].Item2;

        internal void Advance() => _index++;

        internal void PeekFlags(out bool hasFreqs, out bool hasPositions, out bool hasPayloads)
        {
            PostingsEnum.ReadTermMetadata(_pos, CurrentOffset, out _, out _, out _,
                out hasFreqs, out hasPositions, out hasPayloads);
        }

        /// <summary>
        /// Decodes doc IDs and frequencies for the current term. Sets up internal state
        /// so that <see cref="WritePositionsForTerm"/> can stream position data doc-by-doc.
        /// Callers must return <paramref name="oldIds"/> and <paramref name="freqs"/> to
        /// <see cref="ArrayPool{T}.Shared"/> when done.
        /// </summary>
        internal void DecodeCurrentPostings(out int[] oldIds, out int count, out int[] freqs,
            out bool hasPositions, out bool hasPayloads)
        {
            PostingsEnum.ReadTermMetadata(_pos, CurrentOffset, out long docStart, out count,
                out long skipOffset, out bool hasFreqs, out hasPositions, out hasPayloads);
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

            _decodedDocCount = count;
            _decodedHasPositions = hasPositions;
            _decodedHasPayloads = hasPayloads;

            if (hasPositions)
            {
                // Position source stream at the start of position data (past skip data).
                _pos.Seek(skipOffset);
                int skipCount = _pos.ReadInt32();
                _pos.Seek(_pos.Position + (long)skipCount * 15);
                _decodedPosDataStart = _pos.Position;
            }
        }

        /// <summary>
        /// Streams one doc's position block from the source file to <paramref name="output"/>.
        /// Must be called exactly <c>count</c> times after <see cref="DecodeCurrentPostings"/>,
        /// in the same order as the doc IDs returned by that method.
        /// </summary>
        internal void WriteDocPositions(IndexOutput output, bool hasPayloads)
        {
            int posCount = _pos.ReadVarInt();
            output.WriteVarInt(posCount);

            // Copy position deltas (byte-identical in source and merged output).
            for (int i = 0; i < posCount; i++)
            {
                byte b;
                do
                {
                    b = _pos.ReadByte();
                    output.WriteByte(b);
                } while ((b & 0x80) != 0);
            }

            if (hasPayloads)
            {
                for (int i = 0; i < posCount; i++)
                {
                    int payloadLen = _pos.ReadVarInt();
                    output.WriteVarInt(payloadLen);
                    if (payloadLen > 0)
                        output.WriteBytes(_pos.ReadBytes(payloadLen));
                }
            }
        }

        /// <summary>
        /// Writes all position data for the current term by streaming one doc at a time
        /// through <see cref="WriteDocPositions"/>.
        /// </summary>
        internal void WritePositionsForTerm(IndexOutput output, bool hasPayloads)
        {
            if (!_decodedHasPositions) return;

            for (int i = 0; i < _decodedDocCount; i++)
                WriteDocPositions(output, hasPayloads);
        }

        public void Dispose()
        {
            _pos.Dispose();
            _dic.Dispose();
        }
    }
}
