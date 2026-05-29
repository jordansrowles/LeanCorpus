---
title: Benchmarks - DESKTOP-U6KL8U9
---

# Benchmarks: DESKTOP-U6KL8U9

**.NET** 10.0.7 &nbsp;&middot;&nbsp; **Commit** `7043e32` &nbsp;&middot;&nbsp; 18 May 2026 19:20 UTC &nbsp;&middot;&nbsp; 6 benchmarks

## synonym

| Method                  | SynonymCount | DocumentCount | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated | Alloc Ratio |
|------------------------ |------------- |-------------- |---------:|---------:|---------:|------:|--------:|----------:|----------:|----------:|----------:|------------:|
| **LeanCorpus_NoSynonyms**   | **10**           | **10000**         | **393.7 ms** |  **7.83 ms** | **18.76 ms** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |  **30.83 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 10           | 10000         | 787.3 ms | 15.45 ms | 27.47 ms |  2.00 |    0.12 | 5000.0000 | 3000.0000 | 2000.0000 | 279.84 MB |        9.08 |
|                         |              |               |          |          |          |       |         |           |           |           |           |             |
| **LeanCorpus_NoSynonyms**   | **50**           | **10000**         | **411.0 ms** |  **8.16 ms** | **16.30 ms** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |  **30.83 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 50           | 10000         | 832.7 ms | 16.61 ms | 26.82 ms |  2.03 |    0.10 | 5000.0000 | 3000.0000 | 2000.0000 | 283.77 MB |        9.20 |
|                         |              |               |          |          |          |       |         |           |           |           |           |             |
| **LeanCorpus_NoSynonyms**   | **200**          | **10000**         | **420.3 ms** |  **8.38 ms** | **14.22 ms** |  **1.00** |    **0.00** |         **-** |         **-** |         **-** |  **30.83 MB** |        **1.00** |
| LeanCorpus_WithSynonyms | 200          | 10000         | 856.7 ms | 14.60 ms | 13.66 ms |  2.04 |    0.08 | 5000.0000 | 3000.0000 | 2000.0000 | 286.61 MB |        9.30 |

<details>
<summary>Full data (report.json)</summary>

<pre><code class="lang-json">{
  "schemaVersion": 2,
  "runId": "2026-05-18 19-20 (7043e321)",
  "runType": "full",
  "generatedAtUtc": "2026-05-18T19:20:36.1751019\u002B00:00",
  "commandLineArgs": [
    "--filter",
    "*SynonymBenchmarks*"
  ],
  "hostMachineName": "DESKTOP-U6KL8U9",
  "commitHash": "7043e321",
  "dotnetVersion": "10.0.7",
  "provenance": {
    "sourceCommit": "7043e321",
    "sourceRef": "",
    "sourceManifestPath": "",
    "gitCommitHash": "7043e321",
    "gitAvailable": true,
    "gitDirty": true,
    "benchmarkDotNetVersion": "0.16.0-nightly.20260427.506\u002Bc68dc1556c410c4bdfe21373c7689be5781fbaf9",
    "runtimeFramework": ".NET 10.0.7",
    "runtimeIdentifier": "win-x64",
    "osDescription": "Microsoft Windows 10.0.19045",
    "processArchitecture": "X64",
    "effectiveDocCount": 10000,
    "dataFingerprintSha256": "",
    "dataSources": []
  },
  "totalBenchmarkCount": 6,
  "suites": [
    {
      "suiteName": "synonym",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.SynonymBenchmarks-20260518-202158",
      "benchmarkCount": 6,
      "benchmarks": [
        {
          "key": "SynonymBenchmarks.LeanCorpus_NoSynonyms|DocumentCount=10000, SynonymCount=10",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_NoSynonyms: DefaultJob [SynonymCount=10, DocumentCount=10000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_NoSynonyms",
          "parameters": {
            "SynonymCount": "10",
            "DocumentCount": "10000"
          },
          "statistics": {
            "sampleCount": 68,
            "meanNanoseconds": 393693763.2352941,
            "medianNanoseconds": 395023900,
            "minNanoseconds": 358247200,
            "maxNanoseconds": 432507100,
            "standardDeviationNanoseconds": 18757503.528470106,
            "operationsPerSecond": 2.540045317919711
          },
          "gc": {
            "bytesAllocatedPerOperation": 32331568,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_NoSynonyms|DocumentCount=10000, SynonymCount=200",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_NoSynonyms: DefaultJob [SynonymCount=200, DocumentCount=10000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_NoSynonyms",
          "parameters": {
            "SynonymCount": "200",
            "DocumentCount": "10000"
          },
          "statistics": {
            "sampleCount": 37,
            "meanNanoseconds": 420270737.8378378,
            "medianNanoseconds": 422220900,
            "minNanoseconds": 385365700,
            "maxNanoseconds": 441989100,
            "standardDeviationNanoseconds": 14223679.875716476,
            "operationsPerSecond": 2.379418574666152
          },
          "gc": {
            "bytesAllocatedPerOperation": 32331568,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_NoSynonyms|DocumentCount=10000, SynonymCount=50",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_NoSynonyms: DefaultJob [SynonymCount=50, DocumentCount=10000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_NoSynonyms",
          "parameters": {
            "SynonymCount": "50",
            "DocumentCount": "10000"
          },
          "statistics": {
            "sampleCount": 49,
            "meanNanoseconds": 410959010.20408165,
            "medianNanoseconds": 415307200,
            "minNanoseconds": 370903500,
            "maxNanoseconds": 440860800,
            "standardDeviationNanoseconds": 16298194.860861188,
            "operationsPerSecond": 2.4333327051362166
          },
          "gc": {
            "bytesAllocatedPerOperation": 32331568,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_WithSynonyms|DocumentCount=10000, SynonymCount=10",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_WithSynonyms: DefaultJob [SynonymCount=10, DocumentCount=10000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_WithSynonyms",
          "parameters": {
            "SynonymCount": "10",
            "DocumentCount": "10000"
          },
          "statistics": {
            "sampleCount": 40,
            "meanNanoseconds": 787291150,
            "medianNanoseconds": 778507200,
            "minNanoseconds": 757024700,
            "maxNanoseconds": 851528300,
            "standardDeviationNanoseconds": 27468628.828323506,
            "operationsPerSecond": 1.2701781291457424
          },
          "gc": {
            "bytesAllocatedPerOperation": 293429072,
            "gen0Collections": 5,
            "gen1Collections": 3,
            "gen2Collections": 2
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_WithSynonyms|DocumentCount=10000, SynonymCount=200",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_WithSynonyms: DefaultJob [SynonymCount=200, DocumentCount=10000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_WithSynonyms",
          "parameters": {
            "SynonymCount": "200",
            "DocumentCount": "10000"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 856665200,
            "medianNanoseconds": 860080200,
            "minNanoseconds": 834888300,
            "maxNanoseconds": 874781500,
            "standardDeviationNanoseconds": 13660915.30927349,
            "operationsPerSecond": 1.167317173616951
          },
          "gc": {
            "bytesAllocatedPerOperation": 300533424,
            "gen0Collections": 5,
            "gen1Collections": 3,
            "gen2Collections": 2
          }
        },
        {
          "key": "SynonymBenchmarks.LeanCorpus_WithSynonyms|DocumentCount=10000, SynonymCount=50",
          "displayInfo": "SynonymBenchmarks.LeanCorpus_WithSynonyms: DefaultJob [SynonymCount=50, DocumentCount=10000]",
          "typeName": "SynonymBenchmarks",
          "methodName": "LeanCorpus_WithSynonyms",
          "parameters": {
            "SynonymCount": "50",
            "DocumentCount": "10000"
          },
          "statistics": {
            "sampleCount": 34,
            "meanNanoseconds": 832706435.2941177,
            "medianNanoseconds": 825376600,
            "minNanoseconds": 803008100,
            "maxNanoseconds": 898433400,
            "standardDeviationNanoseconds": 26822625.704644278,
            "operationsPerSecond": 1.2009034127937213
          },
          "gc": {
            "bytesAllocatedPerOperation": 297550496,
            "gen0Collections": 5,
            "gen1Collections": 3,
            "gen2Collections": 2
          }
        }
      ]
    }
  ]
}</code></pre>

</details>

