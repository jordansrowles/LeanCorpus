---
title: Benchmarks - Function score
---

# Function score

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | Mode     | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        | **105.5 μs** | **0.13 μs** | **0.11 μs** |  **1.00** |  **0.1221** |      **-** |     **880 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 140.6 μs | 0.30 μs | 0.28 μs |  1.33 |       - |      - |     920 B |        1.05 |
| LuceneNet_TermQuery           | Max      | 100000        | 190.2 μs | 0.37 μs | 0.34 μs |  1.80 | 18.3105 | 0.2441 |   77541 B |       88.11 |
| LuceneNet_FunctionScoreQuery  | Max      | 100000        | 221.5 μs | 1.72 μs | 1.60 μs |  2.10 | 18.5547 | 0.2441 |   78878 B |       89.63 |
|                               |          |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        | **104.1 μs** | **0.15 μs** | **0.14 μs** |  **1.00** |  **0.1221** |      **-** |     **880 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 134.2 μs | 0.12 μs | 0.09 μs |  1.29 |       - |      - |     920 B |        1.05 |
| LuceneNet_TermQuery           | Multiply | 100000        | 190.0 μs | 0.41 μs | 0.34 μs |  1.83 | 18.3105 | 0.2441 |   77541 B |       88.11 |
| LuceneNet_FunctionScoreQuery  | Multiply | 100000        | 221.0 μs | 1.43 μs | 1.34 μs |  2.12 | 18.5547 | 0.2441 |   78878 B |       89.63 |
|                               |          |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        | **104.0 μs** | **0.15 μs** | **0.14 μs** |  **1.00** |  **0.1221** |      **-** |     **880 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 132.4 μs | 0.32 μs | 0.30 μs |  1.27 |       - |      - |     920 B |        1.05 |
| LuceneNet_TermQuery           | Replace  | 100000        | 186.2 μs | 0.46 μs | 0.36 μs |  1.79 | 18.3105 | 0.2441 |   77541 B |       88.11 |
| LuceneNet_FunctionScoreQuery  | Replace  | 100000        | 223.8 μs | 0.41 μs | 0.34 μs |  2.15 | 18.5547 | 0.2441 |   78873 B |       89.63 |
|                               |          |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        | **105.5 μs** | **0.14 μs** | **0.12 μs** |  **1.00** |  **0.1221** |      **-** |     **880 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 133.1 μs | 0.25 μs | 0.22 μs |  1.26 |       - |      - |     920 B |        1.05 |
| LuceneNet_TermQuery           | Sum      | 100000        | 190.0 μs | 0.49 μs | 0.38 μs |  1.80 | 18.3105 | 0.2441 |   77541 B |       88.11 |
| LuceneNet_FunctionScoreQuery  | Sum      | 100000        | 225.9 μs | 1.55 μs | 1.45 μs |  2.14 | 18.5547 | 0.2441 |   78878 B |       89.63 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-function-score"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-function-score" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-function-score" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-function-score" style="max-width:960px"><canvas id="chart-bench-function-score" style="height:500px"></canvas></div>
<p><a href="debian-function-score.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


