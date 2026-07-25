---
title: Benchmarks - N-gram
---

# N-gram

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `e3f1d25` &nbsp;&middot;&nbsp; 25 July 2026 07:36 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                        | GramRange | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0        | Allocated   | Alloc Ratio |
|---------------------------------------------- |---------- |-------------- |-----------:|--------:|--------:|------:|--------:|------------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **2-3**       | **100000**        |   **279.9 ms** | **1.93 ms** | **2.07 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 2-3       | 100000        |   309.2 ms | 5.06 ms | 4.97 ms |  1.10 |    0.02 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 2-3       | 100000        |   434.5 ms | 0.73 ms | 0.65 ms |  1.55 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 2-3       | 100000        |   395.2 ms | 0.34 ms | 0.26 ms |  1.41 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 2-3       | 100000        | 1,016.7 ms | 2.48 ms | 2.32 ms |  3.63 |    0.03 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 2-3       | 100000        |   975.3 ms | 0.92 ms | 0.82 ms |  3.48 |    0.02 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 2-3       | 100000        |   910.0 ms | 6.09 ms | 5.69 ms |  3.25 |    0.03 | 211000.0000 | 885600000 B |          NA |
| LuceneNet_NGramTokenizer                      | 2-3       | 100000        | 5,728.3 ms | 3.81 ms | 3.56 ms | 20.46 |    0.14 | 211000.0000 | 885600000 B |          NA |
|                                               |           |               |            |         |         |       |         |             |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **3-5**       | **100000**        |   **285.2 ms** | **0.71 ms** | **0.59 ms** |  **1.00** |    **0.00** |           **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 3-5       | 100000        |   468.7 ms | 0.91 ms | 0.81 ms |  1.64 |    0.00 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 3-5       | 100000        |   455.9 ms | 3.84 ms | 3.60 ms |  1.60 |    0.01 |           - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 3-5       | 100000        |   409.1 ms | 0.17 ms | 0.14 ms |  1.43 |    0.00 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 3-5       | 100000        | 1,431.5 ms | 2.12 ms | 1.98 ms |  5.02 |    0.01 |           - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 3-5       | 100000        | 1,005.0 ms | 7.63 ms | 7.14 ms |  3.52 |    0.03 |           - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 3-5       | 100000        |   905.1 ms | 3.41 ms | 3.03 ms |  3.17 |    0.01 | 212000.0000 | 888000000 B |          NA |
| LuceneNet_NGramTokenizer                      | 3-5       | 100000        | 9,007.3 ms | 3.17 ms | 2.48 ms | 31.58 |    0.06 | 212000.0000 | 888000000 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-ngram"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-ngram" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-ngram" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-ngram" style="max-width:960px"><canvas id="chart-bench-ngram" style="height:500px"></canvas></div>
<p><a href="ngram.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


