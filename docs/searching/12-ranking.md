# Ranking profiles and query rules

`RankingProfile` keeps relevance configuration immutable and named. Resolve a profile before search, then use its fingerprint as the identity of the result configuration.

Profiles are programmatic. LeanCorpus does not load rules or profiles from files, databases, or request payloads.

```csharp
var profile = new RankingProfile(
    name: "catalogue",
    version: "2026-07",
    pipeline: new RankingPipeline([
        new ScoreFunctionStage(
            "popularity",
            DoubleValuesSource.FromDoubleField("popularity"),
            RankingScoreCombination.Add,
            candidateBudget: 200)
    ]));

var request = new RankingSearchRequest(
    new TermQuery("body", "coffee"),
    topN: 20,
    profile,
    context: new RankingRequestContext(
        queryText: "coffee",
        locale: "en-GB",
        safeCacheIdentity: "public-catalogue"));

RankingSearchResult result = searcher.Search(request);
```

## Profiles and similarities

A profile can declare a default similarity and field-specific similarities. The searcher validates these against its own immutable configuration. It rejects a mismatch rather than changing scoring semantics during a request.

Field weights are immutable profile data. Use them with query types that support field weights, such as `CombinedFieldsQuery`.

## Rules

`QueryRuleSet` is bounded and ordered by descending priority, then rule identifier. Rules match once per request, never once per document. Matching can use normalised exact query text, profile name, locale, tags, and trusted application context.

Exact query matching is not analysed-text matching. It normalises Unicode form, whitespace, and case only.

```csharp
var rules = new QueryRuleSet([
    new QueryRule(
        "hide-discontinued",
        priority: 100,
        match: new QueryRuleMatch(exactQueryIdentity: "coffee"),
        actions: [
            new FilterQueryRuleAction(new TermQuery("status", "available"))
        ]),
    new QueryRule(
        "seasonal-feature",
        priority: 50,
        match: new QueryRuleMatch(requiredTags: ["summer"]),
        actions: [
            new PinQueryRuleAction(new Dictionary<int, int> { [123] = 1 })
        ])
]);
```

Filters are combined with the query before candidate retrieval. Score actions multiply the score of listed candidate document IDs. Pins use 1-based absolute positions in the filtered result set. A pinned document must already be a matching candidate; missing, deleted, filtered, and non-matching documents are ignored. A document appears once only.

## Pipelines

`RankingPipeline` is an immutable ordered list of bounded stages. The core provides numeric score-function stages and a stage for `QueryRescorer`.

Each stage has an identity, candidate budget, and optional timeout. Candidates are truncated before a stage runs. A timeout returns the result collected so far and marks it partial. Scores sort descending, with document ID as the final tie-breaker.

```csharp
var pipeline = new RankingPipeline([
    new QueryRescorerStage(
        "phrase-preference",
        new QueryRescorer(new PhraseQuery("body", "fresh", "coffee")),
        candidateBudget: 100)
]);
```

The pipeline does not expose the searcher, writer, stored document content, or pooled buffers to application code. External model runtimes and remote rerankers remain application concerns.

## Evaluation and diversification

`RankingMetrics` calculates precision, recall, reciprocal rank, average precision, DCG, and NDCG from application-owned judgements. By default, unjudged documents are excluded from precision; use `TreatAsNonRelevant` when that is the intended evaluation policy.

`MaximumMarginalRelevance.Select` diversifies an already bounded candidate window. It preserves the initial score, reports the novelty penalty for every selected document, and resolves ties by document ID. Supply a bounded similarity function that returns `null` for unavailable representations, then choose whether those candidates receive no novelty penalty or are excluded.

## Cache and cursors

The result compatibility identity includes the profile fingerprint, pipeline fingerprint, ruleset fingerprint, matched rules, and optional application-provided safe cache identity. Reuse it when binding cursors. Do not place tenant identifiers in `safeCacheIdentity` unless they are already opaque and safe to record.

## See also

- <xref:Rowles.LeanCorpus.Search.Ranking.RankingProfile>
- <xref:Rowles.LeanCorpus.Search.Ranking.QueryRuleSet>
- <xref:Rowles.LeanCorpus.Search.Ranking.RankingPipeline>
- <xref:Rowles.LeanCorpus.Search.Scoring.QueryRescorer>
