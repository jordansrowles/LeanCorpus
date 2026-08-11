---
title: Benchmarks - Function score
---

# Function score

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                        | Mode     | DocumentCount | Mean     | Error   | StdDev  | Ratio | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------------ |--------- |-------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| **LeanCorpus_BaseTermQuery**      | **Max**      | **100000**        | **109.1 μs** | **0.13 μs** | **0.12 μs** |  **1.00** |  **0.1221** |      **-** |     **808 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Max      | 100000        | 145.2 μs | 0.19 μs | 0.16 μs |  1.33 |       - |      - |     880 B |        1.09 |
| LuceneNet_TermQuery           | Max      | 100000        | 188.9 μs | 0.44 μs | 0.39 μs |  1.73 | 18.3105 | 0.2441 |   77541 B |       95.97 |
| LuceneNet_FunctionScoreQuery  | Max      | 100000        | 223.9 μs | 1.52 μs | 1.42 μs |  2.05 | 18.5547 | 0.2441 |   78878 B |       97.62 |
|                               |          |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Multiply** | **100000**        | **109.8 μs** | **0.10 μs** | **0.09 μs** |  **1.00** |  **0.1221** |      **-** |     **808 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Multiply | 100000        | 139.2 μs | 0.31 μs | 0.29 μs |  1.27 |       - |      - |     880 B |        1.09 |
| LuceneNet_TermQuery           | Multiply | 100000        | 187.4 μs | 0.32 μs | 0.30 μs |  1.71 | 18.3105 | 0.2441 |   77541 B |       95.97 |
| LuceneNet_FunctionScoreQuery  | Multiply | 100000        | 224.9 μs | 1.56 μs | 1.46 μs |  2.05 | 18.5547 | 0.2441 |   78878 B |       97.62 |
|                               |          |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Replace**  | **100000**        | **109.4 μs** | **0.10 μs** | **0.09 μs** |  **1.00** |  **0.1221** |      **-** |     **808 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Replace  | 100000        | 135.6 μs | 0.31 μs | 0.28 μs |  1.24 |       - |      - |     880 B |        1.09 |
| LuceneNet_TermQuery           | Replace  | 100000        | 185.2 μs | 0.44 μs | 0.36 μs |  1.69 | 18.3105 | 0.2441 |   77541 B |       95.97 |
| LuceneNet_FunctionScoreQuery  | Replace  | 100000        | 225.4 μs | 1.56 μs | 1.46 μs |  2.06 | 18.5547 | 0.2441 |   78878 B |       97.62 |
|                               |          |               |          |         |         |       |         |        |           |             |
| **LeanCorpus_BaseTermQuery**      | **Sum**      | **100000**        | **109.7 μs** | **0.14 μs** | **0.13 μs** |  **1.00** |  **0.1221** |      **-** |     **808 B** |        **1.00** |
| LeanCorpus_FunctionScoreQuery | Sum      | 100000        | 137.3 μs | 0.18 μs | 0.15 μs |  1.25 |       - |      - |     880 B |        1.09 |
| LuceneNet_TermQuery           | Sum      | 100000        | 189.1 μs | 0.43 μs | 0.38 μs |  1.72 | 18.3105 | 0.2441 |   77541 B |       95.97 |
| LuceneNet_FunctionScoreQuery  | Sum      | 100000        | 225.3 μs | 1.53 μs | 1.43 μs |  2.05 | 18.5547 | 0.2441 |   78878 B |       97.62 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-function-score"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-function-score" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-function-score" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-function-score" style="max-width:960px"><canvas id="chart-bench-function-score" style="height:500px"></canvas></div>
<p><a href="function-score.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


