namespace Rowles.LeanLucene.Document;

/// <summary>A document is a bag of fields. Nothing more.</summary>
public sealed class LeanDocument
{
    private readonly List<IField> _fields = [];

    /// <summary>All fields belonging to this document.</summary>
    public IReadOnlyList<IField> Fields => _fields;

    /// <summary>Gets a field by index.</summary>
    public IField this[int index] => _fields[index];

    /// <summary>Adds a field to the document.</summary>
    public void Add(IField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _fields.Add(field);
    }

    /// <summary>Returns the first field matching <paramref name="name"/>, or <c>null</c> if none found.</summary>
    public IField? GetField(string name)
    {
        for (int i = 0; i < _fields.Count; i++)
        {
            if (string.Equals(_fields[i].Name, name, StringComparison.Ordinal))
                return _fields[i];
        }

        return null;
    }

    /// <summary>Returns all fields matching <paramref name="name"/>.</summary>
    public IReadOnlyList<IField> GetFields(string name)
    {
        List<IField>? matches = null;

        for (int i = 0; i < _fields.Count; i++)
        {
            if (string.Equals(_fields[i].Name, name, StringComparison.Ordinal))
                (matches ??= []).Add(_fields[i]);
        }

        return matches ?? (IReadOnlyList<IField>)[];
    }
}
