using System.Collections.ObjectModel;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>
/// An immutable catalogue of persistent codec families and physical file roles.
/// </summary>
public sealed class CodecCatalog
{
    private static readonly object DefaultSyncRoot = new();
    private static CodecCatalog? _frozenDefault;
    private static CodecCatalog? _bootstrapDefault;
    private static int _bootstrapDefaultGeneration = -1;

    private readonly Dictionary<string, CodecFamilyDescriptor> _familiesById;
    private readonly Dictionary<string, CodecFileDescriptor> _filesById;
    private readonly Dictionary<byte, IFieldCompressionCodec> _compressionCodecsByPolicy;
    private readonly ReadOnlyCollection<CodecFamilyDescriptor> _families;
    private readonly ReadOnlyCollection<CodecFileDescriptor> _files;
    private readonly ReadOnlyCollection<IFieldCompressionCodec> _compressionCodecs;

    internal CodecCatalog(
        CodecFamilyDescriptor[] families,
        CodecFileDescriptor[] files,
        IFieldCompressionCodec[] compressionCodecs)
    {
        _families = Array.AsReadOnly(families);
        _files = Array.AsReadOnly(files);
        _compressionCodecs = Array.AsReadOnly(compressionCodecs);
        _familiesById = families.ToDictionary(static family => family.FamilyId, StringComparer.Ordinal);
        _filesById = files.ToDictionary(static file => file.FormatId, StringComparer.Ordinal);
        _compressionCodecsByPolicy = compressionCodecs.ToDictionary(static codec => codec.PolicyByte);
    }

    /// <summary>Gets an immutable snapshot containing the built-in formats and registered bootstrap codecs.</summary>
    public static CodecCatalog Default
    {
        get
        {
            lock (DefaultSyncRoot)
            {
                if (_frozenDefault is not null)
                    return _frozenDefault;

                var compressionCodecs = CompressionCodecRegistry.CaptureForCatalog(out int generation);
                if (_bootstrapDefault is not null && _bootstrapDefaultGeneration == generation)
                    return _bootstrapDefault;

                _bootstrapDefault = new CodecCatalogBuilder()
                    .AddBuiltIns(compressionCodecs)
                    .Build();
                _bootstrapDefaultGeneration = generation;
                return _bootstrapDefault;
            }
        }
    }

    internal static void FreezeDefault(IFieldCompressionCodec[] compressionCodecs)
    {
        var snapshot = new CodecCatalogBuilder()
            .AddBuiltIns(compressionCodecs)
            .Build();
        lock (DefaultSyncRoot)
        {
            _frozenDefault ??= snapshot;
            _bootstrapDefault = _frozenDefault;
        }
    }

    /// <summary>Gets all registered codec families in registration order.</summary>
    public IReadOnlyList<CodecFamilyDescriptor> Families => _families;

    /// <summary>Gets all registered file roles in family and registration order.</summary>
    public IReadOnlyList<CodecFileDescriptor> Files => _files;

    /// <summary>Gets the stored-field compression codecs captured by this immutable catalogue.</summary>
    public IReadOnlyList<IFieldCompressionCodec> CompressionCodecs => _compressionCodecs;

    /// <summary>Gets a stored-field compression codec by its persisted policy byte.</summary>
    /// <exception cref="InvalidOperationException">The policy byte is not registered in this catalogue.</exception>
    public IFieldCompressionCodec GetCompressionCodec(byte policyByte)
        => _compressionCodecsByPolicy.TryGetValue(policyByte, out var codec)
            ? codec
            : throw new InvalidOperationException(
                $"No stored-field compression codec is registered for policy byte {policyByte} in this codec catalogue.");

    /// <summary>Tries to get a stored-field compression codec by its persisted policy byte.</summary>
    public bool TryGetCompressionCodec(byte policyByte, out IFieldCompressionCodec? codec)
        => _compressionCodecsByPolicy.TryGetValue(policyByte, out codec);

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
