using Rowles.LeanCorpus.DevOps.Commands;

namespace Rowles.LeanCorpus.DevOps.Snowball;

/// <summary>Turns <c>snowball generate</c> options into identity decisions.</summary>
internal static class SnowballCommandRequests
{
    public static AnalysisVersionRequests Parse(SnowballCommandOptions options, SnowballWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Equivalent.Count > 0 && string.IsNullOrWhiteSpace(options.Reason))
            throw new ArgumentException("--equivalent needs --reason, which is recorded with the identity.");

        var equivalent = options.Equivalent.ToDictionary(static code => code, _ => options.Reason!.Trim(), StringComparer.Ordinal);
        var bump = new Dictionary<string, AnalysisBump>(StringComparer.Ordinal);
        foreach (var value in options.Bump)
        {
            var parts = value.Split('=', 2);
            if (parts.Length != 2 || !Enum.TryParse<AnalysisBump>(parts[1], ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
                throw new ArgumentException($"--bump '{value}' must be <code>=minor or <code>=patch.");
            bump[parts[0]] = kind;
        }

        var pending = options.AcceptFingerprints ? workspace.ReadPendingFingerprints() : null;
        if (!options.AcceptFingerprints && (options.Rebaseline.Count > 0 || bump.Count > 0))
            throw new ArgumentException("--rebaseline and --bump apply to recorded output changes; pass --accept-fingerprints.");

        return new AnalysisVersionRequests(equivalent, options.Rebaseline.ToHashSet(StringComparer.Ordinal), bump, pending);
    }
}
