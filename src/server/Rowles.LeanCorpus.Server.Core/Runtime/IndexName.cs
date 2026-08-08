namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Validates customer-visible index names before they are used as registry keys.</summary>
internal static class IndexName
{
    internal static bool IsValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
            return false;

        foreach (char value in name)
        {
            if (!(char.IsAsciiLetterOrDigit(value) || value is '-' or '_'))
                return false;
        }

        return true;
    }
}
