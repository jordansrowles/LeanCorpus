namespace Rowles.LeanCorpus.Codecs.Fst;

/// <summary>
/// Reads a minimal acyclic FST serialised by <see cref="FstBuilder"/> (FST1 format).
///
/// <para>Provides arc-walk lookup (O(key length)), prefix enumeration (DFS over shared-prefix
/// sub-graphs), and intersection with an arbitrary <see cref="IAutomaton"/> for prefix,
/// wildcard, and Levenshtein queries — all without materialising the full key set.</para>
///
/// <para>The serialised blob layout is the one documented on <see cref="FstBuilder"/>:</para>
/// <code>
/// [magic "FST1" 4B][rootAddress VarInt][keyCount VarInt][node data...]
/// </code>
/// <para>Arc addresses produced by the builder are offsets into the node-data section
/// (the bytes following the header), not into the whole blob. The reader keeps the node
/// section in <c>_nodes</c> so addresses index it directly.</para>
/// </summary>
public sealed class FstReader
{
    private const long NoAddress = -1L;
    private const byte FlagIsFinal = FstBuilder.FlagIsFinal;
    private const byte FlagIsLastArc = FstBuilder.FlagIsLastArc;
    private const byte FlagHasOutput = FstBuilder.FlagHasOutput;
    private const byte FlagHasTarget = FstBuilder.FlagHasTarget;

    private readonly byte[] _nodes;
    private readonly long _rootAddress;
    private readonly long _count;

    private FstReader(byte[] nodes, long rootAddress, long count)
    {
        _nodes = nodes;
        _rootAddress = rootAddress;
        _count = count;
    }

    /// <summary>Number of keys stored in the FST.</summary>
    public long Count => _count;

    /// <summary>True when the FST has no keys.</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>Open an FST from a complete serialised blob produced by <see cref="FstBuilder.Finish"/>.</summary>
    public static FstReader Open(byte[] blob)
    {
        if (blob is null) throw new ArgumentNullException(nameof(blob));
        if (blob.Length < 4) throw new InvalidDataException("FST blob too small to contain header.");
        var span = blob.AsSpan();
        if (!span[..4].SequenceEqual(FstBuilder.HeaderMagic))
            throw new InvalidDataException("Invalid FST magic; expected 'FST1'.");

        int pos = 4;
        long rootAddress = FstBuilder.ReadVarInt(span, ref pos);
        long count = FstBuilder.ReadVarInt(span, ref pos);

        var nodes = new byte[blob.Length - pos];
        Buffer.BlockCopy(blob, pos, nodes, 0, nodes.Length);
        return new FstReader(nodes, rootAddress, count);
    }

    /// <summary>
    /// Walks the FST for an exact key match. Returns true and the summed output along
    /// the accept path (real arcs plus the optional final-output virtual arc) when found.
    /// </summary>
    public bool TryGetOutput(ReadOnlySpan<byte> key, out long output)
    {
        output = 0;
        if (_count == 0 || _rootAddress == NoAddress) return false;

        long nodeAddr = _rootAddress;
        long accumulated = 0;

        for (int i = 0; i < key.Length; i++)
        {
            byte label = key[i];
            if (!TryFollowArc(nodeAddr, label, out long target, out long arcOutput, out _, out _))
                return false;

            accumulated += arcOutput;
            if (target == NoAddress)
            {
                // Arc has no target; this only accepts when the key ends here AND the arc is final.
                // TryFollowArc returns isFinal only when the matched arc is the first arc and the node is final;
                // for an accept by terminating-arc we need the special node logic via the final-output virtual arc.
                // Reaching here means the FST has an arc to a final-only sub-node represented inline.
                return i == key.Length - 1;
            }
            nodeAddr = target;
        }

        // Walked the full key. The current node must be final.
        if (!TryGetFinalOutput(nodeAddr, out long finalOutput)) return false;
        output = accumulated + finalOutput;
        return true;
    }

    /// <summary>Enumerates all (key, output) pairs in sorted byte order.</summary>
    public IEnumerable<(byte[] Key, long Output)> EnumerateAll()
        => EnumerateWithPrefix(ReadOnlySpan<byte>.Empty);

    /// <summary>
    /// Enumerates all (key, output) pairs whose key starts with <paramref name="prefix"/>,
    /// in sorted byte order.
    /// </summary>
    public IEnumerable<(byte[] Key, long Output)> EnumerateWithPrefix(ReadOnlySpan<byte> prefix)
    {
        if (_count == 0 || _rootAddress == NoAddress) return [];

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        var prefixCopy = prefix.ToArray();
        for (int i = 0; i < prefixCopy.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, prefixCopy[i], out long target, out long arcOutput, out _, out _))
                return [];
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                // Arc terminates inline: the only key with this prefix is the prefix itself (if accepted here).
                if (i == prefixCopy.Length - 1)
                    return [(prefixCopy, accumulated)];
                return [];
            }
            nodeAddr = target;
        }

        return EnumerateFromNode(nodeAddr, prefixCopy, accumulated);
    }

    /// <summary>
    /// Intersects the FST with an automaton, returning matching (key, output, finalState) triples.
    /// Both the automaton and the FST consume the same byte sequence; pruning is driven by
    /// <see cref="IAutomaton.CanMatch"/>. The automaton is fed every byte of every key
    /// (callers wishing to skip a leading qualifier should construct a
    /// <see cref="PrefixAutomaton"/> over the qualifier and compose it with their target automaton,
    /// or use the qualifier overload).
    /// </summary>
    public IEnumerable<(byte[] Key, long Output, int FinalState)> IntersectAutomaton(IAutomaton automaton)
    {
        if (_count == 0 || _rootAddress == NoAddress) yield break;
        if (!automaton.CanMatch(automaton.Start)) yield break;

        foreach (var hit in IntersectInternal(_rootAddress, automaton.Start, [], 0, automaton))
            yield return hit;
    }

    /// <summary>
    /// Intersects the FST with an automaton applied to the bytes following <paramref name="qualifier"/>.
    /// Only keys starting with the qualifier are considered; the automaton sees the bare suffix.
    /// </summary>
    public IEnumerable<(byte[] Key, long Output, int FinalState)> IntersectAutomaton(
        IAutomaton automaton, ReadOnlySpan<byte> qualifier)
        => IntersectAutomatonQualified(automaton, qualifier.ToArray());

    /// <summary>
    /// Outputs-only variant of <see cref="IntersectAutomaton(IAutomaton, ReadOnlySpan{byte})"/>.
    /// Avoids materialising the matched key, suitable for offset-only query paths.
    /// </summary>
    public IEnumerable<(long Output, int FinalState)> IntersectAutomatonOutputs(
        IAutomaton automaton, ReadOnlySpan<byte> qualifier)
        => IntersectAutomatonOutputsQualified(automaton, qualifier.ToArray());

    /// <summary>
    /// Outputs-only enumeration of all outputs whose key starts with <paramref name="prefix"/>.
    /// Does not materialise any keys.
    /// </summary>
    public IEnumerable<long> EnumerateOutputsWithPrefix(ReadOnlySpan<byte> prefix)
        => EnumerateOutputsWithPrefixImpl(prefix.ToArray());

    /// <summary>
    /// Returns the outputs for every key starting with <paramref name="qualifier"/> whose suffix
    /// contains <paramref name="needle"/> (UTF-8 byte-wise substring match). The key is kept in a
    /// reusable buffer and never materialised as an array.
    /// </summary>
    public IEnumerable<long> EnumerateContainsOutputs(ReadOnlySpan<byte> qualifier, ReadOnlySpan<byte> needle)
        => EnumerateContainsOutputsImpl(qualifier.ToArray(), needle.ToArray());

    private IEnumerable<(byte[] Key, long Output, int FinalState)> IntersectAutomatonQualified(
        IAutomaton automaton, byte[] qualifier)
    {
        if (_count == 0 || _rootAddress == NoAddress) yield break;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < qualifier.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, qualifier[i], out long target, out long arcOutput, out _, out _))
                yield break;
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                if (i == qualifier.Length - 1 && automaton.IsAccept(automaton.Start))
                    yield return (qualifier, accumulated, automaton.Start);
                yield break;
            }
            nodeAddr = target;
        }

        if (!automaton.CanMatch(automaton.Start)) yield break;
        foreach (var hit in IntersectInternal(nodeAddr, automaton.Start, qualifier, accumulated, automaton))
            yield return hit;
    }

    // -- internal traversal -------------------------------------------------

    private IEnumerable<(byte[] Key, long Output)> EnumerateFromNode(long nodeAddr, byte[] prefix, long accumulatedOutput)
    {
        // Iterative DFS; the stack stores (nodeAddr, arcCursor, accumulatedOutput, keyLengthOnEntry).
        var stack = new Stack<Frame>();
        var keyBuf = new byte[Math.Max(64, prefix.Length + 16)];
        Buffer.BlockCopy(prefix, 0, keyBuf, 0, prefix.Length);
        int keyLen = prefix.Length;

        // Emit final output for the entry node if it is final.
        if (TryGetFinalOutput(nodeAddr, out long entryFinal))
        {
            var k = new byte[keyLen];
            Buffer.BlockCopy(keyBuf, 0, k, 0, keyLen);
            yield return (k, accumulatedOutput + entryFinal);
        }

        stack.Push(new Frame(nodeAddr, FirstRealArcOffset(nodeAddr), accumulatedOutput, keyLen));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();

            if (frame.ArcCursor < 0)
            {
                stack.Pop();
                if (stack.Count > 0) keyLen = stack.Peek().KeyLengthBeforeArc;
                continue;
            }

            int pos = frame.ArcCursor;
            var arc = DecodeArc(ref pos);
            // Replace the frame with one pointing to the next arc (or -1 if last).
            stack.Pop();
            stack.Push(new Frame(frame.NodeAddr, arc.IsLastArc ? -1 : pos, frame.AccumulatedOutput, frame.KeyLengthBeforeArc));

            long childOutput = frame.AccumulatedOutput + arc.Output;
            keyLen = frame.KeyLengthBeforeArc;
            EnsureKeyCapacity(ref keyBuf, keyLen + 1);
            keyBuf[keyLen] = arc.Label;
            int childKeyLen = keyLen + 1;

            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal))
                {
                    var k = new byte[childKeyLen];
                    Buffer.BlockCopy(keyBuf, 0, k, 0, childKeyLen);
                    yield return (k, childOutput + childFinal);
                }

                int firstChildArc = FirstRealArcOffset(childAddr);
                if (firstChildArc >= 0)
                {
                    stack.Push(new Frame(childAddr, firstChildArc, childOutput, childKeyLen));
                    keyLen = childKeyLen;
                }
            }
            else
            {
                // Targetless arc represents an inline accept (key terminates on this arc).
                var k = new byte[childKeyLen];
                Buffer.BlockCopy(keyBuf, 0, k, 0, childKeyLen);
                yield return (k, childOutput);
            }
        }
    }

    private IEnumerable<(byte[] Key, long Output, int FinalState)> IntersectInternal(
        long startNode, int startState, byte[] prefix, long accumulatedOutput, IAutomaton automaton)
    {
        var stack = new Stack<IntersectFrame>();
        var keyBuf = new byte[Math.Max(64, prefix.Length + 16)];
        Buffer.BlockCopy(prefix, 0, keyBuf, 0, prefix.Length);
        int keyLen = prefix.Length;

        // Entry node may itself be accepting; check before pushing arcs.
        if (TryGetFinalOutput(startNode, out long startFinal) && automaton.IsAccept(startState))
        {
            var k = new byte[keyLen];
            Buffer.BlockCopy(keyBuf, 0, k, 0, keyLen);
            yield return (k, accumulatedOutput + startFinal, startState);
        }

        stack.Push(new IntersectFrame(startNode, FirstRealArcOffset(startNode), accumulatedOutput, startState, keyLen));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();

            if (frame.ArcCursor < 0)
            {
                stack.Pop();
                if (stack.Count > 0) keyLen = stack.Peek().KeyLengthBeforeArc;
                continue;
            }

            int pos = frame.ArcCursor;
            var arc = DecodeArc(ref pos);
            stack.Pop();
            stack.Push(new IntersectFrame(frame.NodeAddr, arc.IsLastArc ? -1 : pos, frame.AccumulatedOutput, frame.AutomatonState, frame.KeyLengthBeforeArc));

            int nextState = automaton.Step(frame.AutomatonState, arc.Label);
            if (!automaton.CanMatch(nextState))
                continue;

            long childOutput = frame.AccumulatedOutput + arc.Output;
            keyLen = frame.KeyLengthBeforeArc;
            EnsureKeyCapacity(ref keyBuf, keyLen + 1);
            keyBuf[keyLen] = arc.Label;
            int childKeyLen = keyLen + 1;

            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal) && automaton.IsAccept(nextState))
                {
                    var k = new byte[childKeyLen];
                    Buffer.BlockCopy(keyBuf, 0, k, 0, childKeyLen);
                    yield return (k, childOutput + childFinal, nextState);
                }

                int firstChildArc = FirstRealArcOffset(childAddr);
                if (firstChildArc >= 0)
                {
                    stack.Push(new IntersectFrame(childAddr, firstChildArc, childOutput, nextState, childKeyLen));
                    keyLen = childKeyLen;
                }
            }
            else if (automaton.IsAccept(nextState))
            {
                var k = new byte[childKeyLen];
                Buffer.BlockCopy(keyBuf, 0, k, 0, childKeyLen);
                yield return (k, childOutput, nextState);
            }
        }
    }

    // -- allocation-light outputs-only traversals -------------------------

    /// <summary>
    /// Collects all outputs whose key matches <paramref name="automaton"/> when applied to the
    /// suffix following <paramref name="qualifier"/>, appending to <paramref name="sink"/>.
    /// Allocation-light: no iterator state-machine or per-arc copying.
    /// </summary>
    public void CollectIntersectOutputs(IAutomaton automaton, ReadOnlySpan<byte> qualifier, List<long> sink)
    {
        if (_count == 0 || _rootAddress == NoAddress) return;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < qualifier.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, qualifier[i], out long target, out long arcOutput, out _, out _))
                return;
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                if (i == qualifier.Length - 1 && automaton.IsAccept(automaton.Start))
                    sink.Add(accumulated);
                return;
            }
            nodeAddr = target;
        }

        int startState = automaton.Start;
        if (!automaton.CanMatch(startState)) return;

        if (TryGetFinalOutput(nodeAddr, out long startFinal) && automaton.IsAccept(startState))
            sink.Add(accumulated + startFinal);

        CollectIntersectRecursive(nodeAddr, startState, accumulated, automaton, sink);
    }

    private void CollectIntersectRecursive(long nodeAddr, int automatonState, long accumulatedOutput, IAutomaton automaton, List<long> sink)
    {
        // When the automaton is in a fully-permissive sink state, every remaining
        // FST path is a match. Skip automaton calls and collect all outputs.
        if (automaton.IsSink(automatonState))
        {
            if (TryGetFinalOutput(nodeAddr, out long final))
                sink.Add(accumulatedOutput + final);
            CollectOutputsRecursive(nodeAddr, accumulatedOutput, sink);
            return;
        }

        int firstArc = FirstRealArcOffset(nodeAddr);
        if (firstArc < 0) return;
        int pos = firstArc;
        while (true)
        {
            var arc = DecodeArc(ref pos);
            int nextState = automaton.Step(automatonState, arc.Label);
            if (automaton.CanMatch(nextState))
            {
                long childOutput = accumulatedOutput + arc.Output;
                if (arc.HasTarget)
                {
                    long childAddr = arc.Target;
                    if (TryGetFinalOutput(childAddr, out long childFinal) && automaton.IsAccept(nextState))
                        sink.Add(childOutput + childFinal);
                    CollectIntersectRecursive(childAddr, nextState, childOutput, automaton, sink);
                }
                else if (automaton.IsAccept(nextState))
                {
                    sink.Add(childOutput);
                }
            }
            if (arc.IsLastArc) break;
        }
    }

    /// <summary>
    /// Collects all outputs whose key starts with <paramref name="prefix"/>, appending to
    /// <paramref name="sink"/>. Allocation-light: no iterator state-machine.
    /// </summary>
    public void CollectOutputsWithPrefix(ReadOnlySpan<byte> prefix, List<long> sink)
    {
        if (_count == 0 || _rootAddress == NoAddress) return;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, prefix[i], out long target, out long arcOutput, out _, out _))
                return;
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                if (i == prefix.Length - 1) sink.Add(accumulated);
                return;
            }
            nodeAddr = target;
        }

        if (TryGetFinalOutput(nodeAddr, out long entryFinal))
            sink.Add(accumulated + entryFinal);

        CollectOutputsRecursive(nodeAddr, accumulated, sink);
    }

    private void CollectOutputsRecursive(long nodeAddr, long accumulatedOutput, List<long> sink)
    {
        int firstArc = FirstRealArcOffset(nodeAddr);
        if (firstArc < 0) return;
        int pos = firstArc;
        while (true)
        {
            var arc = DecodeArc(ref pos);
            long childOutput = accumulatedOutput + arc.Output;
            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal))
                    sink.Add(childOutput + childFinal);
                CollectOutputsRecursive(childAddr, childOutput, sink);
            }
            else
            {
                sink.Add(childOutput);
            }
            if (arc.IsLastArc) break;
        }
    }

    /// <summary>
    /// Collects outputs for keys starting with <paramref name="qualifier"/> whose suffix contains
    /// <paramref name="needle"/> (UTF-8 byte-wise). Allocation-light: reuses a single key buffer
    /// and emits offsets directly into <paramref name="sink"/>.
    /// </summary>
    public void CollectContainsOutputs(ReadOnlySpan<byte> qualifier, ReadOnlySpan<byte> needle, List<long> sink)
    {
        if (_count == 0 || _rootAddress == NoAddress) return;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < qualifier.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, qualifier[i], out long target, out long arcOutput, out _, out _))
                return;
            accumulated += arcOutput;
            if (target == NoAddress) return;
            nodeAddr = target;
        }

        if (needle.Length == 0)
        {
            // Empty needle matches every key with this qualifier.
            if (TryGetFinalOutput(nodeAddr, out long entryFinal))
                sink.Add(accumulated + entryFinal);
            CollectOutputsRecursive(nodeAddr, accumulated, sink);
            return;
        }

        Span<byte> keyBuf = stackalloc byte[256];
        byte[]? rented = null;
        try
        {
            CollectContainsRecursive(nodeAddr, accumulated, ref keyBuf, ref rented, 0, needle, sink);
        }
        finally
        {
            if (rented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private void CollectContainsRecursive(long nodeAddr, long accumulatedOutput, ref Span<byte> keyBuf, ref byte[]? rented, int keyLen, ReadOnlySpan<byte> needle, List<long> sink)
    {
        int firstArc = FirstRealArcOffset(nodeAddr);
        if (firstArc < 0) return;
        int pos = firstArc;
        while (true)
        {
            var arc = DecodeArc(ref pos);
            EnsureKeyCapacity(ref keyBuf, ref rented, keyLen + 1);
            keyBuf[keyLen] = arc.Label;
            int childKeyLen = keyLen + 1;
            long childOutput = accumulatedOutput + arc.Output;

            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal))
                {
                    if (keyBuf.Slice(0, childKeyLen).IndexOf(needle) >= 0)
                        sink.Add(childOutput + childFinal);
                }
                CollectContainsRecursive(childAddr, childOutput, ref keyBuf, ref rented, childKeyLen, needle, sink);
            }
            else
            {
                if (keyBuf.Slice(0, childKeyLen).IndexOf(needle) >= 0)
                    sink.Add(childOutput);
            }

            if (arc.IsLastArc) break;
        }
    }

    private static void EnsureKeyCapacity(ref Span<byte> keyBuf, ref byte[]? rented, int needed)
    {
        if (keyBuf.Length >= needed) return;
        int newSize = keyBuf.Length;
        while (newSize < needed) newSize *= 2;
        var fresh = System.Buffers.ArrayPool<byte>.Shared.Rent(newSize);
        keyBuf.Slice(0, keyBuf.Length).CopyTo(fresh);
        if (rented is not null) System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        rented = fresh;
        keyBuf = fresh;
    }

    private IEnumerable<(long Output, int FinalState)> IntersectAutomatonOutputsQualified(
        IAutomaton automaton, byte[] qualifier)
    {
        if (_count == 0 || _rootAddress == NoAddress) yield break;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < qualifier.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, qualifier[i], out long target, out long arcOutput, out _, out _))
                yield break;
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                if (i == qualifier.Length - 1 && automaton.IsAccept(automaton.Start))
                    yield return (accumulated, automaton.Start);
                yield break;
            }
            nodeAddr = target;
        }

        if (!automaton.CanMatch(automaton.Start)) yield break;
        foreach (var hit in IntersectOutputsInternal(nodeAddr, automaton.Start, accumulated, automaton))
            yield return hit;
    }

    private IEnumerable<(long Output, int FinalState)> IntersectOutputsInternal(
        long startNode, int startState, long accumulatedOutput, IAutomaton automaton)
    {
        if (TryGetFinalOutput(startNode, out long startFinal) && automaton.IsAccept(startState))
            yield return (accumulatedOutput + startFinal, startState);

        var stack = new Stack<IntersectFrame>();
        stack.Push(new IntersectFrame(startNode, FirstRealArcOffset(startNode), accumulatedOutput, startState, 0));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (frame.ArcCursor < 0) { stack.Pop(); continue; }

            int pos = frame.ArcCursor;
            var arc = DecodeArc(ref pos);
            stack.Pop();
            stack.Push(new IntersectFrame(frame.NodeAddr, arc.IsLastArc ? -1 : pos, frame.AccumulatedOutput, frame.AutomatonState, 0));

            int nextState = automaton.Step(frame.AutomatonState, arc.Label);
            if (!automaton.CanMatch(nextState)) continue;

            long childOutput = frame.AccumulatedOutput + arc.Output;

            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal) && automaton.IsAccept(nextState))
                    yield return (childOutput + childFinal, nextState);

                int firstChildArc = FirstRealArcOffset(childAddr);
                if (firstChildArc >= 0)
                    stack.Push(new IntersectFrame(childAddr, firstChildArc, childOutput, nextState, 0));
            }
            else if (automaton.IsAccept(nextState))
            {
                yield return (childOutput, nextState);
            }
        }
    }

    private IEnumerable<long> EnumerateOutputsWithPrefixImpl(byte[] prefix)
    {
        if (_count == 0 || _rootAddress == NoAddress) yield break;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, prefix[i], out long target, out long arcOutput, out _, out _))
                yield break;
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                if (i == prefix.Length - 1)
                    yield return accumulated;
                yield break;
            }
            nodeAddr = target;
        }

        foreach (var output in EnumerateOutputsFromNode(nodeAddr, accumulated))
            yield return output;
    }

    private IEnumerable<long> EnumerateOutputsFromNode(long nodeAddr, long accumulatedOutput)
    {
        if (TryGetFinalOutput(nodeAddr, out long entryFinal))
            yield return accumulatedOutput + entryFinal;

        var stack = new Stack<Frame>();
        stack.Push(new Frame(nodeAddr, FirstRealArcOffset(nodeAddr), accumulatedOutput, 0));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (frame.ArcCursor < 0) { stack.Pop(); continue; }

            int pos = frame.ArcCursor;
            var arc = DecodeArc(ref pos);
            stack.Pop();
            stack.Push(new Frame(frame.NodeAddr, arc.IsLastArc ? -1 : pos, frame.AccumulatedOutput, 0));

            long childOutput = frame.AccumulatedOutput + arc.Output;

            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal))
                    yield return childOutput + childFinal;

                int firstChildArc = FirstRealArcOffset(childAddr);
                if (firstChildArc >= 0)
                    stack.Push(new Frame(childAddr, firstChildArc, childOutput, 0));
            }
            else
            {
                yield return childOutput;
            }
        }
    }

    private IEnumerable<long> EnumerateContainsOutputsImpl(byte[] qualifier, byte[] needle)
    {
        if (_count == 0 || _rootAddress == NoAddress) yield break;

        long nodeAddr = _rootAddress;
        long accumulated = 0;
        for (int i = 0; i < qualifier.Length; i++)
        {
            if (!TryFollowArc(nodeAddr, qualifier[i], out long target, out long arcOutput, out _, out _))
                yield break;
            accumulated += arcOutput;
            if (target == NoAddress)
            {
                // The qualifier itself is a complete key; nothing follows for substring matching.
                yield break;
            }
            nodeAddr = target;
        }

        // Stream the suffix bytes through a reusable buffer and run a byte-wise Contains on each accept.
        var stack = new Stack<Frame>();
        var keyBuf = new byte[64];
        int keyLen = 0;

        if (TryGetFinalOutput(nodeAddr, out long entryFinal))
        {
            // Empty suffix; matches only when needle itself is empty.
            if (needle.Length == 0)
                yield return accumulated + entryFinal;
        }

        stack.Push(new Frame(nodeAddr, FirstRealArcOffset(nodeAddr), accumulated, 0));

        while (stack.Count > 0)
        {
            var frame = stack.Peek();
            if (frame.ArcCursor < 0)
            {
                stack.Pop();
                if (stack.Count > 0) keyLen = stack.Peek().KeyLengthBeforeArc;
                continue;
            }

            int pos = frame.ArcCursor;
            var arc = DecodeArc(ref pos);
            stack.Pop();
            stack.Push(new Frame(frame.NodeAddr, arc.IsLastArc ? -1 : pos, frame.AccumulatedOutput, frame.KeyLengthBeforeArc));

            long childOutput = frame.AccumulatedOutput + arc.Output;
            keyLen = frame.KeyLengthBeforeArc;
            EnsureKeyCapacity(ref keyBuf, keyLen + 1);
            keyBuf[keyLen] = arc.Label;
            int childKeyLen = keyLen + 1;

            if (arc.HasTarget)
            {
                long childAddr = arc.Target;
                if (TryGetFinalOutput(childAddr, out long childFinal))
                {
                    if (ContainsBytes(keyBuf.AsSpan(0, childKeyLen), needle))
                        yield return childOutput + childFinal;
                }

                int firstChildArc = FirstRealArcOffset(childAddr);
                if (firstChildArc >= 0)
                {
                    stack.Push(new Frame(childAddr, firstChildArc, childOutput, childKeyLen));
                    keyLen = childKeyLen;
                }
            }
            else
            {
                if (ContainsBytes(keyBuf.AsSpan(0, childKeyLen), needle))
                    yield return childOutput;
            }
        }
    }

    private static bool ContainsBytes(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0) return true;
        return haystack.IndexOf(needle) >= 0;
    }


    /// <summary>
    /// Reads the final output of the node at <paramref name="nodeAddr"/>, if it is final.
    /// Returns false when the node is non-final.
    /// </summary>
    private bool TryGetFinalOutput(long nodeAddr, out long finalOutput)
    {
        finalOutput = 0;
        if (nodeAddr < 0 || nodeAddr >= _nodes.Length) return false;
        int pos = (int)nodeAddr;
        var span = _nodes.AsSpan();
        byte flags = span[pos];
        byte label = span[pos + 1];

        // Final-only node: single virtual arc with label 0x00, isLastArc set.
        if ((flags & FlagIsLastArc) != 0 && label == 0x00 && (flags & FlagHasTarget) == 0)
        {
            if ((flags & FlagIsFinal) == 0) return false;
            if ((flags & FlagHasOutput) != 0)
            {
                int p = pos + 2;
                finalOutput = FstBuilder.ReadVarInt(span, ref p);
            }
            return true;
        }

        // Virtual final-output arc (label 0xFF) prepended when node is final with non-zero final output.
        if (label == 0xFF && (flags & FlagIsFinal) != 0 && (flags & FlagHasOutput) != 0)
        {
            int p = pos + 2;
            finalOutput = FstBuilder.ReadVarInt(span, ref p);
            return true;
        }

        // Final flag on first real arc indicates final node with zero final output.
        if ((flags & FlagIsFinal) != 0)
        {
            finalOutput = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the offset of the first <em>real</em> arc of the node, or -1 when the node has
    /// no real arcs (final-only node). Skips the virtual final-output arc when present.
    /// </summary>
    private int FirstRealArcOffset(long nodeAddr)
    {
        if (nodeAddr < 0 || nodeAddr >= _nodes.Length) return -1;
        int pos = (int)nodeAddr;
        var span = _nodes.AsSpan();
        byte flags = span[pos];
        byte label = span[pos + 1];

        // Final-only node: no real arcs.
        if ((flags & FlagIsLastArc) != 0 && label == 0x00 && (flags & FlagHasTarget) == 0)
            return -1;

        // Skip virtual final-output arc.
        if (label == 0xFF && (flags & FlagIsFinal) != 0 && (flags & FlagHasOutput) != 0)
        {
            int p = pos + 2;
            _ = FstBuilder.ReadVarInt(span, ref p);
            return p;
        }

        return pos;
    }

    /// <summary>
    /// Scans the arcs of the node at <paramref name="nodeAddr"/> for <paramref name="label"/>.
    /// On match, returns the arc's target (or <see cref="NoAddress"/> when inline-accept) and output.
    /// </summary>
    private bool TryFollowArc(long nodeAddr, byte label,
        out long target, out long output, out bool isFinal, out bool isLastArc)
    {
        target = NoAddress;
        output = 0;
        isFinal = false;
        isLastArc = false;

        int pos = FirstRealArcOffset(nodeAddr);
        if (pos < 0) return false;

        while (pos < _nodes.Length)
        {
            var arc = DecodeArc(ref pos);
            if (arc.Label == label)
            {
                target = arc.HasTarget ? arc.Target : NoAddress;
                output = arc.Output;
                isFinal = arc.IsFinal;
                isLastArc = arc.IsLastArc;
                return true;
            }
            if (arc.IsLastArc) return false;
        }
        return false;
    }

    private Arc DecodeArc(ref int pos)
    {
        var span = _nodes.AsSpan();
        byte flags = span[pos++];
        byte label = span[pos++];
        long target = NoAddress;
        long output = 0;
        if ((flags & FlagHasTarget) != 0)
            target = FstBuilder.ReadVarInt(span, ref pos);
        if ((flags & FlagHasOutput) != 0)
            output = FstBuilder.ReadVarInt(span, ref pos);
        return new Arc(label, target, output,
            (flags & FlagIsFinal) != 0,
            (flags & FlagIsLastArc) != 0,
            (flags & FlagHasOutput) != 0,
            (flags & FlagHasTarget) != 0);
    }

    private static void EnsureKeyCapacity(ref byte[] keyBuf, int required)
    {
        if (keyBuf.Length >= required) return;
        int newCap = keyBuf.Length * 2;
        while (newCap < required) newCap *= 2;
        var grown = new byte[newCap];
        Buffer.BlockCopy(keyBuf, 0, grown, 0, keyBuf.Length);
        keyBuf = grown;
    }

    private readonly record struct Arc(
        byte Label, long Target, long Output,
        bool IsFinal, bool IsLastArc, bool HasOutput, bool HasTarget);

    private readonly record struct Frame(long NodeAddr, int ArcCursor, long AccumulatedOutput, int KeyLengthBeforeArc);

    private readonly record struct IntersectFrame(
        long NodeAddr, int ArcCursor, long AccumulatedOutput, int AutomatonState, int KeyLengthBeforeArc);
}
