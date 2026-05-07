using System.Buffers;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Streaming k-way merge of per-segment postings into a single merged segment.
/// Iterates terms in sorted order across all source segments without ever
/// materialising the full term-doc map in memory. Per-term position data is
/// the only buffered state, bounded by the document frequency of one term.
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
    /// doc IDs to merged doc IDs; entries omitted from the map are dropped.
    /// </summary>
    internal sealed class Source
    {
        internal required string DicPath { get; init; }
        internal required string PosPath { get; init; }
        internal required IReadOnlyDictionary<int, int> DocIdMap { get; init; }
    }

    internal static Result Merge(IReadOnlyList<Source> sources, string posOutputPath, string dicOutputPath)
    {
        var cursors = new List<Cursor>(sources.Count);
        try
        {
            foreach (var s in sources)
            {
                var c = Cursor.Open(s);
                if (c.HasMore) cursors.Add(c);
                else c.Dispose();
            }

            using var posOutput = new IndexOutput(posOutputPath);
            CodecConstants.WriteHeader(posOutput, CodecConstants.PostingsVersion);
            using var blockWriter = new BlockPostingsWriter(posOutput);

            var sortedTerms = new List<string>();
            var offsets = new Dictionary<string, long>(StringComparer.Ordinal);

            // Min-heap of cursor indices, ordered by current term (then by source order
            // so the lowest-numbered segment wins ties — keeps doc IDs monotonic).
            var heap = new SortedSet<(string Term, int Idx)>(TermAndIndexComparer.Instance);
            for (int i = 0; i < cursors.Count; i++)
                heap.Add((cursors[i].CurrentTerm, i));

            var participants = new List<int>(cursors.Count);
            var positionStream = new List<(int DocId, int[] Positions)>();

            while (heap.Count > 0)
            {
                var min = heap.Min;
                string currentTerm = min.Term;

                participants.Clear();
                while (heap.Count > 0 && string.CompareOrdinal(heap.Min.Term, currentTerm) == 0)
                {
                    participants.Add(heap.Min.Idx);
                    heap.Remove(heap.Min);
                }

                participants.Sort();

                bool hasFreqs = false;
                bool hasPositions = false;
                foreach (int idx in participants)
                {
                    cursors[idx].PeekFlags(out bool f, out bool p);
                    hasFreqs |= f;
                    hasPositions |= p;
                }

                long headerPos = posOutput.Position;
                posOutput.WriteInt32(0);
                posOutput.WriteInt64(0L);
                posOutput.WriteBoolean(hasFreqs);
                posOutput.WriteBoolean(hasPositions);
                posOutput.WriteBoolean(false);

                blockWriter.StartTerm();
                positionStream.Clear();

                foreach (int idx in participants)
                {
                    var cursor = cursors[idx];
                    cursor.DecodeCurrent(out var oldIds, out int count, out var freqs, out var positions);
                    try
                    {
                        var docMap = cursor.Source.DocIdMap;
                        for (int j = 0; j < count; j++)
                        {
                            if (!docMap.TryGetValue(oldIds[j], out int newId)) continue;
                            blockWriter.AddPosting(newId, hasFreqs ? freqs[j] : 1);
                            if (hasPositions)
                            {
                                int[] p = positions is null ? Array.Empty<int>() : (positions[j] ?? Array.Empty<int>());
                                positionStream.Add((newId, p));
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<int>.Shared.Return(oldIds);
                        ArrayPool<int>.Shared.Return(freqs);
                    }
                }

                var meta = blockWriter.FinishTerm();

                if (hasPositions)
                {
                    positionStream.Sort(static (a, b) => a.DocId.CompareTo(b.DocId));
                    foreach (var (_, pos) in positionStream)
                    {
                        posOutput.WriteVarInt(pos.Length);
                        int prev = 0;
                        foreach (var p in pos)
                        {
                            posOutput.WriteVarInt(p - prev);
                            prev = p;
                        }
                    }
                }

                long endPos = posOutput.Position;
                posOutput.Seek(headerPos);
                posOutput.WriteInt32(meta.DocFreq);
                posOutput.WriteInt64(meta.SkipOffset);
                posOutput.Seek(endPos);

                if (meta.DocFreq > 0)
                {
                    sortedTerms.Add(currentTerm);
                    offsets[currentTerm] = headerPos;
                }

                foreach (int idx in participants)
                {
                    cursors[idx].Advance();
                    if (cursors[idx].HasMore)
                        heap.Add((cursors[idx].CurrentTerm, idx));
                }
            }

            blockWriter.Dispose();
            posOutput.Dispose();

            TermDictionaryWriter.Write(dicOutputPath, sortedTerms, offsets);
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
        private readonly byte _postingsVersion;
        private readonly List<(string Term, long Offset)> _terms;
        private int _index;

        private Cursor(Source src, TermDictionaryReader dic, IndexInput pos, byte version, List<(string, long)> terms)
        {
            Source = src;
            _dic = dic;
            _pos = pos;
            _postingsVersion = version;
            _terms = terms;
            _index = 0;
        }

        internal static Cursor Open(Source src)
        {
            var dic = TermDictionaryReader.Open(src.DicPath);
            var pos = new IndexInput(src.PosPath);
            byte ver = CodecConstants.ReadHeaderVersion(pos, CodecConstants.PostingsVersion, "postings (.pos)");
            var terms = dic.EnumerateAllTerms();
            return new Cursor(src, dic, pos, ver, terms);
        }

        internal bool HasMore => _index < _terms.Count;
        internal string CurrentTerm => _terms[_index].Item1;
        internal long CurrentOffset => _terms[_index].Item2;

        internal void Advance() => _index++;

        internal void PeekFlags(out bool hasFreqs, out bool hasPositions)
        {
            long savedPos = _pos.Position;
            try
            {
                _pos.Seek(CurrentOffset);
                if (_postingsVersion >= 3)
                {
                    _pos.ReadInt32();        // docFreq
                    _pos.ReadInt64();        // skipOffset
                    hasFreqs = _pos.ReadBoolean();
                    hasPositions = _pos.ReadBoolean();
                    _pos.ReadBoolean();      // hasPayloads
                }
                else
                {
                    int count = _pos.ReadInt32();
                    int skipCount = _pos.ReadInt32();
                    if (skipCount > 0)
                        _pos.Seek(_pos.Position + skipCount * 8L);
                    int prev = 0;
                    for (int j = 0; j < count; j++) prev += _pos.ReadVarInt();
                    hasFreqs = _pos.ReadBoolean();
                    if (hasFreqs) for (int j = 0; j < count; j++) _pos.ReadVarInt();
                    hasPositions = _pos.ReadBoolean();
                }
            }
            finally
            {
                _pos.Seek(savedPos);
            }
        }

        internal void DecodeCurrent(out int[] oldIds, out int count, out int[] freqs, out int[]?[]? positions)
        {
            _pos.Seek(CurrentOffset);
            if (_postingsVersion >= 3)
            {
                count = _pos.ReadInt32();
                long skipOffset = _pos.ReadInt64();
                bool hasFreqs = _pos.ReadBoolean();
                bool hasPositions = _pos.ReadBoolean();
                bool hasPayloads = _pos.ReadBoolean();

                long docStart = _pos.Position;
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

                if (hasPositions)
                {
                    _pos.Seek(skipOffset);
                    int skipCount = _pos.ReadInt32();
                    _pos.Seek(_pos.Position + (long)skipCount * 15);

                    var posArr = new int[count][];
                    for (int j = 0; j < count; j++)
                    {
                        int pc = _pos.ReadVarInt();
                        var arr = new int[pc];
                        int prevPos = 0;
                        for (int k = 0; k < pc; k++)
                        {
                            prevPos += _pos.ReadVarInt();
                            arr[k] = prevPos;
                            if (hasPayloads)
                            {
                                int payloadLen = _pos.ReadVarInt();
                                if (payloadLen > 0) _pos.Seek(_pos.Position + payloadLen);
                            }
                        }
                        posArr[j] = arr;
                    }
                    positions = posArr;
                }
                else
                {
                    positions = null;
                }
            }
            else
            {
                count = _pos.ReadInt32();
                int skipCount = _pos.ReadInt32();
                if (skipCount > 0)
                    _pos.Seek(_pos.Position + skipCount * 8L);

                oldIds = ArrayPool<int>.Shared.Rent(count);
                freqs = ArrayPool<int>.Shared.Rent(count);

                int prev = 0;
                for (int j = 0; j < count; j++)
                {
                    prev += _pos.ReadVarInt();
                    oldIds[j] = prev;
                }

                bool hasFreqs = _pos.ReadBoolean();
                if (hasFreqs)
                    for (int j = 0; j < count; j++) freqs[j] = _pos.ReadVarInt();
                else
                    Array.Fill(freqs, 1, 0, count);

                bool hasPositions = _pos.ReadBoolean();
                bool hasPayloads = _postingsVersion >= 2 && hasPositions && _pos.ReadBoolean();

                if (hasPositions)
                {
                    var posArr = new int[count][];
                    for (int j = 0; j < count; j++)
                    {
                        int pc = _pos.ReadVarInt();
                        var arr = new int[pc];
                        int prevPos = 0;
                        for (int k = 0; k < pc; k++)
                        {
                            prevPos += _pos.ReadVarInt();
                            arr[k] = prevPos;
                            if (hasPayloads)
                            {
                                int payloadLen = _pos.ReadVarInt();
                                if (payloadLen > 0) _pos.Seek(_pos.Position + payloadLen);
                            }
                        }
                        posArr[j] = arr;
                    }
                    positions = posArr;
                }
                else
                {
                    positions = null;
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
