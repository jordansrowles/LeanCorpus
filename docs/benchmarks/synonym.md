---
title: Benchmarks - Synonym
---

# Synonym

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                  | SynonymCount | DocumentCount | Mean       | Error   | StdDev  | Ratio | Gen0        | Allocated  | Alloc Ratio |
|------------------------ |------------- |-------------- |-----------:|--------:|--------:|------:|------------:|-----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **10**           | **100000**        |   **884.9 ms** | **2.26 ms** | **2.11 ms** |  **1.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 10           | 100000        |   865.0 ms | 1.42 ms | 1.33 ms |  0.98 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 10           | 100000        | 2,185.4 ms | 2.52 ms | 2.36 ms |  2.47 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 10           | 100000        | 3,069.4 ms | 3.20 ms | 2.99 ms |  3.47 | 222000.0000 |  887.25 MB |      387.64 |
|                         |              |               |            |         |         |       |             |            |             |
| **LeanCorpus_NoSynonyms**   | **50**           | **100000**        |   **938.8 ms** | **1.72 ms** | **1.61 ms** |  **1.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 50           | 100000        |   862.8 ms | 3.73 ms | 3.49 ms |  0.92 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 50           | 100000        | 2,189.0 ms | 2.29 ms | 2.14 ms |  2.33 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 50           | 100000        | 4,184.9 ms | 7.12 ms | 6.66 ms |  4.46 | 401000.0000 | 1599.35 MB |      698.77 |
|                         |              |               |            |         |         |       |             |            |             |
| **LeanCorpus_NoSynonyms**   | **200**          | **100000**        |   **878.3 ms** | **1.45 ms** | **1.28 ms** |  **1.00** |           **-** |    **2.29 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 200          | 100000        |   868.7 ms | 1.89 ms | 1.77 ms |  0.99 |           - |    2.29 MB |        1.00 |
| LuceneNet_NoSynonyms    | 200          | 100000        | 2,203.4 ms | 2.34 ms | 2.08 ms |  2.51 | 144000.0000 |  577.24 MB |      252.20 |
| LuceneNet_WithSynonyms  | 200          | 100000        | 5,453.7 ms | 5.35 ms | 5.00 ms |  6.21 | 545000.0000 | 2175.32 MB |      950.41 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-synonym"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-synonym" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-synonym" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-synonym" style="max-width:960px"><canvas id="chart-bench-synonym" style="height:500px"></canvas></div>
<p><a href="synonym.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


