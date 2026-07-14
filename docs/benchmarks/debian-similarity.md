---
title: Benchmarks - Similarity
---

# Similarity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean      | Error    | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |----------:|---------:|----------:|------:|--------:|-------:|----------:|------------:|
| LeanCorpus_Bm25_TermQuery                | 100000        |  87.49 μs | 0.474 μs |  0.443 μs |  1.00 |    0.00 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_TfIdf_TermQuery               | 100000        |  92.77 μs | 0.297 μs |  0.278 μs |  1.06 |    0.01 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_Bm25_BooleanQuery             | 100000        | 145.46 μs | 2.830 μs |  3.579 μs |  1.66 |    0.04 | 3.6621 |   15826 B |       21.98 |
| LeanCorpus_TfIdf_BooleanQuery            | 100000        | 135.64 μs | 5.268 μs | 15.532 μs |  1.55 |    0.18 | 3.6621 |   15866 B |       22.04 |
| LeanCorpus_Dirichlet_TermQuery           | 100000        | 161.60 μs | 0.637 μs |  0.596 μs |  1.85 |    0.01 |      - |     960 B |        1.33 |
| LuceneNet_Dirichlet_TermQuery            | 100000        | 212.21 μs | 0.321 μs |  0.285 μs |  2.43 |    0.01 | 5.1270 |   22327 B |       31.01 |
| LeanCorpus_JelinekMercer_TermQuery       | 100000        | 165.53 μs | 0.717 μs |  0.671 μs |  1.89 |    0.01 |      - |     960 B |        1.33 |
| LuceneNet_JelinekMercer_TermQuery        | 100000        | 165.59 μs | 1.499 μs |  1.329 μs |  1.89 |    0.02 | 5.1270 |   22327 B |       31.01 |
| LeanCorpus_AbsoluteDiscounting_TermQuery | 100000        | 174.51 μs | 0.781 μs |  0.692 μs |  1.99 |    0.01 |      - |     960 B |        1.33 |
| LeanCorpus_Dirichlet_BooleanQuery        | 100000        | 697.89 μs | 5.062 μs |  4.227 μs |  7.98 |    0.06 | 4.8828 |   20630 B |       28.65 |
| LeanCorpus_Bm25Plus_TermQuery            | 100000        |  89.76 μs | 0.490 μs |  0.458 μs |  1.03 |    0.01 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_Bm25L_TermQuery               | 100000        |  91.40 μs | 0.449 μs |  0.420 μs |  1.04 |    0.01 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_TfIdfAugmented_TermQuery      | 100000        |  91.35 μs | 0.139 μs |  0.130 μs |  1.04 |    0.01 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_TfIdfPivoted_TermQuery        | 100000        |  87.65 μs | 0.083 μs |  0.074 μs |  1.00 |    0.00 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_TfIdfDoubleNorm_TermQuery     | 100000        |  89.77 μs | 0.469 μs |  0.439 μs |  1.03 |    0.01 | 0.1221 |     720 B |        1.00 |
| LeanCorpus_Bm25Plus_BooleanQuery         | 100000        | 106.28 μs | 4.844 μs | 14.053 μs |  1.21 |    0.16 | 3.6621 |   15881 B |       22.06 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-similarity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-similarity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-similarity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-similarity" style="max-width:960px"><canvas id="chart-bench-similarity" style="height:500px"></canvas></div>
<p><a href="debian-similarity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


