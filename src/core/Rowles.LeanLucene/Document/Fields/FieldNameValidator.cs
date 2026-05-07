namespace Rowles.LeanLucene.Document.Fields;

internal static class FieldNameValidator
{
    public static string Validate(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);
        if (name.Length == 0)
            throw new ArgumentException("Field names must not be empty.", parameterName);

        foreach (char c in name)
        {
            if (c == '\0')
                throw new ArgumentException("Field names must not contain the internal term separator.", parameterName);
            if (char.IsControl(c))
                throw new ArgumentException("Field names must not contain control characters.", parameterName);
        }

        return name;
    }
}
