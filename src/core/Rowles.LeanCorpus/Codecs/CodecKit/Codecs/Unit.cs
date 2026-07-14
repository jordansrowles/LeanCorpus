using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// A type with exactly one value, used for codecs that produce no meaningful result
/// (e.g., <c>Skip</c>, <c>Magic</c>, <c>Padding</c>).
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = default;

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "()";

    public static bool operator ==(Unit left, Unit right) => true;
    public static bool operator !=(Unit left, Unit right) => false;
}
