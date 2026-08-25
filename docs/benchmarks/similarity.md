---
title: Benchmarks - Similarity
---

# Similarity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `66ba120` &nbsp;&middot;&nbsp; 24 August 2026 21:47 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_TfIdf_TermQuery               | 100000        | 125.9 μs | 0.34 μs | 0.28 μs |  1.09 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LuceneNet_TfIdf_TermQuery                | 100000        | 148.8 μs | 0.33 μs | 0.29 μs |  1.29 |    0.00 | 11.9629 | 0.2441 |  49.92 KB |       13.57 |
| LeanCorpus_TfIdf_BooleanQuery            | 100000        | 435.2 μs | 4.86 μs | 4.55 μs |  3.77 |    0.04 |  4.8828 |      - |  20.25 KB |        5.50 |
| LeanCorpus_Bm25_TermQuery                | 100000        | 115.6 μs | 0.06 μs | 0.05 μs |  1.00 |    0.00 |  0.8545 |      - |   3.68 KB |        1.00 |
| LuceneNet_Bm25_TermQuery                 | 100000        | 146.2 μs | 0.39 μs | 0.37 μs |  1.27 |    0.00 | 12.2070 | 0.2441 |     51 KB |       13.86 |
| LeanCorpus_Bm25_BooleanQuery             | 100000        | 431.4 μs | 4.25 μs | 3.98 μs |  3.73 |    0.03 |  4.8828 |      - |  20.25 KB |        5.50 |
| LuceneNet_Bm25_BooleanQuery              | 100000        | 370.2 μs | 2.63 μs | 2.46 μs |  3.20 |    0.02 | 30.7617 | 0.9766 | 127.65 KB |       34.69 |
| LeanCorpus_Dirichlet_TermQuery           | 100000        | 169.6 μs | 0.18 μs | 0.17 μs |  1.47 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LuceneNet_Dirichlet_TermQuery            | 100000        | 315.6 μs | 0.58 μs | 0.48 μs |  2.73 |    0.00 | 11.7188 | 0.4883 |  49.69 KB |       13.50 |
| LeanCorpus_JelinekMercer_TermQuery       | 100000        | 173.0 μs | 0.25 μs | 0.23 μs |  1.50 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LuceneNet_JelinekMercer_TermQuery        | 100000        | 246.9 μs | 0.60 μs | 0.54 μs |  2.14 |    0.00 | 11.7188 | 0.4883 |  49.69 KB |       13.50 |
| LeanCorpus_AbsoluteDiscounting_TermQuery | 100000        | 187.1 μs | 0.23 μs | 0.19 μs |  1.62 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LeanCorpus_Dirichlet_BooleanQuery        | 100000        | 452.3 μs | 4.52 μs | 4.23 μs |  3.91 |    0.04 |  4.8828 |      - |  20.27 KB |        5.51 |
| LeanCorpus_Bm25Plus_TermQuery            | 100000        | 127.7 μs | 0.19 μs | 0.18 μs |  1.11 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LeanCorpus_Bm25L_TermQuery               | 100000        | 128.9 μs | 0.17 μs | 0.16 μs |  1.12 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LeanCorpus_TfIdfAugmented_TermQuery      | 100000        | 127.3 μs | 0.15 μs | 0.14 μs |  1.10 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LeanCorpus_TfIdfPivoted_TermQuery        | 100000        | 124.2 μs | 0.19 μs | 0.17 μs |  1.07 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LeanCorpus_TfIdfDoubleNorm_TermQuery     | 100000        | 127.7 μs | 0.18 μs | 0.17 μs |  1.10 |    0.00 |  0.7324 |      - |   3.68 KB |        1.00 |
| LeanCorpus_Bm25Plus_BooleanQuery         | 100000        | 434.2 μs | 3.43 μs | 3.04 μs |  3.76 |    0.03 |  4.8828 |      - |  20.26 KB |        5.51 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-similarity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-similarity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-similarity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-similarity" style="max-width:960px"><canvas id="chart-bench-similarity" style="height:500px"></canvas></div>
<p><a href="similarity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


