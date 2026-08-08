using System.Security.Cryptography;
using System.Text;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Ranking;

/// <summary>Immutable relevance settings resolved before a request is executed.</summary>
public sealed class RankingProfile
{
    private readonly IReadOnlyDictionary<string, float> _fieldWeights;
    private readonly IReadOnlyDictionary<string, ISimilarity> _fieldSimilarities;

    /// <summary>Initialises a profile from programmatic, immutable configuration.</summary>
    public RankingProfile(string name, string version, RankingPipeline? pipeline = null,
        IReadOnlyDictionary<string, float>? fieldWeights = null, ISimilarity? defaultSimilarity = null,
        IReadOnlyDictionary<string, ISimilarity>? fieldSimilarities = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A profile name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("A profile version is required.", nameof(version));
        Name = name; Version = version; Description = description; Pipeline = pipeline ?? RankingPipeline.Empty;
        DefaultSimilarity = defaultSimilarity;
        _fieldWeights = CopyWeights(fieldWeights);
        _fieldSimilarities = CopySimilarities(fieldSimilarities);
        Fingerprint = FingerprintOf($"{Name}\n{Version}\n{Description}\n{DefaultSimilarity}\n{Pipeline.Fingerprint}\n" +
            string.Join("\n", _fieldWeights.Select(static p => $"{p.Key}={p.Value:R}")) + "\n" +
            string.Join("\n", _fieldSimilarities.Select(static p => $"{p.Key}={p.Value}")));
    }

    public string Name { get; }
    public string Version { get; }
    public string? Description { get; }
    public string Fingerprint { get; }
    public RankingPipeline Pipeline { get; }
    public ISimilarity? DefaultSimilarity { get; }
    public IReadOnlyDictionary<string, float> FieldWeights => _fieldWeights;
    public IReadOnlyDictionary<string, ISimilarity> FieldSimilarities => _fieldSimilarities;

    private static IReadOnlyDictionary<string, float> CopyWeights(IReadOnlyDictionary<string, float>? values)
        => (values ?? new Dictionary<string, float>()).OrderBy(static p => p.Key, StringComparer.Ordinal).ToDictionary(
            static p => !string.IsNullOrWhiteSpace(p.Key) ? p.Key : throw new ArgumentException("Field names must be non-empty."),
            static p => float.IsFinite(p.Value) && p.Value > 0 ? p.Value : throw new ArgumentOutOfRangeException(nameof(values)), StringComparer.Ordinal);
    private static IReadOnlyDictionary<string, ISimilarity> CopySimilarities(IReadOnlyDictionary<string, ISimilarity>? values)
        => (values ?? new Dictionary<string, ISimilarity>()).OrderBy(static p => p.Key, StringComparer.Ordinal).ToDictionary(
            static p => !string.IsNullOrWhiteSpace(p.Key) ? p.Key : throw new ArgumentException("Field names must be non-empty."),
            static p => p.Value ?? throw new ArgumentNullException(nameof(values)), StringComparer.Ordinal);
    public static string FingerprintOf(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

/// <summary>Immutable ordered plan for bounded relevance processing.</summary>
public sealed class RankingPipeline
{
    public static RankingPipeline Empty { get; } = new([]);
    public RankingPipeline(IEnumerable<RankingStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        Stages = stages.ToArray();
        if (Stages.Select(static s => s.Identity).Distinct(StringComparer.Ordinal).Count() != Stages.Count)
            throw new ArgumentException("Stage identities must be unique.", nameof(stages));
        Fingerprint = RankingProfile.FingerprintOf(string.Join("\n", Stages.Select(static s => s.CacheIdentity)));
    }
    public IReadOnlyList<RankingStage> Stages { get; }
    public string Fingerprint { get; }
}

/// <summary>Base type for a bounded, immutable ranking stage.</summary>
public abstract class RankingStage
{
    protected RankingStage(string identity, int candidateBudget, TimeSpan? timeout = null)
    { if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("An identity is required.", nameof(identity)); ArgumentOutOfRangeException.ThrowIfLessThan(candidateBudget, 1); Identity = identity; CandidateBudget = candidateBudget; Timeout = timeout; }
    public string Identity { get; }
    public int CandidateBudget { get; }
    public TimeSpan? Timeout { get; }
    internal abstract string CacheIdentity { get; }
}

/// <summary>Applies a numeric value source to a bounded candidate window.</summary>
public sealed class ScoreFunctionStage : RankingStage
{
    public ScoreFunctionStage(string identity, DoubleValuesSource source, RankingScoreCombination combination, int candidateBudget, TimeSpan? timeout = null) : base(identity, candidateBudget, timeout) { Source = source ?? throw new ArgumentNullException(nameof(source)); Combination = combination; }
    public DoubleValuesSource Source { get; }
    public RankingScoreCombination Combination { get; }
    internal override string CacheIdentity => $"function:{Identity}:{CandidateBudget}:{Timeout}:{Combination}:{Source}";
}

/// <summary>Uses the existing bounded query rescorer as a pipeline stage.</summary>
public sealed class QueryRescorerStage : RankingStage
{
    public QueryRescorerStage(string identity, QueryRescorer rescorer, int candidateBudget, TimeSpan? timeout = null) : base(identity, candidateBudget, timeout) { Rescorer = rescorer ?? throw new ArgumentNullException(nameof(rescorer)); }
    public QueryRescorer Rescorer { get; }
    internal override string CacheIdentity => $"rescore:{Identity}:{CandidateBudget}:{Timeout}:{Rescorer.Query}:{Rescorer.FirstPassWeight:R}:{Rescorer.SecondPassWeight:R}";
}

/// <summary>How a numeric or reranker score combines with the current score.</summary>
public enum RankingScoreCombination { Replace, Add, Multiply, Interpolate, OrderOnly }

/// <summary>One deterministic rule evaluated before candidate retrieval.</summary>
public sealed class QueryRule
{
    public QueryRule(string id, int priority, QueryRuleMatch match, IEnumerable<QueryRuleAction> actions, bool enabled = true, DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null)
    { if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A rule identifier is required.", nameof(id)); if (endsAt is not null && startsAt is not null && endsAt < startsAt) throw new ArgumentException("The activation window is invalid.", nameof(endsAt)); Id = id; Priority = priority; Match = match ?? throw new ArgumentNullException(nameof(match)); Actions = actions?.ToArray() ?? throw new ArgumentNullException(nameof(actions)); Enabled = enabled; StartsAt = startsAt; EndsAt = endsAt; }
    public string Id { get; } public int Priority { get; } public QueryRuleMatch Match { get; } public IReadOnlyList<QueryRuleAction> Actions { get; } public bool Enabled { get; } public DateTimeOffset? StartsAt { get; } public DateTimeOffset? EndsAt { get; }
    internal bool Matches(RankingRequestContext context, string profile, DateTimeOffset now) => Enabled && (StartsAt is null || StartsAt <= now) && (EndsAt is null || now < EndsAt) && Match.Matches(context, profile);
}

/// <summary>Bounded immutable rule collection. Matching is request-scoped, never document-scoped.</summary>
public sealed class QueryRuleSet
{
    public QueryRuleSet(IEnumerable<QueryRule> rules, int maximumRules = 256)
    { ArgumentOutOfRangeException.ThrowIfLessThan(maximumRules, 1); Rules = rules?.OrderByDescending(static r => r.Priority).ThenBy(static r => r.Id, StringComparer.Ordinal).ToArray() ?? throw new ArgumentNullException(nameof(rules)); if (Rules.Count > maximumRules || Rules.Select(static r => r.Id).Distinct(StringComparer.Ordinal).Count() != Rules.Count) throw new ArgumentException("Rules must have unique identifiers and fit the configured bound.", nameof(rules)); Fingerprint = RankingProfile.FingerprintOf(string.Join("\n", Rules.Select(static r => $"{r.Id}:{r.Priority}:{r.Enabled}:{r.StartsAt}:{r.EndsAt}:{r.Match.ExactQueryIdentity}:{r.Match.ProfileName}:{r.Match.Locale}:{string.Join(',', r.Match.RequiredTags.OrderBy(static t => t, StringComparer.Ordinal))}:{string.Join(',', r.Actions)}"))); }
    public IReadOnlyList<QueryRule> Rules { get; } public string Fingerprint { get; }
    public IReadOnlyList<QueryRule> Resolve(RankingRequestContext context, string profile, DateTimeOffset now) => Rules.Where(r => r.Matches(context, profile, now)).ToArray();
}

/// <summary>Exact request identity and trusted, application-supplied matching context.</summary>
public sealed class RankingRequestContext
{
    public RankingRequestContext(string? queryText = null, string? locale = null, IEnumerable<string>? tags = null, IReadOnlyDictionary<string, string>? values = null, string? safeCacheIdentity = null)
    { QueryText = queryText; ExactQueryIdentity = Normalise(queryText); Locale = locale; Tags = new HashSet<string>(tags ?? [], StringComparer.Ordinal); Values = new Dictionary<string, string>(values ?? new Dictionary<string, string>(), StringComparer.Ordinal); SafeCacheIdentity = safeCacheIdentity; }
    public string? QueryText { get; } public string ExactQueryIdentity { get; } public string? Locale { get; } public IReadOnlySet<string> Tags { get; } public IReadOnlyDictionary<string, string> Values { get; } public string? SafeCacheIdentity { get; }
    internal static string Normalise(string? text) => string.Join(' ', (text ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}

/// <summary>Declarative rule conditions. Exact text identity is deliberately not analysed text.</summary>
public sealed class QueryRuleMatch
{
    public QueryRuleMatch(string? exactQueryIdentity = null, string? profileName = null, string? locale = null, IEnumerable<string>? requiredTags = null, IReadOnlyDictionary<string, string>? requiredValues = null)
    { ExactQueryIdentity = exactQueryIdentity is null ? null : RankingRequestContext.Normalise(exactQueryIdentity); ProfileName = profileName; Locale = locale; RequiredTags = new HashSet<string>(requiredTags ?? [], StringComparer.Ordinal); RequiredValues = new Dictionary<string, string>(requiredValues ?? new Dictionary<string, string>(), StringComparer.Ordinal); }
    public string? ExactQueryIdentity { get; } public string? ProfileName { get; } public string? Locale { get; } public IReadOnlySet<string> RequiredTags { get; } public IReadOnlyDictionary<string, string> RequiredValues { get; }
    internal bool Matches(RankingRequestContext c, string profile) => (ExactQueryIdentity is null || ExactQueryIdentity == c.ExactQueryIdentity) && (ProfileName is null || ProfileName == profile) && (Locale is null || Locale == c.Locale) && RequiredTags.All(c.Tags.Contains) && RequiredValues.All(p => c.Values.TryGetValue(p.Key, out var value) && value == p.Value);
}

public abstract record QueryRuleAction;
public sealed record FilterQueryRuleAction(Query Filter) : QueryRuleAction;
public sealed record ScoreQueryRuleAction(IReadOnlyCollection<int> DocumentIds, float Factor) : QueryRuleAction;
public sealed record PinQueryRuleAction(IReadOnlyDictionary<int, int> Positions) : QueryRuleAction;

/// <summary>A complete immutable request for profile-based search.</summary>
public sealed class RankingSearchRequest
{
    public RankingSearchRequest(Query query, int topN, RankingProfile profile, QueryRuleSet? rules = null, RankingRequestContext? context = null, DateTimeOffset? now = null)
    { Query = query ?? throw new ArgumentNullException(nameof(query)); ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1); TopN = topN; Profile = profile ?? throw new ArgumentNullException(nameof(profile)); Rules = rules; Context = context ?? new RankingRequestContext(); Now = now ?? DateTimeOffset.UtcNow; }
    public Query Query { get; } public int TopN { get; } public RankingProfile Profile { get; } public QueryRuleSet? Rules { get; } public RankingRequestContext Context { get; } public DateTimeOffset Now { get; }
}

/// <summary>Profile-based results, including cache and cursor compatibility identity.</summary>
public sealed class RankingSearchResult
{
    internal RankingSearchResult(TopDocs topDocs, string identity, IReadOnlyList<string> matchedRules, bool partial) { TopDocs = topDocs; CompatibilityIdentity = identity; MatchedRuleIds = matchedRules; IsPartial = partial; }
    public TopDocs TopDocs { get; } public string CompatibilityIdentity { get; } public IReadOnlyList<string> MatchedRuleIds { get; } public bool IsPartial { get; }
}
