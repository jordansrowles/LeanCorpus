---
title: Benchmarks - Similarity
---

# Similarity

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                   | DocumentCount | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------------------------------------- |-------------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| LeanCorpus_TfIdf_TermQuery               | 100000        | 116.4 μs | 0.14 μs | 0.13 μs |  1.13 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LuceneNet_TfIdf_TermQuery                | 100000        | 147.2 μs | 0.27 μs | 0.23 μs |  1.43 |    0.00 | 11.9629 | 0.2441 |   51119 B |       58.09 |
| LeanCorpus_TfIdf_BooleanQuery            | 100000        | 428.8 μs | 4.74 μs | 4.44 μs |  4.17 |    0.04 |  3.9063 |      - |   16534 B |       18.79 |
| LeanCorpus_Bm25_TermQuery                | 100000        | 102.9 μs | 0.11 μs | 0.10 μs |  1.00 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LuceneNet_Bm25_TermQuery                 | 100000        | 146.7 μs | 0.10 μs | 0.09 μs |  1.43 |    0.00 | 12.2070 | 0.2441 |   52221 B |       59.34 |
| LeanCorpus_Bm25_BooleanQuery             | 100000        | 422.7 μs | 5.01 μs | 4.68 μs |  4.11 |    0.04 |  3.9063 |      - |   16516 B |       18.77 |
| LuceneNet_Bm25_BooleanQuery              | 100000        | 366.1 μs | 2.59 μs | 2.42 μs |  3.56 |    0.02 | 30.7617 | 0.9766 |  130715 B |      148.54 |
| LeanCorpus_Dirichlet_TermQuery           | 100000        | 156.9 μs | 0.14 μs | 0.12 μs |  1.52 |    0.00 |       - |      - |     880 B |        1.00 |
| LuceneNet_Dirichlet_TermQuery            | 100000        | 312.4 μs | 0.42 μs | 0.35 μs |  3.04 |    0.00 | 11.7188 | 0.4883 |   50879 B |       57.82 |
| LeanCorpus_JelinekMercer_TermQuery       | 100000        | 159.9 μs | 0.20 μs | 0.18 μs |  1.55 |    0.00 |       - |      - |     880 B |        1.00 |
| LuceneNet_JelinekMercer_TermQuery        | 100000        | 245.6 μs | 0.31 μs | 0.27 μs |  2.39 |    0.00 | 11.7188 | 0.4883 |   50879 B |       57.82 |
| LeanCorpus_AbsoluteDiscounting_TermQuery | 100000        | 172.0 μs | 0.43 μs | 0.40 μs |  1.67 |    0.00 |       - |      - |     880 B |        1.00 |
| LeanCorpus_Dirichlet_BooleanQuery        | 100000        | 434.7 μs | 8.44 μs | 8.29 μs |  4.22 |    0.08 |  3.9063 |      - |   16535 B |       18.79 |
| LeanCorpus_Bm25Plus_TermQuery            | 100000        | 114.4 μs | 0.12 μs | 0.12 μs |  1.11 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LeanCorpus_Bm25L_TermQuery               | 100000        | 116.9 μs | 0.14 μs | 0.13 μs |  1.14 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LeanCorpus_TfIdfAugmented_TermQuery      | 100000        | 115.0 μs | 0.08 μs | 0.07 μs |  1.12 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LeanCorpus_TfIdfPivoted_TermQuery        | 100000        | 110.7 μs | 0.10 μs | 0.09 μs |  1.08 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LeanCorpus_TfIdfDoubleNorm_TermQuery     | 100000        | 113.6 μs | 0.08 μs | 0.07 μs |  1.10 |    0.00 |  0.1221 |      - |     880 B |        1.00 |
| LeanCorpus_Bm25Plus_BooleanQuery         | 100000        | 427.6 μs | 5.31 μs | 4.71 μs |  4.16 |    0.04 |  3.9063 |      - |   16527 B |       18.78 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-similarity"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-similarity" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-similarity" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-similarity" style="max-width:960px"><canvas id="chart-bench-similarity" style="height:500px"></canvas></div>
<p><a href="similarity.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


