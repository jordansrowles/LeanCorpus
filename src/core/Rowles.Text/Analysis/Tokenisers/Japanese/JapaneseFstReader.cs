using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

#if ROWLES_TEXT
internal sealed class JapaneseFstReader
{
    private const long NoAddress = -1;
    private const byte IsFinal = 0x80;
    private const byte IsLastArc = 0x40;
    private const byte HasOutput = 0x20;
    private const byte HasTarget = 0x10;
    private readonly byte[] _nodes;
    private readonly long _rootAddress;
    internal long Count { get; }

    private JapaneseFstReader(byte[] nodes, long rootAddress, long count)
    { _nodes = nodes; _rootAddress = rootAddress; Count = count; }

    internal static JapaneseFstReader Open(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 4 || !blob[..4].SequenceEqual("FST1"u8))
            throw new InvalidDataException("Invalid Japanese dictionary FST.");
        int position = 4;
        if (!TryReadVarInt(blob, ref position, blob.Length, out long root) ||
            !TryReadVarInt(blob, ref position, blob.Length, out long count) || count < 0)
            throw new InvalidDataException("Japanese dictionary FST header is invalid.");
        return new JapaneseFstReader(blob[position..].ToArray(), root, count);
    }

    internal PrefixCursor CreatePrefixCursor() => new(this);

    private bool TryGetFinalOutput(long nodeAddress, out long output)
    {
        output = 0;
        if (nodeAddress < 0 || nodeAddress + 1 >= _nodes.Length) return false;
        int position = (int)nodeAddress;
        byte flags = _nodes[position];
        byte label = _nodes[position + 1];
        if ((flags & IsLastArc) != 0 && label == 0 && (flags & HasTarget) == 0)
        {
            if ((flags & IsFinal) == 0) return false;
            position += 2;
            return (flags & HasOutput) == 0 || TryReadVarInt(_nodes, ref position, _nodes.Length, out output);
        }
        if (label == 0xFF && (flags & IsFinal) != 0 && (flags & HasOutput) != 0)
        {
            position += 2;
            return TryReadVarInt(_nodes, ref position, _nodes.Length, out output);
        }
        return (flags & IsFinal) != 0;
    }

    private int FirstRealArc(long nodeAddress)
    {
        if (nodeAddress < 0 || nodeAddress + 1 >= _nodes.Length) return -1;
        int position = (int)nodeAddress;
        byte flags = _nodes[position];
        byte label = _nodes[position + 1];
        if ((flags & IsLastArc) != 0 && label == 0 && (flags & HasTarget) == 0) return -1;
        if (label == 0xFF && (flags & IsFinal) != 0 && (flags & HasOutput) != 0)
        {
            position += 2;
            return TryReadVarInt(_nodes, ref position, _nodes.Length, out _) ? position : -1;
        }
        return position;
    }

    private bool TryFollowArc(long nodeAddress, byte wanted, out long target, out long output)
    {
        target = NoAddress; output = 0;
        int position = FirstRealArc(nodeAddress);
        while (position >= 0 && position < _nodes.Length)
        {
            byte flags = _nodes[position++];
            if (position >= _nodes.Length) return false;
            byte label = _nodes[position++];
            long candidateTarget = NoAddress, candidateOutput = 0;
            if ((flags & HasTarget) != 0 && !TryReadVarInt(_nodes, ref position, _nodes.Length, out candidateTarget)) return false;
            if ((flags & HasOutput) != 0 && !TryReadVarInt(_nodes, ref position, _nodes.Length, out candidateOutput)) return false;
            if (label == wanted) { target = candidateTarget; output = candidateOutput; return true; }
            if ((flags & IsLastArc) != 0) return false;
        }
        return false;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> data, ref int position, int end, out long value)
    {
        ulong result = 0; value = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            if (position >= end) return false;
            byte item = data[position++]; result |= (ulong)(item & 0x7F) << shift;
            if ((item & 0x80) == 0) { value = unchecked((long)result); return true; }
        }
        return false;
    }

    internal struct PrefixCursor
    {
        private readonly JapaneseFstReader _reader;
        private long _nodeAddress;
        private long _output;
        private bool _canAdvance;
        private bool _inlineFinal;

        internal PrefixCursor(JapaneseFstReader reader)
        { _reader = reader; _nodeAddress = reader._rootAddress; _output = 0; _canAdvance = reader.Count != 0 && reader._rootAddress != NoAddress; _inlineFinal = false; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Move(byte label)
        {
            if (!_canAdvance || !_reader.TryFollowArc(_nodeAddress, label, out long target, out long output))
            { _canAdvance = false; _inlineFinal = false; return false; }
            _output += output;
            if (target == NoAddress) { _canAdvance = false; _inlineFinal = true; }
            else { _nodeAddress = target; _inlineFinal = false; }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetOutput(out long output)
        {
            if (_inlineFinal) { output = _output; return true; }
            if (_canAdvance && _reader.TryGetFinalOutput(_nodeAddress, out long finalOutput))
            { output = _output + finalOutput; return true; }
            output = 0; return false;
        }
    }
}
#endif
