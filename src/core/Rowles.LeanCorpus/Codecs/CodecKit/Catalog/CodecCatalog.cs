using System.Collections.ObjectModel;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// An immutable catalogue of persistent codec families and physical file roles.
/// </summary>
public sealed class CodecCatalog
{
    private readonly Dictionary<string, CodecFamilyDescriptor> _familiesById;
    private readonly Dictionary<string, CodecFileDescriptor> _filesById;
    private readonly ReadOnlyCollection<CodecFamilyDescriptor> _families;
    private readonly ReadOnlyCollection<CodecFileDescriptor> _files;

    internal CodecCatalog(
        CodecFamilyDescriptor[] families,
        CodecFileDescriptor[] files)
    {
        _families = Array.AsReadOnly(families);
        _files = Array.AsReadOnly(files);
        _familiesById = families.ToDictionary(static family => family.FamilyId, StringComparer.Ordinal);
        _filesById = files.ToDictionary(static file => file.FormatId, StringComparer.Ordinal);
    }

    /// <summary>Gets the default catalogue containing every built-in persistent format.</summary>
    public static CodecCatalog Default { get; } = new CodecCatalogBuilder()
        .AddBuiltIns()
        .Build();

    /// <summary>Gets all registered codec families in registration order.</summary>
    public IReadOnlyList<CodecFamilyDescriptor> Families => _families;

    /// <summary>Gets all registered file roles in family and registration order.</summary>
    public IReadOnlyList<CodecFileDescriptor> Files => _files;

    /// <summary>Gets a family by its stable identifier.</summary>
    /// <exception cref="KeyNotFoundException">The family is not registered.</exception>
    public CodecFamilyDescriptor GetFamily(string familyId)
    {
        ArgumentNullException.ThrowIfNull(familyId);
        return _familiesById.TryGetValue(familyId, out var family)
            ? family
            : throw new KeyNotFoundException($"Codec family '{familyId}' is not registered.");
    }

    /// <summary>Gets a file role by its stable format identifier.</summary>
    /// <exception cref="KeyNotFoundException">The file role is not registered.</exception>
    public CodecFileDescriptor GetFile(string formatId)
    {
        ArgumentNullException.ThrowIfNull(formatId);
        return _filesById.TryGetValue(formatId, out var file)
            ? file
            : throw new KeyNotFoundException($"Codec format '{formatId}' is not registered.");
    }

    /// <summary>Tries to get a family by its stable identifier.</summary>
    public bool TryGetFamily(string familyId, out CodecFamilyDescriptor? family)
    {
        ArgumentNullException.ThrowIfNull(familyId);
        return _familiesById.TryGetValue(familyId, out family);
    }

    /// <summary>Tries to get a file role by its stable format identifier.</summary>
    public bool TryGetFile(string formatId, out CodecFileDescriptor? file)
    {
        ArgumentNullException.ThrowIfNull(formatId);
        return _filesById.TryGetValue(formatId, out file);
    }

    /// <summary>Tries to resolve a logical file name to its registered file role.</summary>
    public bool TryMatchFile(string fileName, out CodecFileDescriptor? file)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        foreach (var candidate in _files)
        {
            if (candidate.FileMatcher.IsMatch(fileName))
            {
                file = candidate;
                return true;
            }
        }

        file = null;
        return false;
    }

    /// <summary>Tries to resolve a temporary logical file name to its owning registered file role.</summary>
    public bool TryMatchTemporaryFile(string fileName, out CodecFileDescriptor? file)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        foreach (var candidate in _files)
        {
            foreach (var matcher in candidate.TemporaryFileMatchers)
            {
                if (matcher.IsMatch(fileName))
                {
                    file = candidate;
                    return true;
                }
            }
        }

        file = null;
        return false;
    }
}
