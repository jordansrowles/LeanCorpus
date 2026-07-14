---
title: Benchmarks - N-gram
---

# N-gram

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c892a43` &nbsp;&middot;&nbsp; 12 July 2026 12:37 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                        | GramRange | DocumentCount | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0        | Gen1      | Allocated   | Alloc Ratio |
|---------------------------------------------- |---------- |-------------- |-----------:|--------:|--------:|------:|--------:|------------:|----------:|------------:|------------:|
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **2-3**       | **100000**        |   **130.7 ms** | **0.14 ms** | **0.11 ms** |  **1.00** |    **0.00** |           **-** |         **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 2-3       | 100000        |   122.5 ms | 0.28 ms | 0.26 ms |  0.94 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 2-3       | 100000        |   203.8 ms | 0.62 ms | 0.58 ms |  1.56 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 2-3       | 100000        |   170.6 ms | 0.27 ms | 0.23 ms |  1.30 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 2-3       | 100000        |   392.5 ms | 0.78 ms | 0.65 ms |  3.00 |    0.01 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 2-3       | 100000        |   439.8 ms | 0.59 ms | 0.49 ms |  3.36 |    0.00 |           - |         - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 2-3       | 100000        |   503.5 ms | 3.73 ms | 3.49 ms |  3.85 |    0.03 | 211000.0000 | 1000.0000 | 885600000 B |          NA |
| LuceneNet_NGramTokenizer                      | 2-3       | 100000        | 4,051.1 ms | 5.36 ms | 4.75 ms | 30.98 |    0.04 | 211000.0000 |         - | 885600000 B |          NA |
|                                               |           |               |            |         |         |       |         |             |           |             |             |
| **LeanCorpus_EdgeNGramTokeniser_SpanSink**        | **3-5**       | **100000**        |   **130.1 ms** | **0.18 ms** | **0.16 ms** |  **1.00** |    **0.00** |           **-** |         **-** |           **-** |          **NA** |
| LeanCorpus_NGramTokeniser_SpanSink            | 3-5       | 100000        |   192.2 ms | 1.57 ms | 1.39 ms |  1.48 |    0.01 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_SpanSink  | 3-5       | 100000        |   219.2 ms | 1.18 ms | 1.11 ms |  1.69 |    0.01 |           - |         - |           - |          NA |
| LeanCorpus_EdgeNGramTokeniser_Streaming       | 3-5       | 100000        |   175.8 ms | 0.22 ms | 0.17 ms |  1.35 |    0.00 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_Streaming           | 3-5       | 100000        |   549.9 ms | 1.25 ms | 1.17 ms |  4.23 |    0.01 |           - |         - |           - |          NA |
| LeanCorpus_NGramTokeniser_WordSplit_Streaming | 3-5       | 100000        |   474.6 ms | 0.53 ms | 0.47 ms |  3.65 |    0.01 |           - |         - |           - |          NA |
| LuceneNet_EdgeNGramTokenizer                  | 3-5       | 100000        |   519.5 ms | 1.55 ms | 1.38 ms |  3.99 |    0.01 | 212000.0000 |         - | 888000000 B |          NA |
| LuceneNet_NGramTokenizer                      | 3-5       | 100000        | 3,621.2 ms | 2.52 ms | 2.24 ms | 27.84 |    0.04 | 212000.0000 |         - | 888000000 B |          NA |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-ngram"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-ngram" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-ngram" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-ngram" style="max-width:960px"><canvas id="chart-bench-ngram" style="height:500px"></canvas></div>
<p><a href="debian-ngram.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


