using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.Postings;

/// <summary>
/// Writes postings in packed block format (v3).
/// Doc IDs and frequencies are written in 128-int delta-encoded packed blocks,
/// with VarInt encoding for the tail (remaining &lt; 128 values).
/// Skip data is emitted after every block for efficient <c>Advance()</c>.
/// </summary>
internal sealed class BlockPostingsWriter : IDisposable
{
    private const int BlockSize = PackedIntCodec.BlockSize; // 128

    private readonly IndexOutput _docOut;
    private readonly IndexOutput? _posOut;

    // Buffered values for the current block
    private readonly int[] _docBuffer = new int[BlockSize];
    private readonly int[] _freqBuffer = new int[BlockSize];
    private readonly int[] _posBuffer = new int[BlockSize];
    private int _bufferCount;
    private int _posBufCount;

    // Skip data: one entry per block
    private readonly List<SkipEntry> _skipEntries = [];

    // State
    private int _lastDocId;
    private int _totalDocCount;
    private long _docStartOffset;

    // Per-doc norm values for impact metadata (optional)
    private readonly byte[] _normBuffer = new byte[BlockSize];

    // Scratch space for bit-packing
    private readonly byte[] _packScratch = new byte[1 + 32 * 16]; // max output of Pack

    public BlockPostingsWriter(IndexOutput docOut, IndexOutput? posOut = null)
    {
        _docOut = docOut;
        _posOut = posOut;
    }

    /// <summary>
    /// Begins a new term's posting list. Records the file offset for the skip index.
    /// </summary>
    public void StartTerm()
    {
        _bufferCount = 0;
        _posBufCount = 0;
        _lastDocId = 0;
        _totalDocCount = 0;
        _skipEntries.Clear();
        _docStartOffset = _docOut.Position;
    }

    /// <summary>
    /// Adds a posting (doc ID + frequency). Call once per doc per term.
    /// </summary>
    public void AddPosting(int docId, int freq, byte norm = 0)
    {
        _docBuffer[_bufferCount] = docId;
        _freqBuffer[_bufferCount] = freq;
        _normBuffer[_bufferCount] = norm;
        _bufferCount++;
        _totalDocCount++;

        if (_bufferCount == BlockSize)
            FlushBlock();
    }

    /// <summary>
    /// Adds a position for the current document. Must be called freq times
    /// between consecutive AddPosting calls.
    /// </summary>
    public void AddPosition(int position)
    {
        if (_posOut == null) return;

        if (_posBufCount == BlockSize)
            FlushPosBlock();

        _posBuffer[_posBufCount++] = position;
    }

    /// <summary>
    /// Finishes the current term's posting list. Writes any remaining tail
    /// (less than a full block) as VarInt-encoded values, then the skip index.
    /// Returns a <see cref="TermPostingMetadata"/> with the byte offsets needed
    /// for the term dictionary entry.
    /// </summary>
    public TermPostingMetadata FinishTerm()
    {
        // Flush remaining positions
        if (_posOut != null && _posBufCount > 0)
            FlushPosTail();

        long docBytesStart = _docStartOffset;
        int skipCount = _skipEntries.Count;

        // Write tail (remaining docs < 128)
        if (_bufferCount > 0)
            WriteTail();

        // Write skip data at the end
        long skipOffset = _docOut.Position;
        _docOut.WriteInt32(skipCount);
        foreach (var skip in _skipEntries)
        {
            _docOut.WriteInt32(skip.LastDocId);
            _docOut.WriteInt64(skip.DocByteOffset);
            if (_posOut != null)
                _docOut.WriteInt64(skip.PosFileOffset);
            // Impact metadata (v3+)
            _docOut.WriteByte((byte)(skip.MaxFreqInBlock & 0xFF));
            _docOut.WriteByte((byte)(skip.MaxFreqInBlock >> 8));
            _docOut.WriteByte(skip.MaxNormInBlock);
        }

        return new TermPostingMetadata
        {
            DocFreq = _totalDocCount,
            DocStartOffset = docBytesStart,
            SkipOffset = skipOffset,
            SingletonDocId = _totalDocCount == 1 ? _docBuffer[0] : -1
        };
    }

    private void FlushBlock()
    {
        // Compute impact metadata
        ushort maxFreq = 0;
        byte maxNorm = 0;
        for (int i = 0; i < BlockSize; i++)
        {
            ushort f = (ushort)Math.Min(_freqBuffer[i], ushort.MaxValue);
            if (f > maxFreq) maxFreq = f;
            if (_normBuffer[i] > maxNorm) maxNorm = _normBuffer[i];
        }

        // Record skip entry before writing block
        _skipEntries.Add(new SkipEntry
        {
            LastDocId = _docBuffer[BlockSize - 1],
            DocByteOffset = _docOut.Position - _docStartOffset,
            PosFileOffset = _posOut?.Position ?? 0,
            MaxFreqInBlock = maxFreq,
            MaxNormInBlock = maxNorm
        });

        // Delta-encode doc IDs and pack
        var (numBits, bytesWritten) = PackedIntCodec.PackDelta(
            _docBuffer.AsSpan(0, BlockSize), _lastDocId, _packScratch);
        // PackDelta writes [numBits:1][data] into scratch; write numBits explicitly
        // then only the packed data (skip the embedded numBits byte in scratch)
        _docOut.WriteByte((byte)numBits);
        if (bytesWritten > 1)
            _docOut.WriteBytes(_packScratch.AsSpan(1, bytesWritten - 1));

        // Pack frequencies (minus-one encoded: freq ≥ 1 → store freq-1)
        Span<int> freqMinusOne = stackalloc int[BlockSize];
        for (int i = 0; i < BlockSize; i++)
            freqMinusOne[i] = _freqBuffer[i] - 1;

        int freqBytes = PackedIntCodec.Pack(freqMinusOne, _packScratch);
        _docOut.WriteBytes(_packScratch.AsSpan(0, freqBytes));

        _lastDocId = _docBuffer[BlockSize - 1];
        _bufferCount = 0;
    }

    private void WriteTail()
    {
        // Write tail count
        _docOut.WriteVarInt(_bufferCount);

        // Delta-encode and VarInt-encode remaining doc IDs
        int prev = _lastDocId;
        for (int i = 0; i < _bufferCount; i++)
        {
            _docOut.WriteVarInt(_docBuffer[i] - prev);
            prev = _docBuffer[i];
        }

        // VarInt-encode remaining frequencies (minus-one)
        for (int i = 0; i < _bufferCount; i++)
            _docOut.WriteVarInt(_freqBuffer[i] - 1);
    }

    private void FlushPosBlock()
    {
        if (_posOut == null || _posBufCount == 0) return;

        // Delta-encode positions within the block
        var (numBits, bytesWritten) = PackedIntCodec.PackDelta(
            _posBuffer.AsSpan(0, _posBufCount), 0, _packScratch);
        _posOut.WriteByte((byte)numBits);
        _posOut.WriteBytes(_packScratch.AsSpan(0, bytesWritten));
        _posBufCount = 0;
    }

    private void FlushPosTail()
    {
        if (_posOut == null || _posBufCount == 0) return;

        _posOut.WriteVarInt(_posBufCount);
        int prev = 0;
        for (int i = 0; i < _posBufCount; i++)
        {
            _posOut.WriteVarInt(_posBuffer[i] - prev);
            prev = _posBuffer[i];
        }
        _posBufCount = 0;
    }

    public void Dispose()
    {
        // Writer does not own the output streams
    }
}

/// <summary>
/// Metadata returned by <see cref="BlockPostingsWriter.FinishTerm"/>
/// for storage in the term dictionary.
/// </summary>
internal struct TermPostingMetadata
{
    public int DocFreq;
    public long DocStartOffset;
    public long SkipOffset;
    /// <summary>If docFreq == 1, the single doc ID (avoid posting file seek). Otherwise -1.</summary>
    public int SingletonDocId;
}

internal struct SkipEntry
{
    public int LastDocId;
    public long DocByteOffset;
    public long PosFileOffset;
    /// <summary>Highest term frequency in the block (for WAND scoring). 0 if not available.</summary>
    public ushort MaxFreqInBlock;
    /// <summary>Highest quantised norm in the block (for WAND scoring). 0 if not available.</summary>
    public byte MaxNormInBlock;
}
