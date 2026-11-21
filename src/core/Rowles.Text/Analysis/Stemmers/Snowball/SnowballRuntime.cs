// Ported from the Snowball 3.2.0 C# runtime (csharp/Snowball/Stemmer.cs).
//
// Copyright (c) 2001, Dr Martin Porter
// Copyright (c) 2002, Richard Boulton
// Copyright (c) 2015, Cesar Souza
// Copyright (c) 2018-2026, Olly Betts
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
//    1. Redistributions of source code must retain the above copyright notice,
//       this list of conditions and the following disclaimer.
//    2. Redistributions in binary form must reproduce the above copyright
//       notice, this list of conditions and the following disclaimer in the
//       documentation and/or other materials provided with the distribution.
//    3. Neither the name of the Snowball project nor the names of its contributors
//       may be used to endorse or promote products derived from this software
//       without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

/// <summary>
/// The Snowball string-processing environment over a reusable character buffer. Generated algorithm
/// classes derive from this and call its operations exactly as the reference runtime's
/// <c>StringBuilder</c>-based <c>Stemmer</c> class, so their bodies stay unchanged.
/// </summary>
/// <remarks>
/// Each operation is a line-by-line port of the reference runtime; only the storage differs. An
/// instance holds per-word state and is never shared between threads: <see cref="SnowballPort"/>
/// owns one instance per algorithm per thread. The member names follow the reference runtime so
/// the generated code compiles without renaming.
/// </remarks>
[SuppressMessage("Naming", "IDE1006:Naming Styles", Justification = "Names match the Snowball runtime that generated code calls.")]
[SuppressMessage("Major Code Smell", "S100:Methods and properties should be named in PascalCase", Justification = "Names match the Snowball runtime that generated code calls.")]
[SuppressMessage("Minor Code Smell", "S2357:Fields should be private", Justification = "Generated algorithms read and write the Snowball cursor fields directly.")]
internal abstract class SnowballRuntime
{
    // Buffers above this size are released after a word so one very long token does not pin memory.
    private const int RetainedCapacity = 1024;
    private const int InitialCapacity = 64;

    private char[] _buffer = new char[InitialCapacity];
    private int _length;

    /// <summary>Current position.</summary>
    protected int cursor;

    /// <summary>End of the region being processed.</summary>
    protected int limit;

    /// <summary>Start of the region being processed backwards.</summary>
    protected int limit_backward;

    /// <summary>Start of the current slice.</summary>
    protected int bra;

    /// <summary>End of the current slice.</summary>
    protected int ket;

    /// <summary>The length of the word being stemmed (the reference runtime's <c>current.Length</c>).</summary>
    protected int CurrentLength => _length;

    /// <summary>The stemmed word after <see cref="Run"/>.</summary>
    internal ReadOnlySpan<char> Result => _buffer.AsSpan(0, _length);

    /// <summary>Loads <paramref name="word"/> and resets all runtime and algorithm state.</summary>
    internal void Load(ReadOnlySpan<char> word)
    {
        // The previous word's contents are discarded, so nothing is copied when the buffer changes.
        _length = 0;
        if (_buffer.Length > RetainedCapacity)
            _buffer = new char[InitialCapacity];
        EnsureCapacity(word.Length);
        word.CopyTo(_buffer);
        _length = word.Length;

        // As the reference setBufferContents.
        limit_backward = bra = ket = cursor = 0;
        limit = _length;
        ResetState();
    }

    /// <summary>Runs the algorithm over the loaded word.</summary>
    internal void Run() => stem();

    /// <summary>Clears algorithm state kept in instance fields, so every word starts afresh.</summary>
    protected virtual void ResetState()
    {
    }

    /// <summary>The generated algorithm.</summary>
    protected abstract bool stem();

    /// <summary>Tests whether the current character is in the group <paramref name="s"/>, moving forward.</summary>
    protected int in_grouping(string s, int min, int max, bool repeat)
    {
        do
        {
            if (cursor >= limit)
                return -1;

            char ch = _buffer[cursor];
            if (ch > max || ch < min)
                return 1;

            if (!s.Contains(ch))
                return 1;

            cursor++;
        }
        while (repeat);

        return 0;
    }

    /// <summary>Tests whether the previous character is in the group <paramref name="s"/>, moving backward.</summary>
    protected int in_grouping_b(string s, int min, int max, bool repeat)
    {
        do
        {
            if (cursor <= limit_backward)
                return -1;

            char ch = _buffer[cursor - 1];
            if (ch > max || ch < min)
                return 1;

            if (!s.Contains(ch))
                return 1;

            cursor--;
        }
        while (repeat);

        return 0;
    }

    /// <summary>Tests whether the current character is outside the group <paramref name="s"/>, moving forward.</summary>
    protected int out_grouping(string s, int min, int max, bool repeat)
    {
        do
        {
            if (cursor >= limit)
                return -1;

            char ch = _buffer[cursor];
            if (ch > max || ch < min)
            {
                cursor++;
                continue;
            }

            if (!s.Contains(ch))
            {
                cursor++;
                continue;
            }

            return 1;
        }
        while (repeat);

        return 0;
    }

    /// <summary>Tests whether the previous character is outside the group <paramref name="s"/>, moving backward.</summary>
    protected int out_grouping_b(string s, int min, int max, bool repeat)
    {
        do
        {
            if (cursor <= limit_backward)
                return -1;

            char ch = _buffer[cursor - 1];
            if (ch > max || ch < min)
            {
                cursor--;
                continue;
            }

            if (!s.Contains(ch))
            {
                cursor--;
                continue;
            }

            return 1;
        }
        while (repeat);

        return 0;
    }

    /// <summary>Matches <paramref name="s"/> at the cursor and moves past it.</summary>
    protected bool eq_s(string s)
    {
        if (limit - cursor < s.Length)
            return false;

        for (int i = 0; i != s.Length; i++)
        {
            if (_buffer[cursor + i] != s[i])
                return false;
        }

        cursor += s.Length;
        return true;
    }

    /// <summary>Matches <paramref name="s"/> before the cursor and moves back over it.</summary>
    protected bool eq_s_b(string s)
    {
        if (cursor - limit_backward < s.Length)
            return false;

        for (int i = 0; i != s.Length; i++)
        {
            if (_buffer[cursor - s.Length + i] != s[i])
                return false;
        }

        cursor -= s.Length;
        return true;
    }

    /// <summary>Finds the longest entry of <paramref name="v"/> at the cursor, moving forward.</summary>
    protected int find_among(SnowballAmong[] v)
    {
        int i = 0;
        int j = v.Length;

        int c = cursor;
        int l = limit;

        int common_i = 0;
        int common_j = 0;

        bool first_key_inspected = false;

        while (true)
        {
            int k = i + ((j - i) >> 1);
            int diff = 0;
            int common = common_i < common_j ? common_i : common_j;

            SnowballAmong w = v[k];

            for (int i2 = common; i2 < w.SearchString.Length; i2++)
            {
                if (c + common == l)
                {
                    diff = -1;
                    break;
                }

                diff = _buffer[c + common] - w.SearchString[i2];

                if (diff != 0)
                    break;

                common++;
            }

            if (diff < 0)
            {
                j = k;
                common_j = common;
            }
            else
            {
                i = k;
                common_i = common;
            }

            if (j - i <= 1)
            {
                if (i > 0)
                    break;

                if (j == i)
                    break;

                if (first_key_inspected)
                    break;

                first_key_inspected = true;
            }
        }

        while (true)
        {
            SnowballAmong w = v[i];

            if (common_i >= w.SearchString.Length)
            {
                // The pinned algorithms have no among conditions (the transform rejects them).
                cursor = c + w.SearchString.Length;
                return w.Result;
            }

            i = w.MatchIndex;

            if (i < 0)
                return 0;
        }
    }

    /// <summary>Finds the longest entry of <paramref name="v"/> ending at the cursor, moving backward.</summary>
    protected int find_among_b(SnowballAmong[] v)
    {
        int i = 0;
        int j = v.Length;

        int c = cursor;
        int lb = limit_backward;

        int common_i = 0;
        int common_j = 0;

        bool first_key_inspected = false;

        while (true)
        {
            int k = i + ((j - i) >> 1);
            int diff = 0;
            int common = common_i < common_j ? common_i : common_j;
            SnowballAmong w = v[k];

            for (int i2 = w.SearchString.Length - 1 - common; i2 >= 0; i2--)
            {
                if (c - common == lb)
                {
                    diff = -1;
                    break;
                }

                diff = _buffer[c - 1 - common] - w.SearchString[i2];

                if (diff != 0)
                    break;

                common++;
            }

            if (diff < 0)
            {
                j = k;
                common_j = common;
            }
            else
            {
                i = k;
                common_i = common;
            }

            if (j - i <= 1)
            {
                if (i > 0)
                    break;

                if (j == i)
                    break;

                if (first_key_inspected)
                    break;

                first_key_inspected = true;
            }
        }

        while (true)
        {
            SnowballAmong w = v[i];

            if (common_i >= w.SearchString.Length)
            {
                cursor = c - w.SearchString.Length;
                return w.Result;
            }

            i = w.MatchIndex;

            if (i < 0)
                return 0;
        }
    }

    /// <summary>Replaces <c>[c_bra, c_ket)</c> with <paramref name="s"/> and adjusts the limit and cursor.</summary>
    protected int replace_s(int c_bra, int c_ket, ReadOnlySpan<char> s)
    {
        int adjustment = s.Length - (c_ket - c_bra);
        Replace(c_bra, c_ket, s);
        limit += adjustment;
        if (cursor >= c_ket)
            cursor += adjustment;
        else if (cursor > c_bra)
            cursor = c_bra;
        return adjustment;
    }

    /// <summary>Checks that the slice is within the buffer.</summary>
    protected void slice_check()
    {
        Debug.Assert(bra >= 0);
        Debug.Assert(bra <= ket);
        Debug.Assert(ket <= limit);
        Debug.Assert(limit <= _length);
    }

    /// <summary>Replaces the slice with <paramref name="s"/>.</summary>
    protected void slice_from(string s)
    {
        slice_check();
        replace_s(bra, ket, s);
        ket = bra + s.Length;
    }

    /// <summary>Deletes the slice.</summary>
    protected void slice_del() => slice_from(string.Empty);

    /// <summary>Inserts <paramref name="s"/> in place of <c>[c_bra, c_ket)</c>, keeping the slice in step.</summary>
    protected void insert(int c_bra, int c_ket, string s) => insert(c_bra, c_ket, s.AsSpan());

    /// <summary>Inserts the contents of <paramref name="s"/> in place of <c>[c_bra, c_ket)</c>, keeping the slice in step.</summary>
    protected void insert(int c_bra, int c_ket, SnowballSlice s) => insert(c_bra, c_ket, s.Span);

    private void insert(int c_bra, int c_ket, ReadOnlySpan<char> s)
    {
        int adjustment = replace_s(c_bra, c_ket, s);
        if (c_bra <= bra) bra += adjustment;
        if (c_bra <= ket) ket += adjustment;
    }

    /// <summary>Copies the slice into <paramref name="s"/>.</summary>
    protected void slice_to(SnowballSlice s)
    {
        slice_check();
        s.Set(_buffer.AsSpan(bra, ket - bra));
    }

    private void Replace(int start, int end, ReadOnlySpan<char> text)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(end, start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, _length);

        int newLength = _length - (end - start) + text.Length;
        EnsureCapacity(newLength);
        int tail = _length - end;
        if (tail > 0)
            Array.Copy(_buffer, end, _buffer, start + text.Length, tail);
        text.CopyTo(_buffer.AsSpan(start));
        _length = newLength;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _buffer.Length)
            return;
        int capacity = Math.Max(required, _buffer.Length * 2);
        var grown = new char[capacity];
        _buffer.AsSpan(0, _length).CopyTo(grown);
        _buffer = grown;
    }
}

/// <summary>An entry of a Snowball <c>among</c> table. Immutable and shared by all instances.</summary>
internal sealed class SnowballAmong(string searchString, int matchIndex, int result)
{
    /// <summary>The string to match.</summary>
    public string SearchString { get; } = searchString;

    /// <summary>The index of the longest entry that is a prefix (or suffix) of this one, or -1.</summary>
    public int MatchIndex { get; } = matchIndex;

    /// <summary>The value returned when this entry matches.</summary>
    public int Result { get; } = result;
}

/// <summary>
/// A reusable character buffer for a Snowball string variable, replacing the reference
/// runtime's <c>StringBuilder</c> so slices can be saved and reinserted without allocating.
/// </summary>
internal sealed class SnowballSlice
{
    private char[] _chars = new char[16];
    private int _length;

    /// <summary>The current contents.</summary>
    public ReadOnlySpan<char> Span => _chars.AsSpan(0, _length);

    /// <summary>Replaces the contents with <paramref name="value"/>.</summary>
    public void Set(ReadOnlySpan<char> value)
    {
        if (value.Length > _chars.Length)
            _chars = new char[Math.Max(value.Length, _chars.Length * 2)];
        value.CopyTo(_chars);
        _length = value.Length;
    }

    /// <summary>Empties the buffer.</summary>
    public void Clear() => _length = 0;
}
