---
title: Benchmarks - N-gram
---

# N-gram

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                        | GramRange | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0        | Allocated   | Alloc Ratio |
|---------------------------------------------- |---------- |-------------- |-----------:|--------:|--------:|------:|--------:|------------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **2-3**       | **100000**        |   **285.6 ms** | **3.69 ms** | **3.45 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 2-3       | 100000        |   314.2 ms | 6.08 ms | 7.24 ms |  1.10 |    0.03 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 2-3       | 100000        |   436.3 ms | 0.41 ms | 0.34 ms |  1.53 |    0.02 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 2-3       | 100000        |   394.5 ms | 0.52 ms | 0.46 ms |  1.38 |    0.02 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 2-3       | 100000        | 1,009.3 ms | 2.48 ms | 2.32 ms |  3.53 |    0.04 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 2-3       | 100000        |   963.3 ms | 0.72 ms | 0.60 ms |  3.37 |    0.04 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 2-3       | 100000        |   912.0 ms | 6.24 ms | 5.83 ms |  3.19 |    0.04 | 211000.0000 | 885600000 B |          NA |
| LuceneNet_NGramTokenizer                      | 2-3       | 100000        | 5,644.3 ms | 4.03 ms | 3.58 ms | 19.76 |    0.23 | 211000.0000 | 885600000 B |          NA |
|                                               |           |               |            |         |         |       |         |             |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **3-5**       | **100000**        |   **288.0 ms** | **1.00 ms** | **0.93 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 3-5       | 100000        |   470.2 ms | 1.38 ms | 1.29 ms |  1.63 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 3-5       | 100000        |   453.3 ms | 0.51 ms | 0.45 ms |  1.57 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 3-5       | 100000        |   435.9 ms | 0.68 ms | 0.53 ms |  1.51 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 3-5       | 100000        | 1,434.1 ms | 2.02 ms | 1.89 ms |  4.98 |    0.02 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 3-5       | 100000        |   985.6 ms | 1.80 ms | 1.69 ms |  3.42 |    0.01 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 3-5       | 100000        |   911.1 ms | 2.38 ms | 2.11 ms |  3.16 |    0.01 | 212000.0000 | 888000000 B |          NA |
| LuceneNet_NGramTokenizer                      | 3-5       | 100000        | 9,333.5 ms | 6.05 ms | 5.36 ms | 32.40 |    0.10 | 212000.0000 | 888000000 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-ngram"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-ngram" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-ngram" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-ngram" style="max-width:960px"><canvas id="chart-bench-ngram" style="height:500px"></canvas></div>
<p><a href="ngram.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


