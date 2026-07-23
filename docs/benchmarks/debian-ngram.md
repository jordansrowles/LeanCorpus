---
title: Benchmarks - N-gram
---

# N-gram

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `f3305a5` &nbsp;&middot;&nbsp; 16 July 2026 20:51 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                        | GramRange | DocumentCount | Mean       | Error    | StdDev   | Median     | Ratio | RatioSD | Gen0        | Gen1      | Allocated   | Alloc Ratio |
|---------------------------------------------- |---------- |-------------- |-----------:|---------:|---------:|-----------:|------:|--------:|------------:|----------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **2-3**       | **100000**        |   **221.8 ms** |  **0.27 ms** |  **0.24 ms** |   **221.8 ms** |  **1.00** |    **0.00** |           **-** |         **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 2-3       | 100000        |   188.9 ms |  1.85 ms |  1.64 ms |   189.1 ms |  0.85 |    0.01 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 2-3       | 100000        |   323.1 ms |  0.81 ms |  0.76 ms |   323.0 ms |  1.46 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 2-3       | 100000        |   291.2 ms |  0.52 ms |  0.46 ms |   291.0 ms |  1.31 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 2-3       | 100000        |   607.1 ms |  1.10 ms |  0.92 ms |   606.7 ms |  2.74 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 2-3       | 100000        |   681.5 ms |  2.65 ms |  4.04 ms |   680.2 ms |  3.07 |    0.02 |           - |         - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 2-3       | 100000        |   653.1 ms |  3.19 ms |  2.99 ms |   653.0 ms |  2.94 |    0.01 | 211000.0000 | 1000.0000 | 885600000 B |          NA |
| LuceneNet_NGramTokenizer                      | 2-3       | 100000        | 3,574.3 ms |  1.13 ms |  0.95 ms | 3,574.4 ms | 16.12 |    0.02 | 211000.0000 |         - | 885600000 B |          NA |
|                                               |           |               |            |          |          |            |       |         |             |           |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **3-5**       | **100000**        |   **219.0 ms** |  **0.63 ms** |  **0.59 ms** |   **218.7 ms** |  **1.00** |    **0.00** |           **-** |         **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 3-5       | 100000        |   288.3 ms |  3.56 ms |  3.33 ms |   286.0 ms |  1.32 |    0.02 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 3-5       | 100000        |   331.3 ms |  5.68 ms |  5.31 ms |   333.3 ms |  1.51 |    0.02 |           - |         - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 3-5       | 100000        |   312.1 ms |  4.46 ms |  4.17 ms |   315.5 ms |  1.43 |    0.02 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 3-5       | 100000        |   843.2 ms | 22.95 ms | 66.58 ms |   857.9 ms |  3.85 |    0.30 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 3-5       | 100000        |   689.5 ms |  0.67 ms |  1.09 ms |   689.3 ms |  3.15 |    0.01 |           - |         - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 3-5       | 100000        |   658.1 ms |  2.66 ms |  2.49 ms |   657.4 ms |  3.01 |    0.01 | 212000.0000 |         - | 888000000 B |          NA |
| LuceneNet_NGramTokenizer                      | 3-5       | 100000        | 5,952.9 ms |  2.25 ms |  2.11 ms | 5,953.2 ms | 27.19 |    0.07 | 212000.0000 |         - | 888000000 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-ngram"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-ngram" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-ngram" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-ngram" style="max-width:960px"><canvas id="chart-bench-ngram" style="height:500px"></canvas></div>
<p><a href="debian-ngram.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


