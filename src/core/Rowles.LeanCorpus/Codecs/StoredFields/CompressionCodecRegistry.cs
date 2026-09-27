namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Provides registration and lookup for stored-field compression codecs.
/// </summary>
public static class CompressionCodecRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<byte, IFieldCompressionCodec> Codecs = new();
    private static bool _indexOpened;
    private static int _generation;

    static CompressionCodecRegistry()
    {
        Codecs.Add((byte)FieldCompressionPolicy.None, new NoneCompressionCodec());
        Codecs.Add((byte)FieldCompressionPolicy.Deflate, new DeflateCompressionCodec());
        Codecs.Add((byte)FieldCompressionPolicy.Brotli, new BrotliCompressionCodec());
    }

    /// <summary>
    /// Registers a stored-field compression codec during application bootstrap.
    /// Registration is copied into each immutable codec catalogue and is rejected
    /// once an index has opened.
    /// </summary>
    /// <param name="codec">The codec to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codec"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when its policy byte is already registered or an index has opened.</exception>
    public static void Register(IFieldCompressionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        lock (SyncRoot)
        {
            if (Codecs.ContainsKey(codec.PolicyByte))
                throw new InvalidOperationException(
                    $"A stored-field compression codec is already registered for policy byte {codec.PolicyByte}. " +
                    "Use Replace before opening an index if replacement is intentional.");
            if (_indexOpened)
                throw new InvalidOperationException(
                    "Stored-field compression registration is closed because an index has already opened.");
            Codecs.Add(codec.PolicyByte, codec);
            _generation++;
        }
    }

    /// <summary>Explicitly replaces a bootstrap codec before any index opens.</summary>
    /// <param name="codec">The replacement codec.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codec"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no codec exists for its policy byte or an index has opened.</exception>
    public static void Replace(IFieldCompressionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        lock (SyncRoot)
        {
            if (_indexOpened)
                throw new InvalidOperationException(
                    "Stored-field compression replacement is closed because an index has already opened.");
            if (!Codecs.ContainsKey(codec.PolicyByte))
                throw new InvalidOperationException(
                    $"No stored-field compression codec is registered for policy byte {codec.PolicyByte} to replace.");
            Codecs[codec.PolicyByte] = codec;
            _generation++;
        }
    }

    internal static IFieldCompressionCodec[] CaptureForCatalog()
        => CaptureForCatalog(out _);

    internal static IFieldCompressionCodec[] CaptureForCatalog(out int generation)
    {
        lock (SyncRoot)
        {
            generation = _generation;
            return Codecs.Values.ToArray();
        }
    }

    internal static void MarkIndexOpened()
    {
        IFieldCompressionCodec[] snapshot;
        lock (SyncRoot)
        {
            _indexOpened = true;
            snapshot = Codecs.Values.ToArray();
        }
        Rowles.LeanCorpus.Codecs.CodecKit.CodecCatalog.FreezeDefault(snapshot);
    }

    /// <summary>
    /// Attempts to retrieve a registered stored-field compression codec.
    /// </summary>
    /// <param name="policyByte">The persisted compression policy byte.</param>
    /// <param name="codec">The registered codec, when one is available.</param>
    /// <returns><c>true</c> when a codec is registered; otherwise, <c>false</c>.</returns>
    public static bool TryGet(byte policyByte, out IFieldCompressionCodec? codec)
    {
        lock (SyncRoot)
            return Codecs.TryGetValue(policyByte, out codec);
    }

    /// <summary>
    /// Retrieves a registered stored-field compression codec.
    /// </summary>
    /// <param name="policy">The compression policy.</param>
    /// <returns>The registered codec for <paramref name="policy"/>.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no codec has been registered for <paramref name="policy"/>.</exception>
    public static IFieldCompressionCodec Get(FieldCompressionPolicy policy)
    {
        byte policyByte = (byte)policy;
        if (TryGet(policyByte, out var codec) && codec is not null)
            return codec;

        throw new InvalidOperationException(
            $"No stored-field compression codec is registered for policy '{policy}' ({policyByte}). " +
            "Add the matching LeanCorpus compression package or register a codec before reading or writing this index.");
    }
}
