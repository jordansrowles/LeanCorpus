using System.Buffers;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;

namespace Rowles.LeanCorpus.Codecs.Postings;

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

            string tmpPosBody = posOutputPath + ".body.tmp";
            try
            {
                using (var posOutput = new IndexOutput(tmpPosBody, durable: true))
                using (var blockWriter = new BlockPostingsWriter(posOutput))
                {
                    var sortedTerms = new List<string>();
                    var offsets = new Dictionary<string, long>(StringComparer.Ordinal);

                    // Min-heap of cursor indices, ordered by current term (then by source order
                    // so the lowest-numbered segment wins ties — keeps doc IDs monotonic).
                    var heap = new PriorityQueue<int, (string Term, int Idx)>(cursors.Count, TermAndIndexComparer.Instance);
                    for (int i = 0; i < cursors.Count; i++)
                        heap.Enqueue(i, (cursors[i].CurrentTerm, i));

                    var participants = new List<int>(cursors.Count);
                    var positionStream = new List<(int DocId, int[] Positions, byte[]?[]? Payloads)>();

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

                        long headerPos = posOutput.Position;
                        posOutput.WriteInt32(0);
                        posOutput.WriteInt64(0L);
                        posOutput.WriteBoolean(hasFreqs);
                        posOutput.WriteBoolean(hasPositions);
                        posOutput.WriteBoolean(hasPayloads);

                        blockWriter.StartTerm();
                        positionStream.Clear();

                        string fieldName = QualifiedTermHelpers.GetFieldName(currentTerm).ToString();

                        foreach (int idx in participants)
                        {
                            var cursor = cursors[idx];
                            cursor.DecodeCurrent(out var oldIds, out int count, out var freqs, out var positions, out var payloads);
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
                                    if (hasPositions)
                                    {
                                        int[] p = positions is null ? Array.Empty<int>() : (positions[j] ?? Array.Empty<int>());
                                        byte[]?[]? pl = payloads is null ? null : payloads[j];
                                        positionStream.Add((newId, p, pl));
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
                            foreach (var (_, pos, payloadsForDoc) in positionStream)
                            {
                                posOutput.WriteVarInt(pos.Length);
                                int prev = 0;
                                for (int i = 0; i < pos.Length; i++)
                                {
                                    int p = pos[i];
                                    posOutput.WriteVarInt(p - prev);
                                    prev = p;
                                    if (hasPayloads)
                                    {
                                        var payload = payloadsForDoc is not null && i < payloadsForDoc.Length
                                            ? payloadsForDoc[i]
                                            : null;
                                        posOutput.WriteVarInt(payload?.Length ?? 0);
                                        if (payload is { Length: > 0 })
                                            posOutput.WriteBytes(payload);
                                    }
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
                                heap.Enqueue(idx, (cursors[idx].CurrentTerm, idx));
                        }
                    }

                    blockWriter.Dispose();
                    posOutput.Dispose();

                    byte[] body = File.ReadAllBytes(tmpPosBody);
                    int envelopeOffset = 1 + VarIntSize(body.Length);

                    // Patch skipOffset values inside body to account for CodecKit envelope
                    foreach (var kv in offsets)
                    {
                        int termBodyOffset = (int)kv.Value;
                        long oldSkip = BitConverter.ToInt64(body, termBodyOffset + 4);
                        byte[] bumped = BitConverter.GetBytes(oldSkip + envelopeOffset);
                        bumped.CopyTo(body, termBodyOffset + 4);
                    }

                    using (var finalOutput = new IndexOutput(posOutputPath, durable: true, dropPageCache: true))
                    {
                        finalOutput.WriteByte(Codecs.CodecConstants.PostingsVersion);
                        finalOutput.WriteVarInt(body.Length);
                        finalOutput.WriteBytes(body);
                    }

                    // Re-base offsets to account for CodecKit envelope
                    var rekeyedOffsets = new Dictionary<string, long>(offsets.Count, StringComparer.Ordinal);
                    foreach (var kv in offsets)
                        rekeyedOffsets[kv.Key] = kv.Value + envelopeOffset;

                    TermDictionaryWriter.Write(dicOutputPath, sortedTerms, rekeyedOffsets, dropPageCache: true);
                    return new Result(sortedTerms, rekeyedOffsets);
                }
            }
            finally
            {
                TryDeleteTemporaryFile(tmpPosBody);
            }

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
            CodecFileHeader.ReadVersion(pos, CodecFormats.Postings);
            var terms = dic.EnumerateAllTerms();
            return new Cursor(src, dic, pos, terms);
        }

        internal bool HasMore => _index < _terms.Count;
        internal string CurrentTerm => _terms[_index].Item1;
        internal long CurrentOffset => _terms[_index].Item2;

        internal void Advance() => _index++;

        internal void PeekFlags(out bool hasFreqs, out bool hasPositions, out bool hasPayloads)
        {
            long savedPos = _pos.Position;
            try
            {
                _pos.Seek(CurrentOffset);
                _pos.ReadInt32();        // docFreq
                _pos.ReadInt64();        // skipOffset
                hasFreqs = _pos.ReadBoolean();
                hasPositions = _pos.ReadBoolean();
                hasPayloads = _pos.ReadBoolean();
            }
            finally
            {
                _pos.Seek(savedPos);
            }
        }

        internal void DecodeCurrent(out int[] oldIds, out int count, out int[] freqs, out int[]?[]? positions, out byte[]?[][]? payloads)
        {
            _pos.Seek(CurrentOffset);
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
                byte[]?[][]? payloadArr = hasPayloads ? new byte[]?[count][] : null;
                for (int j = 0; j < count; j++)
                {
                    int pc = _pos.ReadVarInt();
                    var arr = new int[pc];
                    byte[]?[]? docPayloads = hasPayloads ? new byte[]?[pc] : null;
                    int prevPos = 0;
                    for (int k = 0; k < pc; k++)
                    {
                        prevPos += _pos.ReadVarInt();
                        arr[k] = prevPos;
                        if (hasPayloads)
                        {
                            int payloadLen = _pos.ReadVarInt();
                            if (payloadLen > 0)
                                docPayloads![k] = _pos.ReadBytes(payloadLen);
                        }
                    }
                    posArr[j] = arr;
                    if (payloadArr is not null)
                        payloadArr[j] = docPayloads!;
                }
                positions = posArr;
                payloads = payloadArr;
            }
            else
            {
                positions = null;
                payloads = null;
            }
        }

        public void Dispose()
        {
            _pos.Dispose();
            _dic.Dispose();
        }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try { File.Delete(path); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "temp postings file delete"); }
    }

    private static int VarIntSize(long value)
    {
        int size = 0;
        do { size++; value >>= 7; } while (value != 0);
        return size;
    }
}
