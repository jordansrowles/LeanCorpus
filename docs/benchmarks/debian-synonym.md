---
title: Benchmarks - Synonym
---

# Synonym

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | SynonymCount | DocumentCount | Mean       | Error    | StdDev    | Median     | Ratio | RatioSD | Gen0        | Allocated | Alloc Ratio |
|------------------------ |------------- |-------------- |-----------:|---------:|----------:|-----------:|------:|--------:|------------:|----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **10**           | **100000**        |   **313.5 ms** |  **1.52 ms** |   **1.35 ms** |   **313.2 ms** |  **1.00** |    **0.00** |    **500.0000** |   **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 10           | 100000        |   337.0 ms |  1.35 ms |   1.27 ms |   337.9 ms |  1.07 |    0.01 |           - |   2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 10           | 100000        |   790.5 ms |  4.72 ms |   4.42 ms |   791.4 ms |  2.52 |    0.02 |  52000.0000 | 209.27 MB |       91.43 |
| LuceneNet_WithSynonyms  | 10           | 100000        | 1,412.8 ms | 47.37 ms | 139.67 ms | 1,315.8 ms |  4.51 |    0.44 | 124000.0000 | 496.37 MB |      216.87 |
|                         |              |               |            |          |           |            |       |         |             |           |             |
| **LeanCorpus_NoSynonyms**   | **50**           | **100000**        |   **313.2 ms** |  **1.28 ms** |   **1.20 ms** |   **312.6 ms** |  **1.00** |    **0.00** |    **500.0000** |   **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 50           | 100000        |   318.3 ms |  0.79 ms |   0.74 ms |   318.3 ms |  1.02 |    0.00 |    500.0000 |   2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 50           | 100000        |   791.2 ms |  2.19 ms |   1.94 ms |   790.9 ms |  2.53 |    0.01 |  52000.0000 | 209.27 MB |       91.43 |
| LuceneNet_WithSynonyms  | 50           | 100000        | 1,406.6 ms |  1.55 ms |   1.45 ms | 1,406.3 ms |  4.49 |    0.02 | 137000.0000 | 546.68 MB |      238.85 |
|                         |              |               |            |          |           |            |       |         |             |           |             |
| **LeanCorpus_NoSynonyms**   | **200**          | **100000**        |   **309.4 ms** |  **1.97 ms** |   **1.84 ms** |   **309.4 ms** |  **1.00** |    **0.00** |    **500.0000** |   **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 200          | 100000        |   308.3 ms |  1.03 ms |   0.80 ms |   307.9 ms |  1.00 |    0.01 |    500.0000 |   2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 200          | 100000        |   784.1 ms |  1.14 ms |   1.01 ms |   784.0 ms |  2.53 |    0.01 |  52000.0000 | 209.27 MB |       91.43 |
| LuceneNet_WithSynonyms  | 200          | 100000        | 1,429.7 ms |  1.81 ms |   1.51 ms | 1,429.4 ms |  4.62 |    0.03 | 140000.0000 | 560.31 MB |      244.80 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-synonym"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-synonym" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-synonym" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-synonym" style="max-width:960px"><canvas id="chart-bench-synonym" style="height:500px"></canvas></div>
<p><a href="debian-synonym.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


