namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Provides registration and lookup for stored-field compression codecs.
/// </summary>
public static class CompressionCodecRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<byte, IFieldCompressionCodec> Codecs = new();

    static CompressionCodecRegistry()
    {
        Register(new NoneCompressionCodec());
        Register(new DeflateCompressionCodec());
        Register(new BrotliCompressionCodec());
    }

    /// <summary>
    /// Registers a stored-field compression codec.
    /// </summary>
    /// <param name="codec">The codec to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="codec"/> is <c>null</c>.</exception>
    public static void Register(IFieldCompressionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);

        lock (SyncRoot)
            Codecs[codec.PolicyByte] = codec;
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
