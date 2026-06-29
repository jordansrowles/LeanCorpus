using System.Buffers;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

internal struct PooledArray<T> : IReadOnlyList<T>, IDisposable
{
    private T[] _array;
    private int _count;
    public PooledArray(T[] a, int c) { _array = a; _count = c; }
    public int Count => _count;
    public T this[int i] => _array[i];
    public void Dispose() { if (_array is not null) { ArrayPool<T>.Shared.Return(_array, RuntimeHelpers.IsReferenceOrContainsReferences<T>()); _array = null!; } }
    public T[] GetArray() => _array;
    public Span<T> AsSpan() => _array.AsSpan(0, _count);
    public Enumerator GetEnumerator() => new(_array, _count);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[] _a; private readonly int _c; private int _i;
        internal Enumerator(T[] a, int c) { _a = a; _c = c; _i = -1; }
        public T Current => _a[_i];
        object IEnumerator.Current => _a[_i]!;
        public bool MoveNext() => ++_i < _c;
        public void Reset() => _i = -1;
        public void Dispose() { }
    }
}
