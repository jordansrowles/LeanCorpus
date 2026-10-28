namespace Rowles.LeanCorpus.Tests.Architecture.Infrastructure;

internal static class NamespaceSegments
{
    internal static bool Contains(string? @namespace, string segment)
    {
        if (string.IsNullOrEmpty(@namespace))
            return false;

        return @namespace.Split('.').Contains(segment, StringComparer.Ordinal);
    }
}
