namespace Rowles.LeanCorpus;

/// <summary>Represents an explicitly configured default, including a configured null value.</summary>
internal readonly record struct DefaultOverride<T>(bool IsSet, T Value)
{
    internal static DefaultOverride<T> Unset => new(false, default!);

    internal static DefaultOverride<T> Set(T value) => new(true, value);
}
