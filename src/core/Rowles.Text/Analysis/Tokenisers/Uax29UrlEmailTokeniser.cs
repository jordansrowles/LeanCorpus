namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Compatibility wrapper for the historical URL/email tokeniser name.
/// </summary>
/// <remarks>
/// This type forwards to <see cref="UrlEmailTokeniser"/> and does not implement UAX #29.
/// It is retained for one compatibility window; new code should use
/// <see cref="UrlEmailTokeniser"/>.
/// </remarks>
[Obsolete("This compatibility type does not implement UAX #29. Use UrlEmailTokeniser instead.")]
public sealed class Uax29UrlEmailTokeniser : IThreadLocalSpanTokeniser
{
    /// <summary>Token type emitted for URLs.</summary>
    public const string UrlType = UrlEmailTokeniser.UrlType;
    /// <summary>Token type emitted for email addresses.</summary>
    public const string EmailType = UrlEmailTokeniser.EmailType;
    /// <summary>Token type emitted for hashtags.</summary>
    public const string HashtagType = UrlEmailTokeniser.HashtagType;
    /// <summary>Token type emitted for at-mentions.</summary>
    public const string MentionType = UrlEmailTokeniser.MentionType;

    private readonly UrlEmailTokeniser _implementation;

    /// <summary>Initialises the compatibility wrapper without Thai segmentation.</summary>
    public Uax29UrlEmailTokeniser()
        : this(new UrlEmailTokeniser())
    {
    }

    /// <summary>Initialises the compatibility wrapper with an optional Thai tokeniser.</summary>
    /// <param name="thaiTokeniser">The tokeniser used for Thai runs, or null to use the built-in word heuristics.</param>
    public Uax29UrlEmailTokeniser(ISpanTokeniser? thaiTokeniser)
        : this(new UrlEmailTokeniser(thaiTokeniser))
    {
    }

    private Uax29UrlEmailTokeniser(UrlEmailTokeniser implementation)
    {
        _implementation = implementation;
    }

    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
        => _implementation.Tokenise(input, sink);

    /// <inheritdoc/>
    public ISpanTokeniser CreateThreadLocalTokeniser()
        => new Uax29UrlEmailTokeniser((UrlEmailTokeniser)_implementation.CreateThreadLocalTokeniser());
}
