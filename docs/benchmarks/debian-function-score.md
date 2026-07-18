---
title: Benchmarks - Function score
---

# Function score

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | Mode     | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |---------:|--------:|--------:|------:|---------:|-------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        | **156.8 μs** | **0.24 μs** | **0.20 μs** |  **1.00** |        **-** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 469.3 μs | 0.76 μs | 0.71 μs |  2.99 | 102.0508 |      - |  427256 B |      593.41 |
| LuceneNet_TermQuery           | Max      | 100000        | 195.7 μs | 0.11 μs | 0.09 μs |  1.25 |  14.1602 | 0.4883 |   59884 B |       83.17 |
| LuceneNet_FunctionScoreQuery  | Max      | 100000        | 242.0 μs | 0.31 μs | 0.24 μs |  1.54 |  14.4043 | 0.2441 |   60915 B |       84.60 |
|                               |          |               |          |         |         |       |          |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        | **157.3 μs** | **0.24 μs** | **0.21 μs** |  **1.00** |        **-** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 465.4 μs | 1.92 μs | 1.79 μs |  2.96 | 102.0508 |      - |  427256 B |      593.41 |
| LuceneNet_TermQuery           | Multiply | 100000        | 192.9 μs | 0.24 μs | 0.21 μs |  1.23 |  14.1602 | 0.4883 |   59884 B |       83.17 |
| LuceneNet_FunctionScoreQuery  | Multiply | 100000        | 235.8 μs | 0.35 μs | 0.31 μs |  1.50 |  14.4043 | 0.2441 |   60915 B |       84.60 |
|                               |          |               |          |         |         |       |          |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        | **157.4 μs** | **0.26 μs** | **0.24 μs** |  **1.00** |        **-** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 460.8 μs | 0.54 μs | 0.48 μs |  2.93 | 102.0508 |      - |  427256 B |      593.41 |
| LuceneNet_TermQuery           | Replace  | 100000        | 196.2 μs | 0.21 μs | 0.20 μs |  1.25 |  14.1602 | 0.4883 |   59884 B |       83.17 |
| LuceneNet_FunctionScoreQuery  | Replace  | 100000        | 237.5 μs | 0.21 μs | 0.16 μs |  1.51 |  14.4043 | 0.2441 |   60915 B |       84.60 |
|                               |          |               |          |         |         |       |          |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        | **158.6 μs** | **0.09 μs** | **0.08 μs** |  **1.00** |        **-** |      **-** |     **720 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 466.7 μs | 0.61 μs | 0.57 μs |  2.94 | 102.0508 |      - |  427256 B |      593.41 |
| LuceneNet_TermQuery           | Sum      | 100000        | 190.4 μs | 0.24 μs | 0.21 μs |  1.20 |  14.1602 | 0.4883 |   59884 B |       83.17 |
| LuceneNet_FunctionScoreQuery  | Sum      | 100000        | 236.5 μs | 0.43 μs | 0.38 μs |  1.49 |  14.4043 | 0.2441 |   60915 B |       84.60 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-function-score"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-function-score" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-function-score" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-function-score" style="max-width:960px"><canvas id="chart-bench-function-score" style="height:500px"></canvas></div>
<p><a href="debian-function-score.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


