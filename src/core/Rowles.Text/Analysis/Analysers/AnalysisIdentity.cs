namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// An analyser that declares the identity of its output, such as <c>rowles-analysis/de/3.2.0</c>.
/// </summary>
/// <remarks>
/// The identity changes exactly when the analyser's output can change, so terms indexed under one
/// identity are only guaranteed to match queries analysed under the same identity. An index records
/// the identity of every analysed field and refuses a different one (see
/// <c>IndexWriterConfig.AcceptAnalysisIdentityChanges</c>).
/// </remarks>
public interface IAnalysisIdentityProvider
{
    /// <summary>
    /// Gets the identity of this analyser's output, or <see langword="null"/> when the output has no
    /// declared identity (for example, a configuration with custom stop words).
    /// </summary>
    string? AnalysisId { get; }
}

/// <summary>Reads and assigns analysis identities.</summary>
public static class AnalysisIdentity
{
    /// <summary>
    /// The identity recorded for a field whose analyser declares no identity. Two unidentified
    /// analysers are indistinguishable, so changing between them is not detected.
    /// </summary>
    public const string Unidentified = "unidentified";

    /// <summary>The longest identity accepted by <see cref="Assign"/>.</summary>
    public const int MaxLength = 256;

    /// <summary>Gets the declared identity of an analyser, or <see langword="null"/> when it declares none.</summary>
    /// <param name="analyser">The analyser.</param>
    /// <returns>The analyser's identity, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="analyser"/> is <see langword="null"/>.</exception>
    public static string? Of(IAnalyser analyser)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        return (analyser as IAnalysisIdentityProvider)?.AnalysisId;
    }

    /// <summary>
    /// Returns an analyser that behaves exactly like <paramref name="analyser"/> and declares
    /// <paramref name="analysisId"/> as its identity.
    /// </summary>
    /// <param name="analyser">The analyser to identify. Its thread-local copies keep the identity.</param>
    /// <param name="analysisId">
    /// The identity: 1 to <see cref="MaxLength"/> printable, non-space characters, and not
    /// <see cref="Unidentified"/>. Change it whenever the analyser's output can change, for example
    /// <c>myapp/en-products/2</c>.
    /// </param>
    /// <returns>The identified analyser.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="analysisId"/> is not a valid identity.</exception>
    /// <remarks>
    /// Term normalisation for wildcard and range queries (<see cref="ITermNormaliser"/>) is forwarded
    /// only when <paramref name="analyser"/> supports it.
    /// </remarks>
    public static IAnalyser Assign(IAnalyser analyser, string analysisId)
    {
        ArgumentNullException.ThrowIfNull(analyser);
        Validate(analysisId, nameof(analysisId));
        return analyser is ITermNormaliser
            ? new NormalisingIdentifiedAnalyser(analyser, analysisId)
            : new IdentifiedAnalyser(analyser, analysisId);
    }

    /// <summary>Throws when <paramref name="analysisId"/> is not a valid identity.</summary>
    internal static void Validate(string analysisId, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(analysisId, parameterName);
        if (analysisId.Length is 0 or > MaxLength)
            throw new ArgumentException($"An analysis identity must have 1 to {MaxLength} characters.", parameterName);
        if (string.Equals(analysisId, Unidentified, StringComparison.Ordinal))
            throw new ArgumentException($"'{Unidentified}' is reserved for analysers without an identity.", parameterName);
        foreach (char character in analysisId)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
                throw new ArgumentException("An analysis identity must not contain whitespace or control characters.", parameterName);
        }
    }

    private class IdentifiedAnalyser(IAnalyser inner, string analysisId) : IThreadLocalAnalyser, IAnalysisIdentityProvider
    {
        protected IAnalyser Inner { get; } = inner;

        public string AnalysisId { get; } = analysisId;

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) => Inner.Analyse(input, sink);

        public IAnalyser CreateThreadLocalAnalyser()
        {
            if (Inner is not IThreadLocalAnalyser owned)
            {
                throw new InvalidOperationException(
                    $"Analyser '{Inner.GetType().FullName}' does not implement {nameof(IThreadLocalAnalyser)}, so it has no explicit concurrent ownership contract.");
            }

            return Assign(owned.CreateThreadLocalAnalyser(), AnalysisId);
        }
    }

    private sealed class NormalisingIdentifiedAnalyser(IAnalyser inner, string analysisId)
        : IdentifiedAnalyser(inner, analysisId), ITermNormaliser
    {
        public bool TryNormalise(ReadOnlySpan<char> input, out string normalised)
            => ((ITermNormaliser)Inner).TryNormalise(input, out normalised);
    }
}
