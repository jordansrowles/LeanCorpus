using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer.Postings;

/// <summary>
/// A postings-specific sliced byte arena. Addresses are one-based so zero can
/// represent an absent stream in the compact term state.
/// </summary>
internal sealed class PostingsByteArena : IDisposable
{
    internal const int BlockSize = 32 * 1024;
    private const int SliceHeaderSize = sizeof(int);
    private const int FirstSliceSize = 32;
    private const int LargestSliceSize = 4096;
    private const int LargestSliceLevel = 7;

    private readonly ArrayPool<byte> _bytePool;
    private readonly List<byte[]> _blocks = [];
    private int _currentBlockIndex = -1;
    private int _currentBlockOffset;
    private long _rentedBytes;
    private long _allocatedBytes;
    private int _allocationVersion;
    private int _disposed;

    internal PostingsByteArena()
        : this(ArrayPool<byte>.Shared)
    {
    }

    internal PostingsByteArena(ArrayPool<byte> bytePool)
    {
        ArgumentNullException.ThrowIfNull(bytePool);
        _bytePool = bytePool;
    }

    /// <summary>Gets the published owned rented-array and block-reference capacity estimate.</summary>
    internal long AllocatedBytes => Volatile.Read(ref _allocatedBytes);

    internal int AllocationVersion => Volatile.Read(ref _allocationVersion);

    internal int BlockCount => _blocks.Count;

    /// <summary>A primitive cursor for appending one stream.</summary>
    internal struct StreamCursor
    {
        internal int StartAddress;
        internal int CurrentSliceHeaderAddress;
        internal int CurrentPositionAddress;
        internal int CurrentDataEndAddress;
        internal int Level;
        internal int WrittenLength;
    }

    internal StreamCursor StartStream()
    {
        ThrowIfDisposed();
        var cursor = new StreamCursor { Level = 0 };
        AllocateSlice(ref cursor, 0);
        return cursor;
    }

    internal void WriteByte(ref StreamCursor cursor, byte value)
    {
        EnsureLogicalWriteLength(cursor, 1);
        EnsureWritable(ref cursor);
        GetBlockAndOffset(cursor.CurrentPositionAddress, out var block, out int offset);
        block[offset] = value;
        cursor.CurrentPositionAddress++;
        cursor.WrittenLength++;
    }

    internal void WriteVarUInt(ref StreamCursor cursor, uint value)
    {
        int byteCount = 1;
        uint remaining = value;
        while (remaining >= 0x80)
        {
            byteCount++;
            remaining >>= 7;
        }
        EnsureLogicalWriteLength(cursor, byteCount);

        do
        {
            byte next = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0)
                next |= 0x80;
            WriteByte(ref cursor, next);
        }
        while (value != 0);
    }

    internal void WriteBytes(ref StreamCursor cursor, ReadOnlySpan<byte> source)
    {
        ThrowIfDisposed();
        EnsureLogicalWriteLength(cursor, source.Length);
        while (!source.IsEmpty)
        {
            EnsureWritable(ref cursor);
            GetBlockAndOffset(cursor.CurrentPositionAddress, out var block, out int offset);
            int available = cursor.CurrentDataEndAddress - cursor.CurrentPositionAddress;
            int count = Math.Min(available, source.Length);
            source[..count].CopyTo(block.AsSpan(offset, count));
            cursor.CurrentPositionAddress += count;
            cursor.WrittenLength += count;
            source = source[count..];
        }
    }

    private static void EnsureLogicalWriteLength(StreamCursor cursor, int additionalLength)
    {
        if (additionalLength < 0 || additionalLength > int.MaxValue - cursor.WrittenLength)
            throw new InvalidOperationException("The postings stream exceeds the maximum logical length.");
    }

    internal Reader OpenReader(StreamCursor cursor)
    {
        ThrowIfDisposed();
        return OpenReader(cursor.StartAddress, cursor.WrittenLength);
    }

    internal Reader OpenReader(int startAddress, int writtenLength)
    {
        ThrowIfDisposed();
        return new Reader(this, startAddress, writtenLength);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var block in _blocks)
            _bytePool.Return(block, clearArray: false);

        _blocks.Clear();
        _currentBlockIndex = -1;
        _currentBlockOffset = 0;
        _rentedBytes = 0;
        Volatile.Write(ref _allocatedBytes, 0);
    }

    private void EnsureWritable(ref StreamCursor cursor)
    {
        ThrowIfDisposed();
        ValidateCursor(cursor);
        if (cursor.CurrentPositionAddress == cursor.CurrentDataEndAddress)
            AllocateSlice(ref cursor, Math.Min(cursor.Level + 1, LargestSliceLevel));
        else if (cursor.CurrentPositionAddress > cursor.CurrentDataEndAddress)
            throw new InvalidOperationException("The postings stream cursor passed its slice boundary.");
    }

    private void AllocateSlice(ref StreamCursor cursor, int level)
    {
        int size = SliceSize(level);
        EnsureBlock(size, out byte[] block, out int blockOffset);
        int headerAddress = ComposeAddress(_currentBlockIndex, blockOffset);
        long dataEndLong = (long)headerAddress + size;
        if (dataEndLong > int.MaxValue)
            throw new InvalidOperationException("The postings arena address range is exhausted.");

        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(blockOffset, SliceHeaderSize), 0);
        if (cursor.CurrentSliceHeaderAddress != 0)
            WriteInt32At(cursor.CurrentSliceHeaderAddress, headerAddress);

        if (cursor.StartAddress == 0)
            cursor.StartAddress = headerAddress;
        cursor.CurrentSliceHeaderAddress = headerAddress;
        cursor.CurrentPositionAddress = checked(headerAddress + SliceHeaderSize);
        cursor.CurrentDataEndAddress = (int)dataEndLong;
        cursor.Level = level;
    }

    private void EnsureBlock(int sliceSize, out byte[] block, out int blockOffset)
    {
        if (_currentBlockIndex >= 0 && _currentBlockOffset + sliceSize <= BlockSize)
        {
            block = _blocks[_currentBlockIndex];
            blockOffset = _currentBlockOffset;
            _currentBlockOffset += sliceSize;
            return;
        }

        int newBlockIndex = _blocks.Count;
        _ = ComposeAddress(newBlockIndex, 0);
        block = _bytePool.Rent(BlockSize);
        if (block.Length < BlockSize)
        {
            _bytePool.Return(block, clearArray: false);
            throw new InvalidOperationException("The byte pool returned a block smaller than the arena block size.");
        }

        _blocks.Add(block);
        _rentedBytes = checked(_rentedBytes + block.LongLength);
        Volatile.Write(ref _allocatedBytes, checked(_rentedBytes + (long)_blocks.Capacity * IntPtr.Size));
        Interlocked.Increment(ref _allocationVersion);
        _currentBlockIndex = newBlockIndex;
        _currentBlockOffset = sliceSize;
        blockOffset = 0;
    }

    private void ValidateCursor(StreamCursor cursor)
    {
        if (cursor.StartAddress == 0)
            throw new InvalidOperationException("The postings stream cursor is not owned by this arena.");
        ValidateSliceAddress(cursor.CurrentSliceHeaderAddress, cursor.Level);
        int expectedDataEnd = checked(cursor.CurrentSliceHeaderAddress + SliceSize(cursor.Level));
        if (cursor.CurrentDataEndAddress != expectedDataEnd)
            throw new InvalidOperationException("The postings stream cursor has an invalid slice boundary.");
        if (cursor.CurrentPositionAddress < cursor.CurrentSliceHeaderAddress + SliceHeaderSize ||
            cursor.CurrentPositionAddress > cursor.CurrentDataEndAddress)
            throw new InvalidOperationException("The postings stream cursor is invalid.");
    }

    private static int SliceSize(int level)
        => FirstSliceSize << Math.Min(level, LargestSliceLevel);

    internal static int ComposeAddress(int blockIndex, int offset)
    {
        if (blockIndex < 0 || offset < 0 || offset >= BlockSize)
            throw new InvalidOperationException("The postings arena address is outside the block range.");
        long address = (long)blockIndex * BlockSize + offset + 1L;
        if (address > int.MaxValue)
            throw new InvalidOperationException("The postings arena address range is exhausted.");
        return (int)address;
    }

    internal static void DecodeAddress(int address, out int blockIndex, out int offset)
    {
        if (address <= 0)
            throw new InvalidDataException("A postings arena address must be positive.");
        int zeroBased = address - 1;
        blockIndex = zeroBased / BlockSize;
        offset = zeroBased % BlockSize;
    }

    private void GetBlockAndOffset(int address, out byte[] block, out int offset)
    {
        DecodeAddress(address, out int blockIndex, out offset);
        if ((uint)blockIndex >= (uint)_blocks.Count || offset >= BlockSize)
            throw new InvalidDataException("A postings arena address points outside the arena.");
        block = _blocks[blockIndex];
    }

    private void ValidateSliceAddress(int address, int level)
    {
        DecodeAddress(address, out int blockIndex, out int offset);
        if ((uint)blockIndex >= (uint)_blocks.Count)
            throw new InvalidDataException("A postings slice points outside the arena.");
        if ((long)offset + SliceSize(level) > BlockSize)
            throw new InvalidDataException("A postings slice does not fit inside its block.");
        _ = checked(address + SliceSize(level));
    }

    private int ReadInt32At(int address)
    {
        GetBlockAndOffset(address, out var block, out int offset);
        if (offset > BlockSize - SliceHeaderSize)
            throw new InvalidDataException("A postings slice header crosses a block boundary.");
        return BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(offset, SliceHeaderSize));
    }

    private void WriteInt32At(int address, int value)
    {
        GetBlockAndOffset(address, out var block, out int offset);
        if (offset > BlockSize - SliceHeaderSize)
            throw new InvalidOperationException("A postings slice header crosses a block boundary.");
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(offset, SliceHeaderSize), value);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    /// <summary>A stack-only reader over one frozen arena stream.</summary>
    internal ref struct Reader
    {
        private readonly PostingsByteArena _arena;
        private int _remaining;
        private int _sliceHeaderAddress;
        private int _positionAddress;
        private int _dataEndAddress;
        private int _level;

        internal Reader(PostingsByteArena arena, int startAddress, int writtenLength)
        {
            _arena = arena;
            _remaining = writtenLength;
            _sliceHeaderAddress = 0;
            _positionAddress = 0;
            _dataEndAddress = 0;
            _level = 0;

            if (writtenLength < 0)
                throw new ArgumentOutOfRangeException(nameof(writtenLength));

            if (startAddress == 0)
            {
                if (writtenLength != 0)
                    throw new InvalidDataException("An absent postings stream has a non-zero logical length.");
                return;
            }

            arena.ValidateSliceAddress(startAddress, 0);

            _sliceHeaderAddress = startAddress;
            _positionAddress = checked(startAddress + SliceHeaderSize);
            _dataEndAddress = checked(startAddress + SliceSize(0));
        }

        internal bool EndOfStream => _remaining == 0;

        internal byte ReadByte()
        {
            EnsureReadable(1);
            MoveToNextSliceIfNeeded();
            _arena.GetBlockAndOffset(_positionAddress, out var block, out int offset);
            byte value = block[offset];
            _positionAddress++;
            _remaining--;
            return value;
        }

        internal uint ReadVarUInt()
        {
            uint value = 0;
            for (int i = 0; i < 5; i++)
            {
                byte next = ReadByte();
                if (i == 4 && ((next & 0x80) != 0 || (next & 0x70) != 0))
                    throw new InvalidDataException("A postings VarUInt exceeds 32 bits.");
                value |= (uint)(next & 0x7F) << (7 * i);
                if ((next & 0x80) == 0)
                    return value;
            }

            throw new InvalidDataException("A postings VarUInt did not terminate within five bytes.");
        }

        internal void CopyBytes(Span<byte> destination)
        {
            EnsureReadable(destination.Length);
            while (!destination.IsEmpty)
            {
                MoveToNextSliceIfNeeded();
                _arena.GetBlockAndOffset(_positionAddress, out var block, out int offset);
                int available = Math.Min(_dataEndAddress - _positionAddress, _remaining);
                EnsureProgress(available);
                int count = Math.Min(available, destination.Length);
                block.AsSpan(offset, count).CopyTo(destination[..count]);
                _positionAddress += count;
                _remaining -= count;
                destination = destination[count..];
            }
        }

        internal void CopyBytes(IndexOutput destination, int length)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            EnsureReadable(length);
            while (length > 0)
            {
                MoveToNextSliceIfNeeded();
                _arena.GetBlockAndOffset(_positionAddress, out var block, out int offset);
                int available = Math.Min(_dataEndAddress - _positionAddress, _remaining);
                EnsureProgress(available);
                int count = Math.Min(available, length);
                destination.WriteBytes(block.AsSpan(offset, count));
                _positionAddress += count;
                _remaining -= count;
                length -= count;
            }
        }

        internal void CopyBytes(ISequentialIndexOutput destination, int length)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            EnsureReadable(length);
            while (length > 0)
            {
                MoveToNextSliceIfNeeded();
                _arena.GetBlockAndOffset(_positionAddress, out var block, out int offset);
                int available = Math.Min(_dataEndAddress - _positionAddress, _remaining);
                EnsureProgress(available);
                int count = Math.Min(available, length);
                destination.WriteBytes(block.AsSpan(offset, count));
                _positionAddress += count;
                _remaining -= count;
                length -= count;
            }
        }

        internal void SkipBytes(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            EnsureReadable(length);
            while (length > 0)
            {
                MoveToNextSliceIfNeeded();
                int count = Math.Min(Math.Min(_dataEndAddress - _positionAddress, _remaining), length);
                EnsureProgress(count);
                _positionAddress += count;
                _remaining -= count;
                length -= count;
            }
        }

        private void EnsureReadable(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length > _remaining)
                throw new InvalidDataException("A postings stream read passed its logical end.");
        }

        private static void EnsureProgress(int available)
        {
            if (available <= 0)
                throw new InvalidDataException("A postings stream reader made no progress.");
        }

        private void MoveToNextSliceIfNeeded()
        {
            if (_remaining == 0)
                return;
            if (_positionAddress < _dataEndAddress)
                return;
            if (_positionAddress > _dataEndAddress)
                throw new InvalidDataException("A postings stream cursor passed its slice boundary.");

            int next = _arena.ReadInt32At(_sliceHeaderAddress);
            if (next == 0)
                throw new InvalidDataException("A postings stream is missing its forwarding address.");
            if (next <= _sliceHeaderAddress)
                throw new InvalidDataException("A postings stream has an invalid forwarding address.");

            int nextLevel = Math.Min(_level + 1, LargestSliceLevel);
            _arena.ValidateSliceAddress(next, nextLevel);

            _sliceHeaderAddress = next;
            _level = nextLevel;
            _positionAddress = checked(next + SliceHeaderSize);
            _dataEndAddress = checked(next + SliceSize(_level));
        }
    }
}
