---
title: Benchmarks - mmap-io
---

# mmap-io

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 7 August 2026 08:49 UTC &nbsp;&middot;&nbsp; 20,000 docs

| Method                                    | BlockCount | Mean           | Error        | StdDev       | Median         | Ratio | RatioSD | Gen0       | Allocated   | Alloc Ratio |
|------------------------------------------ |----------- |---------------:|-------------:|-------------:|---------------:|------:|--------:|-----------:|------------:|------------:|
| **&#39;Sequential write (4 KiB blocks)&#39;**         | **1000**       |     **2,477.6 μs** |     **739.1 μs** |     **191.9 μs** |     **2,420.5 μs** |  **1.00** |    **0.00** |          **-** |     **66472 B** |        **1.00** |
| &#39;Sequential read spans (4 KiB blocks)&#39;    | 1000       |       643.3 μs |   2,452.9 μs |     637.0 μs |       347.3 μs |  0.26 |    0.24 |          - |      1016 B |        0.02 |
| &#39;Sequential read arrays (allocation API)&#39; | 1000       |     2,873.8 μs |   4,916.3 μs |   1,276.8 μs |     2,921.8 μs |  1.17 |    0.48 |          - |   4121016 B |       62.00 |
| &#39;Random read spans (page-stride)&#39;         | 1000       |       746.2 μs |   2,780.6 μs |     722.1 μs |       404.2 μs |  0.30 |    0.27 |          - |      1320 B |        0.02 |
| &#39;Byte random read (mmap fault stress)&#39;    | 1000       |       720.2 μs |   2,635.3 μs |     684.4 μs |       387.8 μs |  0.29 |    0.25 |          - |      1320 B |        0.02 |
|                                           |            |                |              |              |                |       |         |            |             |             |
| **&#39;Sequential write (4 KiB blocks)&#39;**         | **10000**      |   **270,707.6 μs** |  **68,187.8 μs** |  **17,708.1 μs** |   **263,925.8 μs** |  **1.00** |    **0.00** |          **-** |       **912 B** |        **1.00** |
| &#39;Sequential read spans (4 KiB blocks)&#39;    | 10000      |     5,213.9 μs |  16,186.4 μs |   4,203.6 μs |     3,264.2 μs |  0.02 |    0.01 |          - |      1016 B |        1.11 |
| &#39;Sequential read arrays (allocation API)&#39; | 10000      |    14,855.8 μs |  24,759.2 μs |   6,429.9 μs |    11,660.7 μs |  0.06 |    0.02 |  9000.0000 |  41201016 B |   45,176.55 |
| &#39;Random read spans (page-stride)&#39;         | 10000      |     6,654.2 μs |  16,995.6 μs |   4,413.7 μs |     4,717.0 μs |  0.02 |    0.02 |          - |      1320 B |        1.45 |
| &#39;Byte random read (mmap fault stress)&#39;    | 10000      |     6,389.5 μs |  17,221.4 μs |   4,472.3 μs |     4,315.6 μs |  0.02 |    0.02 |          - |      1320 B |        1.45 |
|                                           |            |                |              |              |                |       |         |            |             |             |
| **&#39;Sequential write (4 KiB blocks)&#39;**         | **100000**     | **3,290,449.5 μs** | **834,074.7 μs** | **216,606.6 μs** | **3,192,178.9 μs** | **1.000** |    **0.00** |          **-** |       **912 B** |        **1.00** |
| &#39;Sequential read spans (4 KiB blocks)&#39;    | 100000     |    31,784.3 μs |  30,270.9 μs |   7,861.2 μs |    28,420.2 μs | 0.010 |    0.00 |          - |      1016 B |        1.11 |
| &#39;Sequential read arrays (allocation API)&#39; | 100000     |    93,086.2 μs |  38,836.8 μs |  10,085.8 μs |    88,474.8 μs | 0.028 |    0.00 | 98000.0000 | 412001016 B |  451,755.50 |
| &#39;Random read spans (page-stride)&#39;         | 100000     |    40,293.5 μs |  35,945.8 μs |   9,335.0 μs |    35,974.9 μs | 0.012 |    0.00 |          - |      1320 B |        1.45 |
| &#39;Byte random read (mmap fault stress)&#39;    | 100000     |    41,601.8 μs |  34,914.1 μs |   9,067.1 μs |    39,586.5 μs | 0.013 |    0.00 |          - |      1320 B |        1.45 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-mmap-io"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-mmap-io" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-mmap-io" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-mmap-io" style="max-width:960px"><canvas id="chart-bench-mmap-io" style="height:500px"></canvas></div>
<p><a href="mmap-io.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


