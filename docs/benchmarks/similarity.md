---
title: Benchmarks - Similarity
---

# Similarity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_TfIdf_TermQuery               | 100000        | 118.4 μs | 0.24 μs | 0.23 μs |  1.08 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LuceneNet_TfIdf_TermQuery                | 100000        | 148.5 μs | 0.25 μs | 0.22 μs |  1.35 |    0.00 | 11.9629 | 0.2441 |   51119 B |       63.27 |
| LeanCorpus_TfIdf_BooleanQuery            | 100000        | 435.9 μs | 4.05 μs | 3.59 μs |  3.97 |    0.03 |  3.9063 |      - |   16414 B |       20.31 |
| LeanCorpus_Bm25_TermQuery                | 100000        | 109.9 μs | 0.14 μs | 0.12 μs |  1.00 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LuceneNet_Bm25_TermQuery                 | 100000        | 145.3 μs | 0.20 μs | 0.18 μs |  1.32 |    0.00 | 12.2070 | 0.2441 |   52221 B |       64.63 |
| LeanCorpus_Bm25_BooleanQuery             | 100000        | 431.4 μs | 5.36 μs | 5.02 μs |  3.93 |    0.04 |  3.9063 |      - |   16388 B |       20.28 |
| LuceneNet_Bm25_BooleanQuery              | 100000        | 370.2 μs | 2.92 μs | 2.73 μs |  3.37 |    0.02 | 30.7617 | 0.9766 |  130715 B |      161.78 |
| LeanCorpus_Dirichlet_TermQuery           | 100000        | 162.0 μs | 0.23 μs | 0.21 μs |  1.47 |    0.00 |       - |      - |     808 B |        1.00 |
| LuceneNet_Dirichlet_TermQuery            | 100000        | 314.1 μs | 0.38 μs | 0.36 μs |  2.86 |    0.00 | 11.7188 | 0.4883 |   50879 B |       62.97 |
| LeanCorpus_JelinekMercer_TermQuery       | 100000        | 165.7 μs | 0.13 μs | 0.11 μs |  1.51 |    0.00 |       - |      - |     808 B |        1.00 |
| LuceneNet_JelinekMercer_TermQuery        | 100000        | 244.0 μs | 0.13 μs | 0.11 μs |  2.22 |    0.00 | 11.7188 | 0.4883 |   50879 B |       62.97 |
| LeanCorpus_AbsoluteDiscounting_TermQuery | 100000        | 179.7 μs | 0.11 μs | 0.09 μs |  1.64 |    0.00 |       - |      - |     808 B |        1.00 |
| LeanCorpus_Dirichlet_BooleanQuery        | 100000        | 443.5 μs | 6.45 μs | 6.03 μs |  4.04 |    0.05 |  3.9063 |      - |   16419 B |       20.32 |
| LeanCorpus_Bm25Plus_TermQuery            | 100000        | 118.5 μs | 0.10 μs | 0.09 μs |  1.08 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LeanCorpus_Bm25L_TermQuery               | 100000        | 122.2 μs | 0.13 μs | 0.12 μs |  1.11 |    0.00 |       - |      - |     808 B |        1.00 |
| LeanCorpus_TfIdfAugmented_TermQuery      | 100000        | 121.1 μs | 0.11 μs | 0.10 μs |  1.10 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LeanCorpus_TfIdfPivoted_TermQuery        | 100000        | 116.5 μs | 0.12 μs | 0.11 μs |  1.06 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LeanCorpus_TfIdfDoubleNorm_TermQuery     | 100000        | 119.0 μs | 0.11 μs | 0.10 μs |  1.08 |    0.00 |  0.1221 |      - |     808 B |        1.00 |
| LeanCorpus_Bm25Plus_BooleanQuery         | 100000        | 438.9 μs | 5.58 μs | 5.22 μs |  4.00 |    0.05 |  3.9063 |      - |   16396 B |       20.29 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-similarity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-similarity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-similarity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-similarity" style="max-width:960px"><canvas id="chart-bench-similarity" style="height:500px"></canvas></div>
<p><a href="similarity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


