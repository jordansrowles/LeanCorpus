using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.DevOps.Snowball;

/// <summary>
/// The committed manifest of default analysis identities, <c>analysis-versions.json</c>. Each
/// language's identity is <c>rowles-analysis/&lt;code&gt;/&lt;version&gt;</c>. Snowball-owned languages
/// use the Snowball version at which their output last changed, with <c>+rN</c> for later Rowles-only
/// changes; Rowles-owned languages use Rowles 0.x.y versions.
/// </summary>
internal sealed record AnalysisVersions(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("languages")] IReadOnlyList<AnalysisLanguage> Languages)
{
    public const string SnowballLine = "snowball";
    public const string RowlesLine = "rowles";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AnalysisVersions Parse(string json)
        => JsonSerializer.Deserialize<AnalysisVersions>(json, SerializerOptions)
            ?? throw new InvalidDataException("analysis-versions.json is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
}

/// <summary>One language's default analysis identity.</summary>
/// <param name="Code">The BCP 47 primary subtag.</param>
/// <param name="Line"><c>snowball</c> or <c>rowles</c>.</param>
/// <param name="Version">The identity version.</param>
/// <param name="Stemmer">A description of the default stemmer.</param>
/// <param name="Algorithm">The Snowball algorithm, for the Snowball line.</param>
/// <param name="CompiledSnowballVersion">The Snowball version the algorithm was last generated from.</param>
/// <param name="CodeSha256">SHA-256 of the transformed algorithm body, for the Snowball line.</param>
/// <param name="Fingerprint">SHA-256 of the default pipeline's output over the fingerprint corpus.</param>
/// <param name="FingerprintVersion">The version that <paramref name="Fingerprint"/> was recorded under.</param>
/// <param name="EquivalenceNote">Why a code change kept the previous identity, when one did.</param>
/// <param name="Kind"><c>analyser</c> for a built-in analyser such as <c>StandardAnalyser</c>; omitted for a factory language.</param>
internal sealed record AnalysisLanguage(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("line")] string Line,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("stemmer")] string Stemmer,
    [property: JsonPropertyName("algorithm")] string? Algorithm = null,
    [property: JsonPropertyName("compiledSnowballVersion")] string? CompiledSnowballVersion = null,
    [property: JsonPropertyName("codeSha256")] string? CodeSha256 = null,
    [property: JsonPropertyName("fingerprint")] string? Fingerprint = null,
    [property: JsonPropertyName("fingerprintVersion")] string? FingerprintVersion = null,
    [property: JsonPropertyName("equivalenceNote")] string? EquivalenceNote = null,
    [property: JsonPropertyName("kind")] string? Kind = null)
{
    public const string AnalyserKind = "analyser";

    [JsonIgnore]
    public string Identity => $"rowles-analysis/{Code}/{Version}";
}

/// <summary>A requested Rowles-owned version bump.</summary>
internal enum AnalysisBump
{
    Minor,
    Patch,
}

/// <summary>Decisions supplied to <c>snowball generate</c>.</summary>
internal sealed record AnalysisVersionRequests(
    IReadOnlyDictionary<string, string> Equivalent,
    IReadOnlySet<string> Rebaseline,
    IReadOnlyDictionary<string, AnalysisBump> Bump,
    IReadOnlyDictionary<string, string>? PendingFingerprints)
{
    public static AnalysisVersionRequests None { get; } = new(
        new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, AnalysisBump>(), null);
}

/// <summary>
/// Applies the identity rules: an identity changes exactly when a language's default output can
/// change. Identical generated code keeps the identity; changed code takes the new Snowball version
/// unless explicitly declared equivalent; a changed pipeline fingerprint without an identity change
/// needs an explicit <c>--rebaseline</c> (Snowball line) or <c>--bump</c> (Rowles line).
/// </summary>
internal static class AnalysisVersionPlanner
{
    public static (AnalysisVersions Versions, IReadOnlyList<string> Changes) Apply(
        AnalysisVersions current,
        string snowballVersion,
        IReadOnlyDictionary<string, string> codeSha256ByAlgorithm,
        AnalysisVersionRequests requests)
    {
        var changes = new List<string>();
        var languages = new List<AnalysisLanguage>(current.Languages.Count);
        foreach (var language in current.Languages)
        {
            var next = language;
            if (language.Line == AnalysisVersions.SnowballLine)
                next = ApplyCode(next, snowballVersion, codeSha256ByAlgorithm, requests, changes);
            next = ApplyFingerprint(next, requests, changes);
            languages.Add(next);
        }

        var unknown = requests.Equivalent.Keys.Concat(requests.Rebaseline).Concat(requests.Bump.Keys)
            .Where(code => current.Languages.All(language => language.Code != code)).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"Unknown analysis language(s): {string.Join(", ", unknown)}.");

        return (current with { Languages = languages }, changes);
    }

    private static AnalysisLanguage ApplyCode(
        AnalysisLanguage language,
        string snowballVersion,
        IReadOnlyDictionary<string, string> codeSha256ByAlgorithm,
        AnalysisVersionRequests requests,
        List<string> changes)
    {
        string algorithm = language.Algorithm
            ?? throw new InvalidDataException($"Snowball language '{language.Code}' has no algorithm.");
        if (!codeSha256ByAlgorithm.TryGetValue(algorithm, out var code))
            throw new InvalidDataException($"No generated code for Snowball algorithm '{algorithm}'.");

        if (language.CodeSha256 is null)
        {
            changes.Add($"{language.Code}: recorded the generated code for {language.Identity}.");
            return language with { CodeSha256 = code, CompiledSnowballVersion = snowballVersion };
        }

        if (string.Equals(language.CodeSha256, code, StringComparison.Ordinal))
            return language with { CompiledSnowballVersion = snowballVersion };

        if (requests.Equivalent.TryGetValue(language.Code, out var reason))
        {
            changes.Add($"{language.Code}: generated code changed but was declared equivalent; {language.Identity} is kept ({reason}).");
            return language with { CodeSha256 = code, CompiledSnowballVersion = snowballVersion, EquivalenceNote = reason };
        }

        if (string.Equals(BaseVersion(language.Version), snowballVersion, StringComparison.Ordinal))
        {
            // A LeanCorpus-only change at the same Snowball version takes the next +rN revision. The
            // fingerprint step then records the new output under that revision.
            if (requests.Rebaseline.Contains(language.Code))
            {
                var revised = language with
                {
                    Version = NextRevision(language.Version),
                    CodeSha256 = code,
                    CompiledSnowballVersion = snowballVersion,
                    EquivalenceNote = null,
                };
                changes.Add($"{language.Code}: {language.Identity} -> {revised.Identity} (generated code changed; rebaselined).");
                return revised;
            }

            throw new InvalidOperationException(
                $"The generated code for '{language.Code}' changed without a Snowball version change. " +
                $"Pass --equivalent {language.Code} with --reason if its output is unchanged, or " +
                $"--rebaseline {language.Code} with --accept-fingerprints.");
        }

        var next = language with
        {
            Version = snowballVersion,
            CodeSha256 = code,
            CompiledSnowballVersion = snowballVersion,
            EquivalenceNote = null,
        };
        changes.Add($"{language.Code}: {language.Identity} -> {next.Identity} (generated code changed).");
        return next;
    }

    private static AnalysisLanguage ApplyFingerprint(
        AnalysisLanguage language, AnalysisVersionRequests requests, List<string> changes)
    {
        if (requests.PendingFingerprints is null
            || !requests.PendingFingerprints.TryGetValue(language.Code, out var fingerprint)
            || string.Equals(language.Fingerprint, fingerprint, StringComparison.Ordinal)
                && string.Equals(language.FingerprintVersion, language.Version, StringComparison.Ordinal))
        {
            if (requests.Rebaseline.Contains(language.Code) || requests.Bump.ContainsKey(language.Code))
            {
                throw new InvalidOperationException(
                    $"'{language.Code}' has no pending fingerprint change; its identity is not bumped without an output change.");
            }
            return language;
        }

        // First recording, or the identity already changed for this output.
        if (language.Fingerprint is null || !string.Equals(language.FingerprintVersion, language.Version, StringComparison.Ordinal))
        {
            changes.Add($"{language.Code}: recorded the pipeline fingerprint for {language.Identity}.");
            return language with { Fingerprint = fingerprint, FingerprintVersion = language.Version };
        }

        string version;
        if (language.Line == AnalysisVersions.SnowballLine && requests.Rebaseline.Contains(language.Code))
            version = NextRevision(language.Version);
        else if (language.Line == AnalysisVersions.RowlesLine && requests.Bump.TryGetValue(language.Code, out var bump))
            version = NextRowlesVersion(language.Version, bump);
        else
        {
            string command = language.Line == AnalysisVersions.SnowballLine
                ? $"--rebaseline {language.Code}"
                : $"--bump {language.Code}=minor (or =patch)";
            throw new InvalidOperationException(
                $"The default analysis output for '{language.Code}' changed, but its identity {language.Identity} did not. " +
                $"Run './devops snowball generate --accept-fingerprints {command}'.");
        }

        var next = language with { Version = version, Fingerprint = fingerprint, FingerprintVersion = version };
        changes.Add($"{language.Code}: {language.Identity} -> {next.Identity} (pipeline output changed).");
        return next;
    }

    /// <summary>Strips any <c>+rN</c> revision.</summary>
    public static string BaseVersion(string version)
    {
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }

    /// <summary><c>3.2.0</c> becomes <c>3.2.0+r2</c>; <c>3.2.0+r2</c> becomes <c>3.2.0+r3</c>.</summary>
    public static string NextRevision(string version)
    {
        int plus = version.IndexOf("+r", StringComparison.Ordinal);
        if (plus < 0)
            return version + "+r2";
        int revision = int.Parse(version.AsSpan(plus + 2), System.Globalization.CultureInfo.InvariantCulture);
        return $"{version[..plus]}+r{revision + 1}";
    }

    /// <summary>Applies a minor or patch bump to a Rowles 0.x.y version.</summary>
    public static string NextRowlesVersion(string version, AnalysisBump bump)
    {
        var parts = version.Split('.');
        if (parts.Length != 3 || !parts.All(static part => part.Length > 0 && part.All(char.IsAsciiDigit)))
            throw new InvalidDataException($"'{version}' is not a three-part Rowles analysis version.");
        int major = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        int minor = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        int patch = int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
        return bump == AnalysisBump.Minor ? $"{major}.{minor + 1}.0" : $"{major}.{minor}.{patch + 1}";
    }

    /// <summary>Renders the internal identity table compiled into Rowles.Text and Core.</summary>
    public static string RenderIdentities(AnalysisVersions versions)
    {
        var builder = new StringBuilder();
        builder.Append("// <auto-generated/>\n");
        builder.Append("// Do not edit. Generated by './devops snowball generate' from analysis-versions.json.\n\n");
        builder.Append("#nullable enable\n\n");
        builder.Append("namespace ").Append(SnowballTransform.ProductionNamespace).Append(";\n\n");
        builder.Append("/// <summary>The default analysis identity of each built-in language pipeline.</summary>\n");
        builder.Append("internal static class AnalysisIdentities\n{\n");
        builder.Append("    /// <summary>Returns the identity for a normalised primary subtag, or null when the language is unknown.</summary>\n");
        builder.Append("    public static string? Find(string primaryTag) => primaryTag switch\n    {\n");
        foreach (var language in versions.Languages)
            builder.Append($"        \"{language.Code}\" => \"{language.Identity}\",\n");
        builder.Append("        _ => null,\n    };\n}\n");
        return builder.ToString();
    }
}
