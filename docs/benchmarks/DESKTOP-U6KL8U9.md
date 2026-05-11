---
title: Benchmarks - DESKTOP-U6KL8U9
---

# Benchmarks: DESKTOP-U6KL8U9

**.NET** 10.0.6 &nbsp;&middot;&nbsp; **Commit** `1bee180` &nbsp;&middot;&nbsp; 28 April 2026 07:53 UTC &nbsp;&middot;&nbsp; 15 benchmarks

## gutenberg-search

| Method                     | SearchTerm | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Gen0   | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------:|----------:|----------:|----------:|------:|--------:|-------:|-------:|----------:|------------:|
| **LeanCorpus_Standard_Search** | **death**      |  **9.680 μs** | **0.0922 μs** | **0.0817 μs** |  **9.669 μs** |  **1.00** |    **0.00** |      **-** |      **-** |     **472 B** |        **1.00** |
| LeanCorpus_English_Search  | death      |  9.637 μs | 0.0390 μs | 0.0365 μs |  9.638 μs |  1.00 |    0.01 |      - |      - |     472 B |        1.00 |
| LuceneNet_Search           | death      | 15.235 μs | 0.1571 μs | 0.1392 μs | 15.239 μs |  1.57 |    0.02 | 0.1526 | 0.0153 |   11231 B |       23.79 |
|                            |            |           |           |           |           |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **love**       | **12.620 μs** | **0.0541 μs** | **0.0506 μs** | **12.614 μs** |  **1.00** |    **0.00** |      **-** |      **-** |     **464 B** |        **1.00** |
| LeanCorpus_English_Search  | love       | 16.882 μs | 0.0990 μs | 0.0926 μs | 16.885 μs |  1.34 |    0.01 |      - |      - |     464 B |        1.00 |
| LuceneNet_Search           | love       | 23.896 μs | 0.7845 μs | 2.3131 μs | 24.098 μs |  1.89 |    0.18 | 0.1526 | 0.0305 |   11180 B |       24.09 |
|                            |            |           |           |           |           |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **man**        | **58.632 μs** | **2.9506 μs** | **8.7000 μs** | **58.572 μs** |  **1.00** |    **0.00** |      **-** |      **-** |     **464 B** |        **1.00** |
| LeanCorpus_English_Search  | man        | 59.109 μs | 1.0303 μs | 0.9133 μs | 59.182 μs |  1.03 |    0.15 |      - |      - |     464 B |        1.00 |
| LuceneNet_Search           | man        | 58.660 μs | 1.4793 μs | 4.3617 μs | 56.669 μs |  1.02 |    0.17 | 0.1221 |      - |   11064 B |       23.84 |
|                            |            |           |           |           |           |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **night**      | **32.269 μs** | **0.2541 μs** | **0.2252 μs** | **32.216 μs** |  **1.00** |    **0.00** |      **-** |      **-** |     **472 B** |        **1.00** |
| LeanCorpus_English_Search  | night      | 34.232 μs | 0.6609 μs | 0.7867 μs | 33.983 μs |  1.06 |    0.02 |      - |      - |     472 B |        1.00 |
| LuceneNet_Search           | night      | 39.239 μs | 0.6468 μs | 0.5401 μs | 38.975 μs |  1.22 |    0.02 | 0.1221 |      - |   11228 B |       23.79 |
|                            |            |           |           |           |           |       |         |        |        |           |             |
| **LeanCorpus_Standard_Search** | **sea**        | **16.227 μs** | **0.2988 μs** | **0.2795 μs** | **16.189 μs** |  **1.00** |    **0.00** |      **-** |      **-** |     **464 B** |        **1.00** |
| LeanCorpus_English_Search  | sea        | 17.221 μs | 0.3439 μs | 0.7024 μs | 17.516 μs |  1.06 |    0.05 |      - |      - |     464 B |        1.00 |
| LuceneNet_Search           | sea        | 30.512 μs | 0.7069 μs | 2.0843 μs | 31.505 μs |  1.88 |    0.13 | 0.1221 |      - |   11276 B |       24.30 |

<details>
<summary>Full data (report.json)</summary>

<pre><code class="lang-json">{
  "schemaVersion": 2,
  "runId": "2026-04-28 07-53 (1bee180)",
  "runType": "full",
  "generatedAtUtc": "2026-04-28T07:53:06.7237019\u002B00:00",
  "commandLineArgs": [
    "--filter",
    "*LeanCorpus*"
  ],
  "hostMachineName": "DESKTOP-U6KL8U9",
  "commitHash": "1bee180",
  "dotnetVersion": "10.0.6",
  "totalBenchmarkCount": 15,
  "suites": [
    {
      "suiteName": "gutenberg-search",
      "summaryTitle": "Rowles.LeanCorpus.Benchmarks.GutenbergSearchBenchmarks-20260428-085333",
      "benchmarkCount": 15,
      "benchmarks": [
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 9637.479146321615,
            "medianNanoseconds": 9638.359069824219,
            "minNanoseconds": 9569.157409667969,
            "maxNanoseconds": 9697.431945800781,
            "standardDeviationNanoseconds": 36.51537098366925,
            "operationsPerSecond": 103761.57341742991
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 16881.677856445312,
            "medianNanoseconds": 16885.354614257812,
            "minNanoseconds": 16602.035522460938,
            "maxNanoseconds": 17006.787109375,
            "standardDeviationNanoseconds": 92.59216179042453,
            "operationsPerSecond": 59235.8181753958
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 59108.558872767855,
            "medianNanoseconds": 59181.793212890625,
            "minNanoseconds": 57504.833984375,
            "maxNanoseconds": 60853.38134765625,
            "standardDeviationNanoseconds": 913.3089956318462,
            "operationsPerSecond": 16918.02370199071
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 21,
            "meanNanoseconds": 34232.20418294271,
            "medianNanoseconds": 33982.977294921875,
            "minNanoseconds": 33523.2421875,
            "maxNanoseconds": 36563.604736328125,
            "standardDeviationNanoseconds": 786.7006706684607,
            "operationsPerSecond": 29212.258569615624
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_English_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_English_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_English_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 51,
            "meanNanoseconds": 17221.072268018535,
            "medianNanoseconds": 17516.265869140625,
            "minNanoseconds": 16126.626586914062,
            "maxNanoseconds": 18458.721923828125,
            "standardDeviationNanoseconds": 702.4234457393964,
            "operationsPerSecond": 58068.39344476315
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 9680.069187709263,
            "medianNanoseconds": 9668.987274169922,
            "minNanoseconds": 9509.5947265625,
            "maxNanoseconds": 9802.38037109375,
            "standardDeviationNanoseconds": 81.69700587699776,
            "operationsPerSecond": 103305.04675211363
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 12620.273030598959,
            "medianNanoseconds": 12613.661193847656,
            "minNanoseconds": 12545.846557617188,
            "maxNanoseconds": 12711.805725097656,
            "standardDeviationNanoseconds": 50.627365719390774,
            "operationsPerSecond": 79237.58840838168
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 100,
            "meanNanoseconds": 58631.69073486328,
            "medianNanoseconds": 58572.418212890625,
            "minNanoseconds": 46019.390869140625,
            "maxNanoseconds": 76204.11987304688,
            "standardDeviationNanoseconds": 8699.99163023602,
            "operationsPerSecond": 17055.622777826276
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 32268.93310546875,
            "medianNanoseconds": 32216.134643554688,
            "minNanoseconds": 32009.82666015625,
            "maxNanoseconds": 32751.8798828125,
            "standardDeviationNanoseconds": 225.21002087287354,
            "operationsPerSecond": 30989.5588035579
          },
          "gc": {
            "bytesAllocatedPerOperation": 472,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LeanCorpus_Standard_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LeanCorpus_Standard_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 15,
            "meanNanoseconds": 16226.582438151041,
            "medianNanoseconds": 16188.83056640625,
            "minNanoseconds": 15932.244873046875,
            "maxNanoseconds": 16852.618408203125,
            "standardDeviationNanoseconds": 279.5029189406637,
            "operationsPerSecond": 61627.27141168404
          },
          "gc": {
            "bytesAllocatedPerOperation": 464,
            "gen0Collections": 0,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=death",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=death]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "death"
          },
          "statistics": {
            "sampleCount": 14,
            "meanNanoseconds": 15235.112871442523,
            "medianNanoseconds": 15238.751220703125,
            "minNanoseconds": 15061.909484863281,
            "maxNanoseconds": 15509.309387207031,
            "standardDeviationNanoseconds": 139.22892678932422,
            "operationsPerSecond": 65637.84649567325
          },
          "gc": {
            "bytesAllocatedPerOperation": 11231,
            "gen0Collections": 10,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=love",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=love]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "love"
          },
          "statistics": {
            "sampleCount": 100,
            "meanNanoseconds": 23895.65936279297,
            "medianNanoseconds": 24097.625732421875,
            "minNanoseconds": 19566.36962890625,
            "maxNanoseconds": 28634.5703125,
            "standardDeviationNanoseconds": 2313.072650248674,
            "operationsPerSecond": 41848.60458619787
          },
          "gc": {
            "bytesAllocatedPerOperation": 11180,
            "gen0Collections": 5,
            "gen1Collections": 1,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=man",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=man]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "man"
          },
          "statistics": {
            "sampleCount": 100,
            "meanNanoseconds": 58660.48718261719,
            "medianNanoseconds": 56669.189453125,
            "minNanoseconds": 54247.021484375,
            "maxNanoseconds": 71492.431640625,
            "standardDeviationNanoseconds": 4361.675913856903,
            "operationsPerSecond": 17047.250168360843
          },
          "gc": {
            "bytesAllocatedPerOperation": 11064,
            "gen0Collections": 1,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=night",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=night]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "night"
          },
          "statistics": {
            "sampleCount": 13,
            "meanNanoseconds": 39239.43105844351,
            "medianNanoseconds": 38975.225830078125,
            "minNanoseconds": 38772.564697265625,
            "maxNanoseconds": 40483.26416015625,
            "standardDeviationNanoseconds": 540.1217078096262,
            "operationsPerSecond": 25484.56929741393
          },
          "gc": {
            "bytesAllocatedPerOperation": 11228,
            "gen0Collections": 2,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        },
        {
          "key": "GutenbergSearchBenchmarks.LuceneNet_Search|SearchTerm=sea",
          "displayInfo": "GutenbergSearchBenchmarks.LuceneNet_Search: DefaultJob [SearchTerm=sea]",
          "typeName": "GutenbergSearchBenchmarks",
          "methodName": "LuceneNet_Search",
          "parameters": {
            "SearchTerm": "sea"
          },
          "statistics": {
            "sampleCount": 100,
            "meanNanoseconds": 30511.944213867188,
            "medianNanoseconds": 31505.18798828125,
            "minNanoseconds": 27562.945556640625,
            "maxNanoseconds": 34832.373046875,
            "standardDeviationNanoseconds": 2084.317839458578,
            "operationsPerSecond": 32774.05048300777
          },
          "gc": {
            "bytesAllocatedPerOperation": 11276,
            "gen0Collections": 2,
            "gen1Collections": 0,
            "gen2Collections": 0
          }
        }
      ]
    }
  ]
}</code></pre>

</details>

