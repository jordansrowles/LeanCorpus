---
title: Benchmarks - compound-file
---

# compound-file

**.NET** 10.0.3 &nbsp;&middot;&nbsp; **Commit** `c4ff69e` &nbsp;&middot;&nbsp; 6 August 2026 20:14 UTC &nbsp;&middot;&nbsp; 100,000 docs

| Method                                     | DocumentCount | Mean            | Error         | StdDev        | Ratio | RatioSD | Gen0        | Gen1        | Gen2       | Allocated     | Alloc Ratio |
|------------------------------------------- |-------------- |----------------:|--------------:|--------------:|------:|--------:|------------:|------------:|-----------:|--------------:|------------:|
| LooseFiles_IndexAndCommit                  | 100000        | 10,538,539.3 μs |  73,698.01 μs |  65,331.37 μs | 1.000 |    0.00 | 299000.0000 | 159000.0000 | 20000.0000 | 2060883.81 KB |       1.000 |
| CompoundFile_IndexAndCommit                | 100000        | 10,538,866.8 μs |  73,263.83 μs |  64,946.47 μs | 1.000 |    0.01 | 302000.0000 | 157000.0000 | 20000.0000 | 2066733.07 KB |       1.003 |
| LooseFiles_IndexCommitUnderMergePressure   | 100000        | 29,048,057.1 μs | 235,381.35 μs | 208,659.44 μs | 2.756 |    0.03 | 722000.0000 | 542000.0000 | 31000.0000 | 5642674.74 KB |       2.738 |
| CompoundFile_IndexCommitUnderMergePressure | 100000        | 19,222,071.0 μs | 213,282.87 μs | 178,100.85 μs | 1.824 |    0.02 | 728000.0000 | 540000.0000 | 26000.0000 | 5701149.09 KB |       2.766 |
| LooseFiles_OpenReader                      | 100000        |     15,202.3 μs |      36.11 μs |      32.01 μs | 0.001 |    0.00 |   1250.0000 |    734.3750 |          - |    6504.27 KB |       0.003 |
| CompoundFile_OpenReader                    | 100000        |     23,495.2 μs |     100.44 μs |      93.95 μs | 0.002 |    0.00 |    968.7500 |    343.7500 |          - |    4004.74 KB |       0.002 |
| LooseFiles_Search                          | 100000        |        183.3 μs |       0.69 μs |       0.65 μs | 0.000 |    0.00 |      3.6621 |           - |          - |      15.27 KB |       0.000 |
| CompoundFile_Search                        | 100000        |        184.4 μs |       0.27 μs |       0.24 μs | 0.000 |    0.00 |      3.6621 |           - |          - |      15.27 KB |       0.000 |
| LooseFiles_StoredFields                    | 100000        |      1,021.1 μs |       3.12 μs |       2.92 μs | 0.000 |    0.00 |     21.4844 |           - |          - |      95.56 KB |       0.000 |
| CompoundFile_StoredFields                  | 100000        |      1,046.5 μs |       2.24 μs |       1.98 μs | 0.000 |    0.00 |     21.4844 |           - |          - |      95.56 KB |       0.000 |
| LooseFiles_VectorSearch                    | 100000        |        667.9 μs |       1.49 μs |       1.40 μs | 0.000 |    0.00 |    234.3750 |           - |          - |     959.13 KB |       0.000 |
| CompoundFile_VectorSearch                  | 100000        |        662.0 μs |       1.92 μs |       1.80 μs | 0.000 |    0.00 |    234.3750 |           - |          - |     959.13 KB |       0.000 |
| LooseFiles_DocValuesAndFacets              | 100000        |        902.5 μs |       2.61 μs |       2.44 μs | 0.000 |    0.00 |     80.0781 |     18.5547 |     0.9766 |     425.03 KB |       0.000 |
| CompoundFile_DocValuesAndFacets            | 100000        |        900.5 μs |       2.13 μs |       2.00 μs | 0.000 |    0.00 |     80.0781 |     18.5547 |     0.9766 |     425.03 KB |       0.000 |

<div class="benchmark-chart">
<p style="margin-bottom:4px"><label>Time scale: <select id="chart-scale-compound-file"><option value="log2" selected>Log2</option><option value="log10">Log10</option><option value="linear">Linear</option></select></label> <label>Width: <input type="range" id="chart-width-compound-file" min="400" max="1400" value="960" step="20" style="vertical-align:middle"></label> <label>Height: <input type="range" id="chart-height-compound-file" min="200" max="900" value="500" step="20" style="vertical-align:middle"></label></p>
<div id="chart-wrap-compound-file" style="max-width:960px"><canvas id="chart-bench-compound-file" style="height:500px"></canvas></div>
<p><a href="compound-file.json">Full results as JSON</a></p>
</div>
<script src="benchmark-charts.js"></script>


