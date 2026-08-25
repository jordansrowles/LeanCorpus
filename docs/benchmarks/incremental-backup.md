---
title: Benchmarks - incremental-backup
---

# incremental-backup

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 7 August 2026 11:54 UTC &nbsp;&middot;&nbsp; 500 docs

| Method                           | UseCompoundFile | DocumentCount | Mean        | Error       | StdDev    | Median       | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------------------------------- |---------------- |-------------- |------------:|------------:|----------:|-------------:|------:|--------:|----------:|------------:|
| **FullBackup**                       | **False**           | **500**           | **1,777.50 ms** | **1,013.53 ms** |  **55.56 ms** | **1,772.068 ms** |  **1.00** |    **0.00** | **258.27 KB** |        **1.00** |
| IncrementalBackup_SmallDelta     | False           | 500           | 1,172.00 ms | 6,260.06 ms | 343.13 ms | 1,169.446 ms |  0.66 |    0.17 | 235.73 KB |        0.91 |
| IncrementalBackup_Unchanged      | False           | 500           |   112.38 ms | 1,288.67 ms |  70.64 ms |    71.778 ms |  0.06 |    0.03 | 157.74 KB |        0.61 |
| ValidateFullBackup               | False           | 500           |    23.34 ms |   680.96 ms |  37.33 ms |     2.002 ms |  0.01 |    0.02 |  42.26 KB |        0.16 |
| ValidateBackupChain              | False           | 500           |    25.27 ms |   728.16 ms |  39.91 ms |     2.342 ms |  0.01 |    0.02 | 107.95 KB |        0.42 |
| RestoreFullBackup                | False           | 500           |   938.62 ms | 2,214.20 ms | 121.37 ms |   948.360 ms |  0.53 |    0.06 | 118.64 KB |        0.46 |
| RestoreBackupChain               | False           | 500           | 1,708.98 ms | 2,678.20 ms | 146.80 ms | 1,689.807 ms |  0.96 |    0.08 | 267.45 KB |        1.04 |
| RestoreFullBackup_WithValidation | False           | 500           |   930.73 ms | 2,544.24 ms | 139.46 ms |   936.142 ms |  0.52 |    0.07 |  203.7 KB |        0.79 |
|                                  |                 |               |             |             |           |              |       |         |           |             |
| **FullBackup**                       | **True**            | **500**           |   **466.54 ms** | **1,360.26 ms** |  **74.56 ms** |   **430.227 ms** |  **1.00** |    **0.00** | **102.24 KB** |        **1.00** |
| IncrementalBackup_SmallDelta     | True            | 500           |   333.36 ms | 1,653.24 ms |  90.62 ms |   282.426 ms |  0.73 |    0.20 | 104.71 KB |        1.02 |
| IncrementalBackup_Unchanged      | True            | 500           |   111.20 ms | 1,254.48 ms |  68.76 ms |    71.593 ms |  0.24 |    0.13 |  82.66 KB |        0.81 |
| ValidateFullBackup               | True            | 500           |    22.58 ms |   671.37 ms |  36.80 ms |     1.439 ms |  0.05 |    0.07 |  18.78 KB |        0.18 |
| ValidateBackupChain              | True            | 500           |    25.75 ms |   762.30 ms |  41.78 ms |     1.748 ms |  0.06 |    0.08 |  44.66 KB |        0.44 |
| RestoreFullBackup                | True            | 500           |   302.92 ms |   982.94 ms |  53.88 ms |   271.975 ms |  0.66 |    0.13 |  50.27 KB |        0.49 |
| RestoreBackupChain               | True            | 500           |   489.84 ms | 2,397.37 ms | 131.41 ms |   430.830 ms |  1.07 |    0.28 |  88.92 KB |        0.87 |
| RestoreFullBackup_WithValidation | True            | 500           |   345.99 ms | 1,977.16 ms | 108.37 ms |   306.818 ms |  0.75 |    0.23 |  90.02 KB |        0.88 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-incremental-backup"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-incremental-backup" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-incremental-backup" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-incremental-backup" style="max-width:960px"><canvas id="chart-bench-incremental-backup" style="height:500px"></canvas></div>
<p><a href="incremental-backup.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


