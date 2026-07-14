using System;
using System.Buffers;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Per-operation mutable state for codec decode/encode operations.
/// Tracks nesting depth, diagnostic path, byte offsets, delimited scopes, and scratch buffers.
/// Created per top-level codec call; never shared across concurrent operations.
/// </summary>
public sealed class CodecContext
{
    private readonly CodecOptions _options;
    private readonly CodecRegistry _registry;
    private int _depth;
    private readonly List<string> _pathSegments = new();
    private long _byteOffsetBase;

    // Delimited scope stack
    private readonly Stack<ScopeInfo> _scopes = new();

    public CodecContext(CodecOptions options, CodecRegistry registry)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>Current codec options.</summary>
    public CodecOptions Options => _options;

    /// <summary>Current codec registry.</summary>
    public CodecRegistry Registry => _registry;

    /// <summary>Current nesting depth.</summary>
    public int Depth => _depth;
    private string? _cachedPath;

    /// <summary>Current diagnostic path, computed lazily.</summary>
    public string CurrentPath => _cachedPath ??= string.Join(" ", _pathSegments);

    /// <summary>Current byte offset base (for decompression sub-stream tracking).</summary>
    public long ByteOffsetBase => _byteOffsetBase;

    /// <summary>Whether we are inside a delimited scope.</summary>
    public bool InScope => _scopes.Count > 0;

    /// <summary>Remaining bytes in the current delimited scope, or -1 if not in a scope.</summary>
    public long RemainingInScope => _scopes.Count > 0 ? _scopes.Peek().Remaining : -1;

    #region Depth tracking

    /// <summary>
    /// Increments nesting depth. Returns a disposable guard that decrements on dispose.
    /// Throws <see cref="LimitExceededException"/> if MaxNestingDepth is exceeded.
    /// </summary>
    public IDisposable PushDepth()
    {
        _depth++;
        if (_depth > _options.MaxNestingDepth)
        {
            _depth--;
            throw new LimitExceededException(
                CodecErrorCode.DepthExceeded, 0, CurrentPath,
                "MaxNestingDepth", _depth + 1, _options.MaxNestingDepth);
        }
        return new DepthGuard(this);
    }

    private void PopDepth()
    {
        if (_depth > 0) _depth--;
    }

    #endregion

    #region Path tracking

    /// <summary>
    /// Pushes a path segment. Returns a disposable guard that pops on dispose.
    /// </summary>
    public IDisposable PushPath(string segment)
    {
        _cachedPath = null;
        _pathSegments.Add(segment);
        return new PathGuard(this);
    }

    private void PopPath()
    {
        if (_pathSegments.Count > 0)
            _pathSegments.RemoveAt(_pathSegments.Count - 1);
    }

    #endregion

    #region Checkpoint / Rewind

    /// <summary>
    /// Captures the current reader position alongside the sequence it belongs to.
    /// Both are required for a correct rewind: the position is only valid within the
    /// sequence it was captured from, so nested codecs that narrow <c>reader.Sequence</c>
    /// cannot invalidate an outer checkpoint.
    /// </summary>
    public CodecCheckpoint Checkpoint(ref SequenceReader<byte> reader)
        => new CodecCheckpoint(reader.Position, reader.Sequence);

    /// <summary>
    /// Rewinds the reader to a previously captured checkpoint.
    /// The reader is reconstructed from the sequence stored in the checkpoint so that
    /// nested rewinds (which may have narrowed <c>reader.Sequence</c>) do not affect
    /// the restoration of outer checkpoints.
    /// </summary>
    public void Rewind(ref SequenceReader<byte> reader, CodecCheckpoint checkpoint)
    {
        reader = new SequenceReader<byte>(checkpoint.Sequence.Slice(checkpoint.Position));
    }

    #endregion

    #region Delimited scopes

    /// <summary>
    /// Enters a delimited scope of the given byte length.
    /// Returns a disposable guard that exits the scope on dispose.
    /// </summary>
    public IDisposable EnterScope(long length)
    {
        _scopes.Push(new ScopeInfo(length));
        return new ScopeGuard(this);
    }

    /// <summary>
    /// Consumes bytes from the current scope.
    /// </summary>
    public void ConsumeScope(long bytes)
    {
        if (_scopes.Count > 0)
        {
            var scope = _scopes.Peek();
            scope.Remaining -= bytes;
        }
    }

    private void ExitScope()
    {
        if (_scopes.Count > 0) _scopes.Pop();
    }

    #endregion

    #region Scratch buffers

    /// <summary>
    /// Rents a scratch buffer for transactional encode staging.
    /// </summary>
    internal IScratchBuffer RentScratchBuffer(int initialCapacity = 4096)
    {
        return new ArrayPoolScratchBuffer(initialCapacity, _options.MaxScratchBufferBytes);
    }

    /// <summary>
    /// Returns a scratch buffer (disposes it).
    /// </summary>
    internal void ReturnScratchBuffer(IScratchBuffer buffer)
    {
        buffer.Dispose();
    }

    #endregion

    #region Offset tracking

    /// <summary>
    /// Computes the current byte offset relative to the offset base.
    /// </summary>
    public long GetByteOffset(ref SequenceReader<byte> reader) => _byteOffsetBase + reader.Consumed;

    /// <summary>
    /// Sets the byte offset base for decompression sub-streams.
    /// Returns a disposable guard that restores the old base.
    /// </summary>
    public IDisposable SetByteOffsetBase(long newBase)
    {
        long oldBase = _byteOffsetBase;
        _byteOffsetBase = newBase;
        return new OffsetBaseGuard(this, oldBase);
    }

    #endregion

    #region Scope guards

    private sealed class DepthGuard : IDisposable
    {
        private CodecContext? _ctx;
        public DepthGuard(CodecContext ctx) => _ctx = ctx;
        public void Dispose()
        {
            var ctx = _ctx;
            if (ctx != null) { _ctx = null; ctx.PopDepth(); }
        }
    }

    private sealed class PathGuard : IDisposable
    {
        private CodecContext? _ctx;
        public PathGuard(CodecContext ctx) => _ctx = ctx;
        public void Dispose()
        {
            var ctx = _ctx;
            if (ctx != null) { _ctx = null; ctx.PopPath(); }
        }
    }

    private sealed class ScopeGuard : IDisposable
    {
        private CodecContext? _ctx;
        public ScopeGuard(CodecContext ctx) => _ctx = ctx;
        public void Dispose()
        {
            var ctx = _ctx;
            if (ctx != null) { _ctx = null; ctx.ExitScope(); }
        }
    }

    private sealed class OffsetBaseGuard : IDisposable
    {
        private CodecContext? _ctx;
        private readonly long _oldBase;
        public OffsetBaseGuard(CodecContext ctx, long oldBase) { _ctx = ctx; _oldBase = oldBase; }
        public void Dispose()
        {
            var ctx = _ctx;
            if (ctx != null) { _ctx = null; ctx._byteOffsetBase = _oldBase; }
        }
    }

    private sealed class ScopeInfo
    {
        public long Remaining;
        public ScopeInfo(long length) => Remaining = length;
    }

    #endregion
}

/// <summary>
/// A saved reader position, bundled with the sequence it was captured from.
/// Passing the sequence through ensures that nested rewinds — which may narrow
/// <c>reader.Sequence</c> to a sub-slice — cannot invalidate an outer checkpoint.
/// </summary>
public readonly struct CodecCheckpoint
{
    internal readonly SequencePosition Position;
    internal readonly ReadOnlySequence<byte> Sequence;

    public CodecCheckpoint(SequencePosition position, ReadOnlySequence<byte> sequence)
    {
        Position = position;
        Sequence = sequence;
    }
}
