---
title: Benchmarks - Indexing
---

# Indexing

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `ad7c40a` &nbsp;&middot;&nbsp; 18 July 2026 14:22 UTC &nbsp;&middot;&nbsp; 500 docs

| Method                    | Job        | IterationCount | LaunchCount | RunStrategy | UnrollFactor | WarmupCount | Profile         | DocumentCount | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0      | Gen1     | Allocated | Alloc Ratio |
|-------------------------- |----------- |--------------- |------------ |------------ |------------- |------------ |---------------- |-------------- |-----------:|----------:|---------:|------:|--------:|----------:|---------:|----------:|------------:|
| **LeanCorpus_IndexDocuments** | **Job-AMZPBM** | **5**              | **Default**     | **Default**     | **16**           | **2**           | **PostingsOnly**    | **500**           |   **164.8 ms** |  **15.72 ms** |  **4.08 ms** |  **1.00** |    **0.00** |  **500.0000** | **250.0000** |   **6.92 MB** |        **1.00** |
| LuceneNet_IndexDocuments  | Job-AMZPBM | 5              | Default     | Default     | 16           | 2           | PostingsOnly    | 500           |   696.8 ms | 275.17 ms | 71.46 ms |  4.23 |    0.41 | 1000.0000 |        - |    7.5 MB |        1.08 |
|                           |            |                |             |             |              |             |                 |               |            |           |          |       |         |           |          |           |             |
| LeanCorpus_IndexDocuments | Dry        | 1              | 1           | ColdStart   | 1            | Default     | PostingsOnly    | 500           |   360.0 ms |        NA |  0.00 ms |  1.00 |    0.00 |         - |        - |   6.95 MB |        1.00 |
| LuceneNet_IndexDocuments  | Dry        | 1              | 1           | ColdStart   | 1            | Default     | PostingsOnly    | 500           | 1,138.0 ms |        NA |  0.00 ms |  3.16 |    0.00 | 1000.0000 |        - |    7.6 MB |        1.09 |
|                           |            |                |             |             |              |             |                 |               |            |           |          |       |         |           |          |           |             |
| **LeanCorpus_IndexDocuments** | **Job-AMZPBM** | **5**              | **Default**     | **Default**     | **16**           | **2**           | **StoredFields**    | **500**           |   **172.1 ms** |  **26.07 ms** |  **6.77 ms** |  **1.00** |    **0.00** |  **666.6667** | **333.3333** |   **7.11 MB** |        **1.00** |
| LuceneNet_IndexDocuments  | Job-AMZPBM | 5              | Default     | Default     | 16           | 2           | StoredFields    | 500           |   705.1 ms | 236.64 ms | 61.46 ms |  4.10 |    0.36 | 1000.0000 |        - |   8.03 MB |        1.13 |
|                           |            |                |             |             |              |             |                 |               |            |           |          |       |         |           |          |           |             |
| LeanCorpus_IndexDocuments | Dry        | 1              | 1           | ColdStart   | 1            | Default     | StoredFields    | 500           |   384.4 ms |        NA |  0.00 ms |  1.00 |    0.00 |         - |        - |   7.15 MB |        1.00 |
| LuceneNet_IndexDocuments  | Dry        | 1              | 1           | ColdStart   | 1            | Default     | StoredFields    | 500           | 1,175.8 ms |        NA |  0.00 ms |  3.06 |    0.00 | 1000.0000 |        - |   8.12 MB |        1.14 |
|                           |            |                |             |             |              |             |                 |               |            |           |          |       |         |           |          |           |             |
| **LeanCorpus_IndexDocuments** | **Job-AMZPBM** | **5**              | **Default**     | **Default**     | **16**           | **2**           | **SortedDocValues** | **500**           |   **166.7 ms** |  **27.18 ms** |  **7.06 ms** |  **1.00** |    **0.00** |  **500.0000** | **250.0000** |   **7.05 MB** |        **1.00** |
| LuceneNet_IndexDocuments  | Job-AMZPBM | 5              | Default     | Default     | 16           | 2           | SortedDocValues | 500           |   779.9 ms | 206.53 ms | 53.63 ms |  4.69 |    0.35 | 1000.0000 |        - |   7.99 MB |        1.13 |
|                           |            |                |             |             |              |             |                 |               |            |           |          |       |         |           |          |           |             |
| LeanCorpus_IndexDocuments | Dry        | 1              | 1           | ColdStart   | 1            | Default     | SortedDocValues | 500           |   373.7 ms |        NA |  0.00 ms |  1.00 |    0.00 |         - |        - |   7.08 MB |        1.00 |
| LuceneNet_IndexDocuments  | Dry        | 1              | 1           | ColdStart   | 1            | Default     | SortedDocValues | 500           | 1,353.4 ms |        NA |  0.00 ms |  3.62 |    0.00 | 1000.0000 |        - |   8.08 MB |        1.14 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-index"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-index" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-index" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-index" style="max-width:960px"><canvas id="chart-bench-index" style="height:500px"></canvas></div>
<p><a href="debian-index.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


