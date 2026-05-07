namespace Rowles.LeanLucene.Codecs.Fst;

/// <summary>
/// Builds a minimal acyclic finite state transducer (FST) from sorted byte-sequence keys
/// with associated <see langword="long"/> outputs. Uses suffix-sharing (Daciuk et al.)
/// to compress the automaton: identical sub-graphs are stored only once.
///
/// <para><b>Usage:</b></para>
/// <code>
///   var builder = new FSTBuilder();
///   builder.Add(key1Bytes, output1);
///   builder.Add(key2Bytes, output2);  // keys MUST be in lexicographic byte order
///   byte[] fstData = builder.Finish();
/// </code>
///
/// <para>The returned <c>byte[]</c> can be written to disk and decoded by <c>FSTReader</c>.</para>
///
/// <para><b>Serialised format (documented for FSTReader):</b></para>
/// <list type="bullet">
///   <item>
///     <b>Header (always present):</b>
///     <list type="number">
///       <item>4 bytes — magic: ASCII <c>FST1</c> (<c>0x46 0x53 0x54 0x31</c>)</item>
///       <item>VarInt — root node address (absolute position in the node-data section, or <c>-1</c> if empty)</item>
///       <item>VarInt — key count</item>
///     </list>
///   </item>
///   <item>
///     <b>Node data:</b> Nodes are written into a contiguous byte buffer. Each node is
///     serialised as a sequence of arcs. Nodes are appended in the order they are compiled
///     (deepest-suffix-first). The root node is the last to be compiled, so its address is
///     stored in the header.
///   </item>
///   <item>
///     <b>Arc layout (each arc):</b>
///     <list type="number">
///       <item>1 byte — flags: <c>[bit 7: isFinal][bit 6: isLastArc][bit 5: hasOutput][bit 4: hasTarget][bits 3-0: reserved]</c></item>
///       <item>1 byte — label (the byte value on this transition; 0x00 for virtual arcs on final-only nodes)</item>
///       <item>If <c>hasTarget</c>: VarInt-encoded target address (absolute position in node-data section)</item>
///       <item>If <c>hasOutput</c>: VarInt-encoded output value (<see langword="long"/>, 7-bit variable-length, unsigned encoding)</item>
///     </list>
///   </item>
///   <item>
///     The <c>isLastArc</c> flag marks the final arc of a node (arcs are written consecutively;
///     the reader scans until it encounters <c>isLastArc == true</c>).
///   </item>
///   <item>
///     <b>Final-only nodes</b> (accept states with no outgoing arcs) are encoded as a single
///     virtual arc with label <c>0x00</c>, flags <c>isFinal | isLastArc</c>, and an optional
///     output (the final output).
///   </item>
///   <item>
///     <b>Final flag on arcs:</b> when a node is both final (accept state) and has outgoing arcs,
///     the <c>isFinal</c> flag is set on the <em>first</em> arc. The node's <c>FinalOutput</c> is
///     encoded as an additional virtual arc (label <c>0xFF</c>) appended <em>before</em> the real
///     arcs when the final output is non-zero. If the final output is zero, only the flag on the
///     first real arc signals acceptance.
///   </item>
/// </list>
///
/// <para><b>VarInt encoding:</b> 7-bit variable-length, little-endian, compatible with
/// <see cref="System.IO.BinaryWriter.Write7BitEncodedInt64"/>. Each byte stores 7 data bits;
/// the high bit (<c>0x80</c>) is a continuation flag.</para>
///
/// <para><b>Algorithm — incremental construction (Daciuk et al.):</b></para>
/// <list type="number">
///   <item>Maintain a "frontier" — one <see cref="UncompiledNode"/> per byte of the current key prefix.</item>
///   <item>When adding a new key, find the common prefix length with the previous key.</item>
///   <item>For each frontier node beyond the common prefix, "freeze" (compile) it:
///     check a registry of previously frozen nodes for an equivalent node (same arcs,
///     outputs, final flag). If found, reuse that address (suffix sharing). Otherwise,
///     serialise the node to the output buffer and register it.</item>
///   <item>Extend the frontier for the new key's suffix.</item>
///   <item>Distribute the output along arcs: push as much output as possible onto earlier
///     (leftmost) arcs; differences are pushed down to child arcs.</item>
///   <item><see cref="Finish"/> freezes remaining frontier nodes and returns the serialised FST.</item>
/// </list>
/// </summary>
public sealed class FSTBuilder
{
    // ── Arc flag constants ───────────────────────────────────────────────────
    internal const byte FlagIsFinal = 0b_1000_0000; // bit 7
    internal const byte FlagIsLastArc = 0b_0100_0000; // bit 6
    internal const byte FlagHasOutput = 0b_0010_0000; // bit 5
    internal const byte FlagHasTarget = 0b_0001_0000; // bit 4

    // ── Header magic bytes ("FST1") ─────────────────────────────────────────
    internal static ReadOnlySpan<byte> HeaderMagic => "FST1"u8;

    // ── Output buffer ───────────────────────────────────────────────────────
    private byte[] _buffer;
    private int _position;

    // ── Frontier (one node per byte of current key prefix + root at index 0) ─
    private UncompiledNode[] _frontier;

    // ── Previous key for sorted-order enforcement ───────────────────────────
    private byte[] _previousKey;
    private int _previousKeyLength;

    // ── Registry for suffix sharing: hash(node) → compiled address ──────────
    private readonly Dictionary<long, long> _registry;

    // ── State ───────────────────────────────────────────────────────────────
    private int _count;
    private bool _finished;

    // ── Sentinel address for "no target" / uncompiled ───────────────────────
    private const long NoAddress = -1;

    /// <summary>
    /// Number of keys added so far.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Creates a new FST builder with the specified initial buffer capacity.
    /// </summary>
    /// <param name="initialCapacity">Initial byte buffer size in bytes (default 1 MiB).</param>
    public FSTBuilder(int initialCapacity = 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity);

        _buffer = new byte[initialCapacity];
        _position = 0;
        _frontier = new UncompiledNode[64];
        for (int i = 0; i < _frontier.Length; i++)
            _frontier[i] = new UncompiledNode();
        _previousKey = new byte[64];
        _previousKeyLength = 0;
        _registry = new Dictionary<long, long>();
        _count = 0;
        _finished = false;
    }

    /// <summary>
    /// Add a key-output pair. Keys MUST be added in strict lexicographic (byte) sorted order.
    /// Duplicate keys are not permitted.
    /// </summary>
    /// <param name="key">The key bytes.</param>
    /// <param name="output">The output value (e.g. a postings file offset).</param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="key"/> is less than or equal to the previously added key.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="Finish"/> has already been called.
    /// </exception>
    public void Add(ReadOnlySpan<byte> key, long output)
    {
        if (_finished)
            throw new InvalidOperationException("Cannot add keys after Finish() has been called.");

        // ── Sorted-order enforcement ────────────────────────────────────
        if (_count > 0)
        {
            int cmp = key.SequenceCompareTo(_previousKey.AsSpan(0, _previousKeyLength));
            if (cmp < 0)
                throw new ArgumentException("Keys must be added in sorted order.");
            if (cmp == 0)
                throw new ArgumentException("Duplicate keys are not permitted.");
        }

        // ── Compute common prefix length with previous key ──────────────
        int prefixLength = CommonPrefixLength(
            _previousKey.AsSpan(0, _previousKeyLength), key);

        // ── Freeze (compile) frontier nodes beyond the common prefix ────
        FreezeTrail(prefixLength);

        // ── Ensure frontier capacity for new key ────────────────────────
        EnsureFrontierCapacity(key.Length + 1);

        // ── Extend frontier for the new key's suffix bytes ──────────────
        for (int i = prefixLength; i < key.Length; i++)
        {
            _frontier[i + 1].Reset();
            _frontier[i].AddOrUpdateArc(key[i], NoAddress, 0);
        }

        // Mark the deepest node (at key.Length) as a final/accept state
        _frontier[key.Length].IsFinal = true;
        _frontier[key.Length].FinalOutput = 0;

        // ── Output distribution ─────────────────────────────────────────
        DistributeOutput(key, prefixLength, output);

        // ── Store previous key for next comparison ──────────────────────
        if (_previousKey.Length < key.Length)
            _previousKey = new byte[Math.Max(key.Length, _previousKey.Length * 2)];
        key.CopyTo(_previousKey);
        _previousKeyLength = key.Length;

        _count++;
    }

    /// <summary>
    /// Finalise the FST and return the serialised byte array.
    /// The builder cannot be used after calling <see cref="Finish"/>.
    /// </summary>
    /// <returns>
    /// The complete serialised FST including header. If no keys were added,
    /// returns a minimal header with root address <c>-1</c> and count <c>0</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if <see cref="Finish"/> has already been called.
    /// </exception>
    public byte[] Finish()
    {
        if (_finished)
            throw new InvalidOperationException("Finish() has already been called.");
        _finished = true;

        // Freeze all remaining frontier nodes down to the root, then compile root itself.
        // FreezeTrail(0) compiles nodes from _previousKeyLength down to 1, wiring targets
        // into _frontier[0]. The root must be compiled separately as the final step.
        if (_count > 0)
        {
            FreezeTrail(0);
            CompileNode(_frontier[0]);
        }

        long rootAddress = _count > 0
            ? _frontier[0].CompiledAddress
            : NoAddress;

        // ── Build final byte[] with header prepended ────────────────────
        // Header: [magic: 4B][rootAddress: VarInt][keyCount: VarInt]
        int headerSize = 4 + VarIntSize(rootAddress) + VarIntSize(_count);
        int totalSize = headerSize + _position;
        var result = new byte[totalSize];

        int hPos = 0;
        HeaderMagic.CopyTo(result.AsSpan(hPos));
        hPos += 4;
        hPos += WriteVarInt(result, hPos, rootAddress);
        WriteVarInt(result, hPos, _count);

        // Copy compiled node data after the header
        Buffer.BlockCopy(_buffer, 0, result, headerSize, _position);

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Core algorithm methods
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Freezes frontier nodes from <c>_previousKeyLength</c> down to
    /// <paramref name="prefixLength"/> (exclusive). Each node is compiled
    /// (serialised) and its address wired into the parent's arc.
    /// </summary>
    private void FreezeTrail(int prefixLength)
    {
        for (int i = _previousKeyLength; i > prefixLength; i--)
        {
            long address = CompileNode(_frontier[i]);
            _frontier[i - 1].SetTargetForLastArc(address);
        }
    }

    /// <summary>
    /// Compiles (serialises) an <see cref="UncompiledNode"/> into the byte buffer.
    /// If an equivalent node already exists in the registry, returns the existing
    /// address (suffix sharing). Otherwise serialises the node and registers it.
    /// </summary>
    /// <returns>The absolute address of the compiled node in the node-data section.</returns>
    private long CompileNode(UncompiledNode node)
    {
        long hash = HashNode(node);
        if (_registry.TryGetValue(hash, out long existingAddress))
        {
            node.CompiledAddress = existingAddress;
            return existingAddress;
        }

        long address = _position;

        // ── Handle nodes with arcs ──────────────────────────────────────
        if (node.NumArcs > 0)
        {
            // If node is final AND has a non-zero final output, emit a virtual
            // "final output" arc before the real arcs. This arc uses label 0xFF
            // as a sentinel and carries the final output value.
            if (node.IsFinal && node.FinalOutput != 0)
            {
                byte fflags = FlagIsFinal | FlagHasOutput;
                EnsureBufferCapacity(22);
                _buffer[_position++] = fflags;
                _buffer[_position++] = 0xFF; // sentinel label for final output arc
                _position += WriteVarInt(_buffer, _position, node.FinalOutput);
            }

            for (int arcIdx = 0; arcIdx < node.NumArcs; arcIdx++)
            {
                byte flags = 0;

                // Mark final on first real arc (when final output is zero)
                if (node.IsFinal && node.FinalOutput == 0 && arcIdx == 0)
                    flags |= FlagIsFinal;

                if (arcIdx == node.NumArcs - 1)
                    flags |= FlagIsLastArc;

                if (node.Outputs[arcIdx] != 0)
                    flags |= FlagHasOutput;

                if (node.Targets[arcIdx] != NoAddress)
                    flags |= FlagHasTarget;

                EnsureBufferCapacity(22); // max: 1 flags + 1 label + 10 target + 10 output

                _buffer[_position++] = flags;
                _buffer[_position++] = node.Labels[arcIdx];

                if ((flags & FlagHasTarget) != 0)
                    _position += WriteVarInt(_buffer, _position, node.Targets[arcIdx]);

                if ((flags & FlagHasOutput) != 0)
                    _position += WriteVarInt(_buffer, _position, node.Outputs[arcIdx]);
            }
        }
        else
        {
            // ── Final-only node (accept state, no outgoing arcs) ────────
            byte flags = FlagIsLastArc;
            if (node.IsFinal)
                flags |= FlagIsFinal;
            if (node.FinalOutput != 0)
                flags |= FlagHasOutput;

            EnsureBufferCapacity(12);
            _buffer[_position++] = flags;
            _buffer[_position++] = 0x00; // virtual label

            if ((flags & FlagHasOutput) != 0)
                _position += WriteVarInt(_buffer, _position, node.FinalOutput);
        }

        _registry[hash] = address;
        node.CompiledAddress = address;
        return address;
    }

    /// <summary>
    /// Distributes the output value along shared-prefix arcs using the
    /// "push output left" strategy. For each shared arc, the output is set
    /// to <c>min(existing, new)</c> and the difference is pushed to child arcs.
    /// </summary>
    private void DistributeOutput(ReadOnlySpan<byte> key, int prefixLength, long output)
    {
        for (int i = 0; i < prefixLength; i++)
        {
            long existingOutput = _frontier[i].GetOutputForLabel(key[i]);
            long commonOutput = Math.Min(existingOutput, output);
            long existingDelta = existingOutput - commonOutput;
            long newDelta = output - commonOutput;

            // Set shared arc output to the common (minimum) value
            _frontier[i].SetOutputForLabel(key[i], commonOutput);

            // Push existing difference down to all arcs of the child node
            if (existingDelta != 0)
                _frontier[i + 1].PrependOutput(existingDelta);

            output = newDelta;
        }

        // Remaining output goes on the first new arc or the final output
        if (prefixLength < key.Length)
        {
            long currentOutput = _frontier[prefixLength].GetOutputForLabel(key[prefixLength]);
            _frontier[prefixLength].SetOutputForLabel(key[prefixLength], currentOutput + output);
        }
        else
        {
            // Key equals a prefix of another key — accumulate on final output
            _frontier[key.Length].FinalOutput += output;
        }
    }

    /// <summary>
    /// Computes a 64-bit FNV-1a hash of the node for deduplication.
    /// Incorporates arc labels, targets, outputs, and final state.
    /// </summary>
    private static long HashNode(UncompiledNode node)
    {
        ulong hash = 14695981039346656037UL; // FNV offset basis

        for (int i = 0; i < node.NumArcs; i++)
        {
            hash ^= node.Labels[i];
            hash *= 1099511628211UL;
            hash ^= unchecked((ulong)node.Targets[i]);
            hash *= 1099511628211UL;
            hash ^= unchecked((ulong)node.Outputs[i]);
            hash *= 1099511628211UL;
        }

        hash ^= node.IsFinal ? 1UL : 0UL;
        hash *= 1099511628211UL;
        hash ^= unchecked((ulong)node.FinalOutput);
        hash *= 1099511628211UL;

        return unchecked((long)hash);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Utility methods
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the length of the longest common prefix between two byte spans.
    /// </summary>
    private static int CommonPrefixLength(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if (a[i] != b[i])
                return i;
        }
        return len;
    }

    private void EnsureFrontierCapacity(int requiredLength)
    {
        if (_frontier.Length >= requiredLength)
            return;

        int newLength = _frontier.Length;
        while (newLength < requiredLength)
            newLength *= 2;

        var newFrontier = new UncompiledNode[newLength];
        Array.Copy(_frontier, newFrontier, _frontier.Length);
        for (int i = _frontier.Length; i < newLength; i++)
            newFrontier[i] = new UncompiledNode();
        _frontier = newFrontier;
    }

    private void EnsureBufferCapacity(int additionalBytes)
    {
        if (_position + additionalBytes <= _buffer.Length)
            return;

        int newLength = _buffer.Length;
        while (newLength < _position + additionalBytes)
            newLength *= 2;

        var newBuffer = new byte[newLength];
        Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _position);
        _buffer = newBuffer;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  VarInt encoding — 7-bit variable-length, little-endian
    //  Compatible with BinaryWriter.Write7BitEncodedInt64.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Writes a <see langword="long"/> value as a 7-bit variable-length integer.
    /// Returns the number of bytes written.
    /// </summary>
    internal static int WriteVarInt(byte[] buffer, int offset, long value)
    {
        ulong v = unchecked((ulong)value);
        int written = 0;
        while (v >= 0x80)
        {
            buffer[offset + written] = (byte)(v | 0x80);
            v >>= 7;
            written++;
        }
        buffer[offset + written] = (byte)v;
        return written + 1;
    }

    /// <summary>
    /// Reads a VarInt-encoded <see langword="long"/> from <paramref name="buffer"/>
    /// starting at <paramref name="offset"/>. Advances <paramref name="offset"/>
    /// past the consumed bytes.
    /// </summary>
    internal static long ReadVarInt(ReadOnlySpan<byte> buffer, ref int offset)
    {
        ulong result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = buffer[offset++];
            result |= (ulong)(b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);

        return unchecked((long)result);
    }

    /// <summary>
    /// Returns the number of bytes needed to VarInt-encode <paramref name="value"/>.
    /// </summary>
    internal static int VarIntSize(long value)
    {
        ulong v = unchecked((ulong)value);
        int size = 1;
        while (v >= 0x80)
        {
            v >>= 7;
            size++;
        }
        return size;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  UncompiledNode — mutable node used during FST construction
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A mutable node in the frontier during FST construction.
    /// Holds an ordered list of arcs (label → target + output)
    /// and a final flag with optional final output.
    /// </summary>
    private sealed class UncompiledNode
    {
        private const int InitialArcCapacity = 8;

        /// <summary>Number of arcs currently stored.</summary>
        public int NumArcs;

        /// <summary>Arc labels (byte values), in insertion order.</summary>
        public byte[] Labels;

        /// <summary>Compiled target addresses (<see cref="NoAddress"/> if not yet compiled).</summary>
        public long[] Targets;

        /// <summary>Output values on each arc.</summary>
        public long[] Outputs;

        /// <summary>Whether this node is an accept (final) state.</summary>
        public bool IsFinal;

        /// <summary>Output emitted when this node is an accept state with no further transitions.</summary>
        public long FinalOutput;

        /// <summary>Absolute address in the byte buffer after compilation.</summary>
        public long CompiledAddress;

        public UncompiledNode()
        {
            Labels = new byte[InitialArcCapacity];
            Targets = new long[InitialArcCapacity];
            Outputs = new long[InitialArcCapacity];
            Reset();
        }

        /// <summary>Resets the node to an empty state for reuse in the frontier.</summary>
        public void Reset()
        {
            NumArcs = 0;
            IsFinal = false;
            FinalOutput = 0;
            CompiledAddress = NoAddress;
        }

        /// <summary>
        /// Adds a new arc or updates an existing arc with the given label.
        /// </summary>
        public void AddOrUpdateArc(byte label, long target, long output)
        {
            for (int i = 0; i < NumArcs; i++)
            {
                if (Labels[i] == label)
                {
                    Targets[i] = target;
                    Outputs[i] = output;
                    return;
                }
            }

            EnsureArcCapacity(NumArcs + 1);
            Labels[NumArcs] = label;
            Targets[NumArcs] = target;
            Outputs[NumArcs] = output;
            NumArcs++;
        }

        /// <summary>
        /// Sets the compiled target address on the most recently added arc.
        /// Called by <see cref="FreezeTrail"/> to wire a child's compiled address
        /// into the parent node.
        /// </summary>
        public void SetTargetForLastArc(long address)
        {
            if (NumArcs > 0)
                Targets[NumArcs - 1] = address;
        }

        /// <summary>Returns the output for the arc with the given label, or 0 if absent.</summary>
        public long GetOutputForLabel(byte label)
        {
            for (int i = 0; i < NumArcs; i++)
            {
                if (Labels[i] == label)
                    return Outputs[i];
            }
            return 0;
        }

        /// <summary>Sets the output for the arc with the given label (must already exist).</summary>
        public void SetOutputForLabel(byte label, long output)
        {
            for (int i = 0; i < NumArcs; i++)
            {
                if (Labels[i] == label)
                {
                    Outputs[i] = output;
                    return;
                }
            }
        }

        /// <summary>
        /// Adds <paramref name="delta"/> to every arc's output and to the final output.
        /// Used during output distribution to push accumulated differences down the tree.
        /// </summary>
        public void PrependOutput(long delta)
        {
            for (int i = 0; i < NumArcs; i++)
                Outputs[i] += delta;

            if (IsFinal)
                FinalOutput += delta;
        }

        private void EnsureArcCapacity(int required)
        {
            if (Labels.Length >= required)
                return;

            int newCapacity = Labels.Length;
            while (newCapacity < required)
                newCapacity *= 2;

            Array.Resize(ref Labels, newCapacity);
            Array.Resize(ref Targets, newCapacity);
            Array.Resize(ref Outputs, newCapacity);
        }
    }
}
