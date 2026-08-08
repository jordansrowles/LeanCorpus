using System.Diagnostics;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

public sealed partial class IndexSearcher
{
    /// <summary>
    /// Executes an immutable ranking profile, its bounded pipeline, and its matching rules.
    /// Profile similarities must match this searcher's immutable scoring configuration.
    /// </summary>
    public RankingSearchResult Search(RankingSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRankingProfile(request.Profile);
        var matchedRules = request.Rules?.Resolve(request.Context, request.Profile.Name, request.Now) ?? [];
        Query query = ApplyFilters(request.Query, matchedRules);
        string identity = CreateRankingIdentity(request, matchedRules);

        if (_queryCache?.TryGet(query, request.TopN, identity) is { } cached)
            return new RankingSearchResult(cached, identity, matchedRules.Select(static r => r.Id).ToArray(), cached.IsPartial);

        int candidateBudget = Math.Max(request.TopN, request.Profile.Pipeline.Stages.Count == 0 ? request.TopN : request.Profile.Pipeline.Stages.Max(static s => s.CandidateBudget));
        var firstPass = Search(query, candidateBudget);
        var candidates = firstPass.ScoreDocs.ToArray();
        ApplyScoreRules(candidates, matchedRules);
        bool partial = firstPass.IsPartial;

        foreach (var stage in request.Profile.Pipeline.Stages)
        {
            var started = Stopwatch.GetTimestamp();
            if (candidates.Length > stage.CandidateBudget)
                Array.Resize(ref candidates, stage.CandidateBudget);
            try
            {
                switch (stage)
                {
                    case ScoreFunctionStage function:
                        ApplyFunction(function, candidates);
                        break;
                    case QueryRescorerStage rescore:
                        candidates = rescore.Rescorer.Rescore(this,
                            new TopDocs(firstPass.TotalHits, candidates, partial), candidates.Length).ScoreDocs;
                        break;
                    default:
                        throw new NotSupportedException($"Ranking stage '{stage.Identity}' is not supported by this core executor.");
                }
                if (stage.Timeout is { } timeout && Stopwatch.GetElapsedTime(started) > timeout)
                    throw new TimeoutException($"Ranking stage '{stage.Identity}' exceeded its timeout.");
            }
            catch (TimeoutException)
            {
                partial = true;
                break;
            }
        }

        Array.Sort(candidates, CompareScoreDocs);
        var finalDocs = ApplyPins(candidates, matchedRules, request.TopN);
        var result = new TopDocs(firstPass.TotalHits, finalDocs, partial);
        _queryCache?.Put(query, request.TopN, result, identity);
        return new RankingSearchResult(result, identity, matchedRules.Select(static r => r.Id).ToArray(), partial);
    }

    private void ValidateRankingProfile(RankingProfile profile)
    {
        if (profile.DefaultSimilarity is not null && !profile.DefaultSimilarity.Equals(_similarity))
            throw new InvalidOperationException("The ranking profile default similarity does not match this IndexSearcher. Create the searcher with the profile similarity before searching.");
        foreach (var (field, similarity) in profile.FieldSimilarities)
        {
            var actual = _config.PerFieldSimilarities is not null && _config.PerFieldSimilarities.TryGetValue(field, out var configured)
                ? configured : _similarity;
            if (!similarity.Equals(actual))
                throw new InvalidOperationException($"The ranking profile similarity for '{field}' does not match this IndexSearcher.");
        }
    }

    private static Query ApplyFilters(Query query, IReadOnlyList<QueryRule> rules)
    {
        var filters = rules.SelectMany(static r => r.Actions).OfType<FilterQueryRuleAction>().ToArray();
        if (filters.Length == 0) return query;
        var builder = new BooleanQuery.Builder().Add(query, Occur.Must);
        foreach (var filter in filters) builder.Add(filter.Filter, Occur.Must);
        return builder.Build();
    }

    private void ApplyFunction(ScoreFunctionStage stage, ScoreDoc[] candidates)
    {
        var source = stage.Source.Rewrite(this);
        for (int i = 0; i < candidates.Length; i++)
        {
            var current = candidates[i];
            if (!source.TryGetValue(this, current.DocId, current.Score, out var value) || !double.IsFinite(value)) continue;
            float next = (float)value;
            float score = stage.Combination switch
            {
                RankingScoreCombination.Replace => next,
                RankingScoreCombination.Add => current.Score + next,
                RankingScoreCombination.Multiply => current.Score * next,
                RankingScoreCombination.Interpolate => (current.Score + next) / 2.0f,
                RankingScoreCombination.OrderOnly => next,
                _ => throw new ArgumentOutOfRangeException(),
            };
            if (float.IsFinite(score)) candidates[i] = new ScoreDoc(current.DocId, score);
        }
    }

    private static void ApplyScoreRules(ScoreDoc[] candidates, IReadOnlyList<QueryRule> rules)
    {
        foreach (var action in rules.SelectMany(static r => r.Actions).OfType<ScoreQueryRuleAction>())
        {
            if (!float.IsFinite(action.Factor) || action.Factor < 0) throw new InvalidOperationException("Rule score factors must be finite and non-negative.");
            var ids = new HashSet<int>(action.DocumentIds);
            for (int i = 0; i < candidates.Length; i++)
                if (ids.Contains(candidates[i].DocId)) candidates[i] = candidates[i] with { Score = candidates[i].Score * action.Factor };
        }
    }

    private static ScoreDoc[] ApplyPins(ScoreDoc[] candidates, IReadOnlyList<QueryRule> rules, int topN)
    {
        var pins = new Dictionary<int, int>();
        foreach (var action in rules.SelectMany(static r => r.Actions).OfType<PinQueryRuleAction>())
            foreach (var pair in action.Positions)
                if (pair.Key >= 0 && pair.Value > 0 && !pins.ContainsKey(pair.Key)) pins.Add(pair.Key, pair.Value);
        var byId = candidates.ToDictionary(static d => d.DocId);
        var output = new ScoreDoc?[topN];
        foreach (var (docId, position) in pins)
            if (position <= topN && byId.TryGetValue(docId, out var doc) && output[position - 1] is null) output[position - 1] = doc;
        var consumed = new HashSet<int>(output.Where(static d => d.HasValue).Select(static d => d!.Value.DocId));
        int next = 0;
        foreach (var doc in candidates)
        {
            if (consumed.Contains(doc.DocId)) continue;
            while (next < output.Length && output[next] is not null) next++;
            if (next == output.Length) break;
            output[next++] = doc;
        }
        return output.Where(static d => d.HasValue).Select(static d => d!.Value).ToArray();
    }

    private static int CompareScoreDocs(ScoreDoc left, ScoreDoc right)
    { int score = right.Score.CompareTo(left.Score); return score != 0 ? score : left.DocId.CompareTo(right.DocId); }

    private static string CreateRankingIdentity(RankingSearchRequest request, IReadOnlyList<QueryRule> rules)
        => RankingProfile.FingerprintOf($"{request.Profile.Fingerprint}:{request.Rules?.Fingerprint}:{request.Context.SafeCacheIdentity}:{string.Join(',', rules.Select(static r => r.Id))}");
}
