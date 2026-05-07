using System.Buffers;
using System.Collections;
using System.Numerics;

namespace Rowles.LeanLucene.Util;

/// <summary>
/// Compressed bitmap supporting efficient set operations on document IDs.
/// Uses three container types that auto-convert based on cardinality:
/// <list type="bullet">
///   <item>Array container (sorted ushort[], for ≤4096 values per 64K chunk)</item>
///   <item>Bitmap container (1024-element ulong[], for &gt;4096 values per 64K chunk)</item>
///   <item>Run container (pairs of [start, length], for consecutive runs)</item>
/// </list>
/// </summary>
public sealed class RoaringBitmap : IEnumerable<int>
{
    private const int ArrayTooBitmapThreshold = 4096;
    private const int BitmapToArrayThreshold = 4096;

    private ushort[] _keys;
    private Container[] _containers;
    private int _size;
    private int _cardinality;

    /// <summary>Initialises a new, empty <see cref="RoaringBitmap"/>.</summary>
    public RoaringBitmap()
    {
        _keys = new ushort[4];
        _containers = new Container[4];
        _size = 0;
        _cardinality = 0;
    }

    /// <summary>Gets the total number of set bits (document IDs) in this bitmap.</summary>
    public int Cardinality => _cardinality;

    /// <summary>Gets a value indicating whether this bitmap contains no set bits.</summary>
    public bool IsEmpty => _cardinality == 0;

    /// <summary>Adds the specified document ID to the bitmap. Has no effect if already present.</summary>
    /// <param name="docId">The document ID to set.</param>
    public void Add(int docId)
    {
        ushort hi = (ushort)(docId >>> 16);
        ushort lo = (ushort)(docId & 0xFFFF);

        int idx = FindKey(hi);
        if (idx >= 0)
        {
            int oldCard = _containers[idx].Cardinality;
            _containers[idx] = _containers[idx].Add(lo);
            _cardinality += _containers[idx].Cardinality - oldCard;
        }
        else
        {
            idx = ~idx;
            InsertNewContainer(idx, hi, new ArrayContainer(lo));
            _cardinality++;
        }
    }

    /// <summary>Adds all document IDs in the range [<paramref name="start"/>, <paramref name="end"/>) to the bitmap.</summary>
    /// <param name="start">The inclusive start of the range.</param>
    /// <param name="end">The exclusive end of the range.</param>
    public void AddRange(int start, int end)
    {
        for (int docId = start; docId < end; docId++)
            Add(docId);
    }

    /// <summary>Returns <see langword="true"/> if the specified document ID is set in this bitmap.</summary>
    /// <param name="docId">The document ID to test.</param>
    /// <returns><see langword="true"/> if the bit is set; otherwise, <see langword="false"/>.</returns>
    public bool Contains(int docId)
    {
        ushort hi = (ushort)(docId >>> 16);
        ushort lo = (ushort)(docId & 0xFFFF);
        int idx = FindKey(hi);
        return idx >= 0 && _containers[idx].Contains(lo);
    }

    /// <summary>Removes the specified document ID from the bitmap.</summary>
    /// <param name="docId">The document ID to clear.</param>
    /// <returns><see langword="true"/> if the bit was set and has been cleared; otherwise, <see langword="false"/>.</returns>
    public bool Remove(int docId)
    {
        ushort hi = (ushort)(docId >>> 16);
        ushort lo = (ushort)(docId & 0xFFFF);
        int idx = FindKey(hi);
        if (idx < 0) return false;

        int oldCard = _containers[idx].Cardinality;
        _containers[idx] = _containers[idx].Remove(lo);
        int newCard = _containers[idx].Cardinality;

        if (newCard < oldCard)
        {
            _cardinality -= (oldCard - newCard);
            if (newCard == 0)
                RemoveContainerAt(idx);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Scans all containers and converts those with consecutive runs to RunContainers
    /// where this would use less memory.
    /// </summary>
    public void OptimiseRuns()
    {
        for (int i = 0; i < _size; i++)
        {
            if (_containers[i] is ArrayContainer ac)
            {
                var rc = TryConvertToRun(ac);
                if (rc != null)
                    _containers[i] = rc;
            }
            else if (_containers[i] is BitmapContainer bc)
            {
                var rc = TryConvertBitmapToRun(bc);
                if (rc != null)
                    _containers[i] = rc;
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerator<int> GetEnumerator()
    {
        for (int i = 0; i < _size; i++)
        {
            int hi = _keys[i] << 16;
            foreach (ushort lo in _containers[i].Enumerate())
                yield return hi | lo;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region Set Operations

    /// <summary>Intersection: returns a new bitmap containing elements present in both a and b.</summary>
    public static RoaringBitmap And(RoaringBitmap a, RoaringBitmap b)
    {
        var result = new RoaringBitmap();
        int ia = 0, ib = 0;
        while (ia < a._size && ib < b._size)
        {
            if (a._keys[ia] < b._keys[ib]) { ia++; }
            else if (a._keys[ia] > b._keys[ib]) { ib++; }
            else
            {
                var c = ContainerAnd(a._containers[ia], b._containers[ib]);
                if (c.Cardinality > 0)
                {
                    result.InsertNewContainer(result._size, a._keys[ia], c);
                    result._cardinality += c.Cardinality;
                }
                ia++; ib++;
            }
        }
        return result;
    }

    /// <summary>Union: returns a new bitmap containing elements present in either a or b.</summary>
    public static RoaringBitmap Or(RoaringBitmap a, RoaringBitmap b)
    {
        var result = new RoaringBitmap();
        int ia = 0, ib = 0;
        while (ia < a._size && ib < b._size)
        {
            if (a._keys[ia] < b._keys[ib])
            {
                var c = CloneContainer(a._containers[ia]);
                result.InsertNewContainer(result._size, a._keys[ia], c);
                result._cardinality += c.Cardinality;
                ia++;
            }
            else if (a._keys[ia] > b._keys[ib])
            {
                var c = CloneContainer(b._containers[ib]);
                result.InsertNewContainer(result._size, b._keys[ib], c);
                result._cardinality += c.Cardinality;
                ib++;
            }
            else
            {
                var c = ContainerOr(a._containers[ia], b._containers[ib]);
                result.InsertNewContainer(result._size, a._keys[ia], c);
                result._cardinality += c.Cardinality;
                ia++; ib++;
            }
        }
        while (ia < a._size)
        {
            var c = CloneContainer(a._containers[ia]);
            result.InsertNewContainer(result._size, a._keys[ia], c);
            result._cardinality += c.Cardinality;
            ia++;
        }
        while (ib < b._size)
        {
            var c = CloneContainer(b._containers[ib]);
            result.InsertNewContainer(result._size, b._keys[ib], c);
            result._cardinality += c.Cardinality;
            ib++;
        }
        return result;
    }

    /// <summary>Difference: returns elements in a but not in b.</summary>
    public static RoaringBitmap AndNot(RoaringBitmap a, RoaringBitmap b)
    {
        var result = new RoaringBitmap();
        int ia = 0, ib = 0;
        while (ia < a._size && ib < b._size)
        {
            if (a._keys[ia] < b._keys[ib])
            {
                var c = CloneContainer(a._containers[ia]);
                result.InsertNewContainer(result._size, a._keys[ia], c);
                result._cardinality += c.Cardinality;
                ia++;
            }
            else if (a._keys[ia] > b._keys[ib])
            {
                ib++;
            }
            else
            {
                var c = ContainerAndNot(a._containers[ia], b._containers[ib]);
                if (c.Cardinality > 0)
                {
                    result.InsertNewContainer(result._size, a._keys[ia], c);
                    result._cardinality += c.Cardinality;
                }
                ia++; ib++;
            }
        }
        while (ia < a._size)
        {
            var c = CloneContainer(a._containers[ia]);
            result.InsertNewContainer(result._size, a._keys[ia], c);
            result._cardinality += c.Cardinality;
            ia++;
        }
        return result;
    }

    /// <summary>Symmetric difference: returns elements in either a or b but not both.</summary>
    public static RoaringBitmap Xor(RoaringBitmap a, RoaringBitmap b)
    {
        var result = new RoaringBitmap();
        int ia = 0, ib = 0;
        while (ia < a._size && ib < b._size)
        {
            if (a._keys[ia] < b._keys[ib])
            {
                var c = CloneContainer(a._containers[ia]);
                result.InsertNewContainer(result._size, a._keys[ia], c);
                result._cardinality += c.Cardinality;
                ia++;
            }
            else if (a._keys[ia] > b._keys[ib])
            {
                var c = CloneContainer(b._containers[ib]);
                result.InsertNewContainer(result._size, b._keys[ib], c);
                result._cardinality += c.Cardinality;
                ib++;
            }
            else
            {
                var c = ContainerXor(a._containers[ia], b._containers[ib]);
                if (c.Cardinality > 0)
                {
                    result.InsertNewContainer(result._size, a._keys[ia], c);
                    result._cardinality += c.Cardinality;
                }
                ia++; ib++;
            }
        }
        while (ia < a._size)
        {
            var c = CloneContainer(a._containers[ia]);
            result.InsertNewContainer(result._size, a._keys[ia], c);
            result._cardinality += c.Cardinality;
            ia++;
        }
        while (ib < b._size)
        {
            var c = CloneContainer(b._containers[ib]);
            result.InsertNewContainer(result._size, b._keys[ib], c);
            result._cardinality += c.Cardinality;
            ib++;
        }
        return result;
    }

    private static Container CloneContainer(Container c)
    {
        // Materialise values and rebuild to avoid sharing mutable state
        var values = c.Enumerate().ToArray();
        if (values.Length == 0) return new ArrayContainer();
        var ac = new ArrayContainer();
        Container result = ac;
        foreach (var v in values)
            result = result.Add(v);
        return result;
    }

    private static Container ContainerAnd(Container a, Container b)
    {
        // Both to bitmap for fast AND, or probe smaller into larger
        if (a is BitmapContainer ba && b is BitmapContainer bb)
            return BitmapBitmapAnd(ba, bb);

        // General: probe each element of the smaller set
        var (smaller, larger) = a.Cardinality <= b.Cardinality ? (a, b) : (b, a);
        var result = new ArrayContainer();
        Container r = result;
        foreach (var v in smaller.Enumerate())
        {
            if (larger.Contains(v))
                r = r.Add(v);
        }
        return r;
    }

    private static Container ContainerOr(Container a, Container b)
    {
        if (a is BitmapContainer ba && b is BitmapContainer bb)
            return BitmapBitmapOr(ba, bb);

        // General: add all from both
        Container r = new ArrayContainer();
        foreach (var v in a.Enumerate()) r = r.Add(v);
        foreach (var v in b.Enumerate()) r = r.Add(v);
        return r;
    }

    private static Container ContainerAndNot(Container a, Container b)
    {
        Container r = new ArrayContainer();
        foreach (var v in a.Enumerate())
        {
            if (!b.Contains(v))
                r = r.Add(v);
        }
        return r;
    }

    private static Container ContainerXor(Container a, Container b)
    {
        if (a is BitmapContainer ba && b is BitmapContainer bb)
            return BitmapBitmapXor(ba, bb);

        Container r = new ArrayContainer();
        foreach (var v in a.Enumerate())
        {
            if (!b.Contains(v)) r = r.Add(v);
        }
        foreach (var v in b.Enumerate())
        {
            if (!a.Contains(v)) r = r.Add(v);
        }
        return r;
    }

    private static Container BitmapBitmapAnd(BitmapContainer a, BitmapContainer b)
    {
        var bc = new BitmapContainer();
        for (int w = 0; w < 1024; w++)
            if ((a.GetWord(w) & b.GetWord(w)) != 0)
                bc.SetWord(w, a.GetWord(w) & b.GetWord(w));
        return bc.Cardinality <= BitmapToArrayThreshold ? ArrayContainer.FromBitmap(bc) : bc;
    }

    private static Container BitmapBitmapOr(BitmapContainer a, BitmapContainer b)
    {
        var bc = new BitmapContainer();
        for (int w = 0; w < 1024; w++)
            bc.SetWord(w, a.GetWord(w) | b.GetWord(w));
        return bc;
    }

    private static Container BitmapBitmapXor(BitmapContainer a, BitmapContainer b)
    {
        var bc = new BitmapContainer();
        for (int w = 0; w < 1024; w++)
            bc.SetWord(w, a.GetWord(w) ^ b.GetWord(w));
        return bc.Cardinality <= BitmapToArrayThreshold ? ArrayContainer.FromBitmap(bc) : bc;
    }

    #endregion

    #region Key management

    private int FindKey(ushort key)
    {
        return Array.BinarySearch(_keys, 0, _size, key);
    }

    private void InsertNewContainer(int index, ushort key, Container container)
    {
        if (_size == _keys.Length)
        {
            int newLen = _keys.Length * 2;
            Array.Resize(ref _keys, newLen);
            Array.Resize(ref _containers, newLen);
        }

        if (index < _size)
        {
            Array.Copy(_keys, index, _keys, index + 1, _size - index);
            Array.Copy(_containers, index, _containers, index + 1, _size - index);
        }

        _keys[index] = key;
        _containers[index] = container;
        _size++;
    }

    private void RemoveContainerAt(int index)
    {
        _size--;
        if (index < _size)
        {
            Array.Copy(_keys, index + 1, _keys, index, _size - index);
            Array.Copy(_containers, index + 1, _containers, index, _size - index);
        }
        _containers[_size] = null!;
    }

    #endregion

    #region Run conversion helpers

    private static RunContainer? TryConvertToRun(ArrayContainer ac)
    {
        if (ac.Cardinality < 4) return null;

        var values = ac.GetSortedValues();
        int runs = CountRuns(values);
        // RunContainer costs 4 bytes per run; ArrayContainer costs 2 bytes per value
        if (runs * 4 < values.Length * 2)
            return RunContainer.FromSortedValues(values);
        return null;
    }

    private static RunContainer? TryConvertBitmapToRun(BitmapContainer bc)
    {
        var values = bc.EnumerateAll().ToArray();
        int runs = CountRuns(values);
        // BitmapContainer is always 8KB; RunContainer is 4 bytes per run
        if (runs * 4 < 8192)
        {
            return RunContainer.FromSortedValues(values);
        }
        return null;
    }

    private static int CountRuns(ReadOnlySpan<ushort> sorted)
    {
        if (sorted.Length == 0) return 0;
        int runs = 1;
        for (int i = 1; i < sorted.Length; i++)
        {
            if (sorted[i] != sorted[i - 1] + 1)
                runs++;
        }
        return runs;
    }

    #endregion

    #region Serialisation

    /// <summary>
    /// Binary format: <c>[int32:magic][byte:version][int32:payloadLen][payload bytes][uint32:crc32]</c>.
    /// The payload is <c>[int32:chunkCount][chunk0_key:ushort, chunk0_type:byte, chunk0_data...]...</c>
    /// Container types: 0=Array, 1=Bitmap, 2=Run.
    /// CRC32 is computed over the payload bytes only.
    /// </summary>
    public void Serialise(BinaryWriter writer)
    {
        using var payloadStream = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payloadStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            WritePayload(payloadWriter);
        }
        var payload = payloadStream.ToArray();
        var crc = Crc32.Compute(payload);

        Codecs.CodecConstants.WriteHeader(writer, Codecs.CodecConstants.RoaringBitmapVersion);
        writer.Write(payload.Length);
        writer.Write(payload);
        writer.Write(crc);
    }

    private void WritePayload(BinaryWriter writer)
    {
        writer.Write(_size);
        for (int i = 0; i < _size; i++)
        {
            writer.Write(_keys[i]);
            switch (_containers[i])
            {
                case ArrayContainer ac:
                    writer.Write((byte)0);
                    writer.Write((ushort)ac.Cardinality);
                    foreach (ushort v in ac.Enumerate())
                        writer.Write(v);
                    break;
                case BitmapContainer bc:
                    writer.Write((byte)1);
                    // Write 1024 ulongs (8 KB)
                    for (int w = 0; w < 1024; w++)
                        writer.Write(bc.GetWord(w));
                    break;
                case RunContainer rc:
                    writer.Write((byte)2);
                    var runs = rc.GetRuns();
                    writer.Write((ushort)runs.Length);
                    for (int r = 0; r < runs.Length; r++)
                    {
                        writer.Write(runs[r].Start);
                        writer.Write(runs[r].Length);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Deserialises a <see cref="RoaringBitmap"/> from a <see cref="BinaryReader"/>.
    /// See <see cref="Serialise(BinaryWriter)"/> for the binary format.
    /// </summary>
    /// <param name="reader">The reader to deserialise from.</param>
    /// <returns>The deserialised bitmap.</returns>
    /// <exception cref="InvalidDataException">Thrown if the magic, version, length or CRC is invalid.</exception>
    public static RoaringBitmap Deserialise(BinaryReader reader)
    {
        Codecs.CodecConstants.ValidateHeader(reader, Codecs.CodecConstants.RoaringBitmapVersion, "Roaring bitmap");
        int payloadLen = reader.ReadInt32();
        if (payloadLen < 0)
            throw new InvalidDataException($"Invalid Roaring bitmap payload length: {payloadLen}.");
        var payload = reader.ReadBytes(payloadLen);
        if (payload.Length != payloadLen)
            throw new InvalidDataException(
                $"Roaring bitmap truncated: expected {payloadLen} payload bytes, got {payload.Length}.");
        uint expectedCrc = reader.ReadUInt32();
        uint actualCrc = Crc32.Compute(payload);
        if (expectedCrc != actualCrc)
            throw new InvalidDataException(
                $"Roaring bitmap CRC mismatch: expected 0x{expectedCrc:X8}, got 0x{actualCrc:X8}. The file is corrupt.");

        using var stream = new MemoryStream(payload);
        using var payloadReader = new BinaryReader(stream);
        return ReadPayload(payloadReader);
    }

    private static RoaringBitmap ReadPayload(BinaryReader reader)
    {
        int chunkCount = reader.ReadInt32();
        var bitmap = new RoaringBitmap();
        bitmap._keys = new ushort[Math.Max(chunkCount, 4)];
        bitmap._containers = new Container[bitmap._keys.Length];
        bitmap._size = chunkCount;
        bitmap._cardinality = 0;

        for (int i = 0; i < chunkCount; i++)
        {
            bitmap._keys[i] = reader.ReadUInt16();
            byte type = reader.ReadByte();
            switch (type)
            {
                case 0: // Array
                    int count = reader.ReadUInt16();
                    var values = new ushort[count];
                    for (int j = 0; j < count; j++)
                        values[j] = reader.ReadUInt16();
                    bitmap._containers[i] = new ArrayContainer(values, count);
                    bitmap._cardinality += count;
                    break;
                case 1: // Bitmap
                    var words = new ulong[1024];
                    int card = 0;
                    for (int w = 0; w < 1024; w++)
                    {
                        words[w] = reader.ReadUInt64();
                        card += BitOperations.PopCount(words[w]);
                    }
                    bitmap._containers[i] = new BitmapContainer(words, card);
                    bitmap._cardinality += card;
                    break;
                case 2: // Run
                    int runCount = reader.ReadUInt16();
                    var runs = new (ushort Start, ushort Length)[runCount];
                    int runCard = 0;
                    for (int r = 0; r < runCount; r++)
                    {
                        runs[r] = (reader.ReadUInt16(), reader.ReadUInt16());
                        runCard += runs[r].Length + 1;
                    }
                    bitmap._containers[i] = RunContainer.FromRuns(runs);
                    bitmap._cardinality += runCard;
                    break;
                default:
                    throw new InvalidDataException($"Unknown container type: {type}");
            }
        }

        return bitmap;
    }

    /// <summary>Serialises this bitmap to the specified file path.</summary>
    /// <param name="filePath">The file path to write to.</param>
    public void Serialise(string filePath)
    {
        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);
        Serialise(writer);
    }

    /// <summary>Deserialises a <see cref="RoaringBitmap"/> from the specified file path.</summary>
    /// <param name="filePath">The file path to read from.</param>
    /// <returns>The deserialised bitmap.</returns>
    public static RoaringBitmap Deserialise(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        return Deserialise(reader);
    }

    #endregion

    #region Container base

    internal abstract class Container
    {
        public abstract int Cardinality { get; }
        public abstract bool Contains(ushort value);
        public abstract Container Add(ushort value);
        public abstract Container Remove(ushort value);
        public abstract IEnumerable<ushort> Enumerate();
    }

    #endregion

    #region ArrayContainer

    internal sealed class ArrayContainer : Container
    {
        private ushort[] _values;
        private int _count;

        public ArrayContainer()
        {
            _values = new ushort[4];
            _count = 0;
        }

        public ArrayContainer(ushort initialValue)
        {
            _values = new ushort[4];
            _values[0] = initialValue;
            _count = 1;
        }

        internal ArrayContainer(ushort[] values, int count)
        {
            _values = values;
            _count = count;
        }

        public override int Cardinality => _count;

        public override bool Contains(ushort value)
        {
            return Array.BinarySearch(_values, 0, _count, value) >= 0;
        }

        public override Container Add(ushort value)
        {
            int idx = Array.BinarySearch(_values, 0, _count, value);
            if (idx >= 0) return this; // already present

            int insertIdx = ~idx;

            if (_count >= ArrayTooBitmapThreshold)
            {
                // Convert to bitmap
                var bc = new BitmapContainer();
                for (int i = 0; i < _count; i++)
                    bc.SetBit(_values[i]);
                bc.SetBit(value);
                return bc;
            }

            if (_count == _values.Length)
                Array.Resize(ref _values, _values.Length * 2);

            if (insertIdx < _count)
                Array.Copy(_values, insertIdx, _values, insertIdx + 1, _count - insertIdx);

            _values[insertIdx] = value;
            _count++;
            return this;
        }

        public override Container Remove(ushort value)
        {
            int idx = Array.BinarySearch(_values, 0, _count, value);
            if (idx < 0) return this;

            _count--;
            if (idx < _count)
                Array.Copy(_values, idx + 1, _values, idx, _count - idx);

            return this;
        }

        public override IEnumerable<ushort> Enumerate()
        {
            for (int i = 0; i < _count; i++)
                yield return _values[i];
        }

        public ReadOnlySpan<ushort> GetSortedValues() => _values.AsSpan(0, _count);

        public static ArrayContainer FromBitmap(BitmapContainer bc)
        {
            var values = new ushort[bc.Cardinality];
            int idx = 0;
            foreach (var v in bc.EnumerateAll())
                values[idx++] = v;
            return new ArrayContainer(values, idx);
        }
    }

    #endregion

    #region BitmapContainer

    internal sealed class BitmapContainer : Container
    {
        private readonly ulong[] _bits;
        private int _cardinality;

        public BitmapContainer()
        {
            _bits = new ulong[1024]; // 1024 * 64 = 65536 bits
            _cardinality = 0;
        }

        internal BitmapContainer(ulong[] bits, int cardinality)
        {
            _bits = bits;
            _cardinality = cardinality;
        }

        public override int Cardinality => _cardinality;

        public override bool Contains(ushort value)
        {
            return (_bits[value >> 6] & (1UL << (value & 63))) != 0;
        }

        internal void SetBit(ushort value)
        {
            int word = value >> 6;
            ulong mask = 1UL << (value & 63);
            if ((_bits[word] & mask) == 0)
            {
                _bits[word] |= mask;
                _cardinality++;
            }
        }

        internal ulong GetWord(int index) => _bits[index];

        internal void SetWord(int index, ulong value)
        {
            int oldPop = BitOperations.PopCount(_bits[index]);
            _bits[index] = value;
            _cardinality += BitOperations.PopCount(value) - oldPop;
        }

        public override Container Add(ushort value)
        {
            SetBit(value);
            return this;
        }

        public override Container Remove(ushort value)
        {
            int word = value >> 6;
            ulong mask = 1UL << (value & 63);
            if ((_bits[word] & mask) != 0)
            {
                _bits[word] &= ~mask;
                _cardinality--;
                if (_cardinality <= BitmapToArrayThreshold)
                    return ArrayContainer.FromBitmap(this);
            }
            return this;
        }

        public override IEnumerable<ushort> Enumerate() => EnumerateAll();

        internal IEnumerable<ushort> EnumerateAll()
        {
            for (int word = 0; word < 1024; word++)
            {
                ulong bits = _bits[word];
                while (bits != 0)
                {
                    int bit = BitOperations.TrailingZeroCount(bits);
                    yield return (ushort)(word * 64 + bit);
                    bits &= bits - 1; // clear lowest set bit
                }
            }
        }
    }

    #endregion

    #region RunContainer

    internal sealed class RunContainer : Container
    {
        // Stored as alternating pairs: start0, length0, start1, length1, ...
        // Each run represents [start, start + length] inclusive
        private ushort[] _runs;
        private int _numRuns;
        private int _cardinality;

        private RunContainer(ushort[] runs, int numRuns, int cardinality)
        {
            _runs = runs;
            _numRuns = numRuns;
            _cardinality = cardinality;
        }

        public override int Cardinality => _cardinality;

        public override bool Contains(ushort value)
        {
            // Binary search on run starts
            int lo = 0, hi = _numRuns - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >>> 1;
                ushort start = _runs[mid * 2];
                ushort length = _runs[mid * 2 + 1];
                if (value < start)
                    hi = mid - 1;
                else if (value > start + length)
                    lo = mid + 1;
                else
                    return true;
            }
            return false;
        }

        public override Container Add(ushort value)
        {
            if (Contains(value)) return this;
            // For simplicity, convert to array container and add
            var ac = ToArrayContainer();
            return ac.Add(value);
        }

        public override Container Remove(ushort value)
        {
            if (!Contains(value)) return this;
            // For simplicity, convert to array container and remove
            var ac = ToArrayContainer();
            return ac.Remove(value);
        }

        public override IEnumerable<ushort> Enumerate()
        {
            for (int i = 0; i < _numRuns; i++)
            {
                ushort start = _runs[i * 2];
                ushort length = _runs[i * 2 + 1];
                for (int v = start; v <= start + length; v++)
                    yield return (ushort)v;
            }
        }

        private ArrayContainer ToArrayContainer()
        {
            var ac = new ArrayContainer();
            foreach (ushort v in Enumerate())
                ac.Add(v);
            return (ArrayContainer)ac; // safe cast — we won't exceed 4096 in a run container used here
        }

        public static RunContainer FromSortedValues(ReadOnlySpan<ushort> sorted)
        {
            if (sorted.Length == 0)
                return new RunContainer(Array.Empty<ushort>(), 0, 0);

            // Count runs first
            int runs = 1;
            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] != sorted[i - 1] + 1)
                    runs++;
            }

            var runData = new ushort[runs * 2];
            int runIdx = 0;
            ushort start = sorted[0];
            ushort length = 0;

            for (int i = 1; i < sorted.Length; i++)
            {
                if (sorted[i] == sorted[i - 1] + 1)
                {
                    length++;
                }
                else
                {
                    runData[runIdx++] = start;
                    runData[runIdx++] = length;
                    start = sorted[i];
                    length = 0;
                }
            }
            runData[runIdx++] = start;
            runData[runIdx++] = length;

            return new RunContainer(runData, runs, sorted.Length);
        }

        internal (ushort Start, ushort Length)[] GetRuns()
        {
            var result = new (ushort Start, ushort Length)[_numRuns];
            for (int i = 0; i < _numRuns; i++)
                result[i] = (_runs[i * 2], _runs[i * 2 + 1]);
            return result;
        }

        internal static RunContainer FromRuns((ushort Start, ushort Length)[] runs)
        {
            var runData = new ushort[runs.Length * 2];
            int cardinality = 0;
            for (int i = 0; i < runs.Length; i++)
            {
                runData[i * 2] = runs[i].Start;
                runData[i * 2 + 1] = runs[i].Length;
                cardinality += runs[i].Length + 1;
            }
            return new RunContainer(runData, runs.Length, cardinality);
        }
    }

    #endregion
}
