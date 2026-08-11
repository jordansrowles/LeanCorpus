---
title: Benchmarks - Analysis filters
---

# Analysis filters

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `6ef0c05` &nbsp;&middot;&nbsp; 9 August 2026 06:18 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                            | Scenario             | Mean      | Error     | StdDev     | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------------- |--------------------- |----------:|----------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **LeanCorpus_Apply**                  | **caching**              |  **3.313 μs** | **0.2058 μs** |  **0.6035 μs** |  **3.376 μs** |  **1.00** |    **0.00** |     **336 B** |        **1.00** |
| LuceneNet_Apply                   | caching              | 18.074 μs | 1.6428 μs |  4.7660 μs | 17.034 μs |  5.65 |    1.85 |    1288 B |        3.83 |
| &#39;LeanCorpus cold filter pipeline&#39; | caching              |  2.931 μs | 0.2246 μs |  0.6552 μs |  2.807 μs |  0.92 |    0.27 |     392 B |        1.17 |
| &#39;Lucene.NET cold filter pipeline&#39; | caching              | 35.120 μs | 3.2982 μs |  9.5685 μs | 34.032 μs | 10.97 |    3.67 |   11104 B |       33.05 |
|                                   |                      |           |           |            |           |       |         |           |             |
| **LeanCorpus_Apply**                  | **classic-noop**         |  **2.743 μs** | **0.2224 μs** |  **0.6451 μs** |  **2.496 μs** |  **1.00** |    **0.00** |      **24 B** |        **1.00** |
| LuceneNet_Apply                   | classic-noop         |  7.313 μs | 0.6940 μs |  2.0024 μs |  6.721 μs |  2.80 |    0.99 |     208 B |        8.67 |
| &#39;LeanCorpus cold filter pipeline&#39; | classic-noop         |  3.039 μs | 0.2248 μs |  0.6522 μs |  2.947 μs |  1.17 |    0.36 |      24 B |        1.00 |
| &#39;Lucene.NET cold filter pipeline&#39; | classic-noop         | 33.087 μs | 3.2680 μs |  9.5846 μs | 31.617 μs | 12.69 |    4.62 |   10424 B |      434.33 |
|                                   |                      |           |           |            |           |       |         |           |             |
| **LeanCorpus_Apply**                  | **hyphenated-words**     |  **3.462 μs** | **0.2700 μs** |  **0.7919 μs** |  **3.321 μs** |  **1.00** |    **0.00** |     **176 B** |        **1.00** |
| LuceneNet_Apply                   | hyphenated-words     |  7.457 μs | 0.7048 μs |  2.0108 μs |  6.616 μs |  2.27 |    0.80 |     240 B |        1.36 |
| &#39;LeanCorpus cold filter pipeline&#39; | hyphenated-words     |  2.985 μs | 0.2288 μs |  0.6709 μs |  2.817 μs |  0.91 |    0.29 |     240 B |        1.36 |
| &#39;Lucene.NET cold filter pipeline&#39; | hyphenated-words     | 32.455 μs | 3.0031 μs |  8.7603 μs | 31.396 μs |  9.86 |    3.49 |   10176 B |       57.82 |
|                                   |                      |           |           |            |           |       |         |           |             |
| **LeanCorpus_Apply**                  | **patte(...)ating [24]** | **10.486 μs** | **0.9748 μs** |  **2.7655 μs** |  **9.864 μs** |  **1.00** |    **0.00** |     **328 B** |        **1.00** |
| LuceneNet_Apply                   | patte(...)ating [24] | 17.989 μs | 1.8245 μs |  5.2933 μs | 16.716 μs |  1.83 |    0.71 |    1408 B |        4.29 |
| &#39;LeanCorpus cold filter pipeline&#39; | patte(...)ating [24] | 10.892 μs | 0.9664 μs |  2.7572 μs | 10.113 μs |  1.11 |    0.39 |     328 B |        1.00 |
| &#39;Lucene.NET cold filter pipeline&#39; | patte(...)ating [24] | 53.077 μs | 4.8216 μs | 14.2165 μs | 51.498 μs |  5.39 |    1.98 |   12792 B |       39.00 |
|                                   |                      |           |           |            |           |       |         |           |             |
| **LeanCorpus_Apply**                  | **pattern-replace-noop** |  **2.946 μs** | **0.2401 μs** |  **0.6967 μs** |  **2.713 μs** |  **1.00** |    **0.00** |      **24 B** |        **1.00** |
| LuceneNet_Apply                   | pattern-replace-noop | 14.975 μs | 1.4819 μs |  4.2756 μs | 13.643 μs |  5.34 |    1.91 |    1296 B |       54.00 |
| &#39;LeanCorpus cold filter pipeline&#39; | pattern-replace-noop |  3.213 μs | 0.2835 μs |  0.8135 μs |  3.185 μs |  1.15 |    0.38 |      24 B |        1.00 |
| &#39;Lucene.NET cold filter pipeline&#39; | pattern-replace-noop | 45.687 μs | 3.8248 μs | 11.0354 μs | 45.727 μs | 16.29 |    5.25 |   12680 B |      528.33 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-analysis-filters"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-analysis-filters" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-analysis-filters" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-analysis-filters" style="max-width:960px"><canvas id="chart-bench-analysis-filters" style="height:500px"></canvas></div>
<p><a href="analysis-filters.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


