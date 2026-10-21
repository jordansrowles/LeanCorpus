# Backup and restore measurement results

Thirty fixed-seed net10.0 reports passed: five configurations, three fresh processes per configuration on each platform, 100 actual FsCheck executions and 3000 model operations per process. Backup and restore remained durable in every configuration. These are local diagnostic measurements, with unchanged model assertions, rather than a release throughput benchmark.

## Source and workload

Measured base: `1c82a2d6bd8e1cea8f257aafc59c377953d26a58`; working-source fingerprint: `38c52001c409570f913b37f4bddad70b76da6326e51df5a68de0d1ec194ad542`. This source was subsequently committed as `646deea5e`. Seed: `14854304604442296793`; gamma: `3998140771831394483`; operation fingerprint: `fdd5f30f1885e22af0992e63e102cefd8c8cabca844cc09bbde1a139ee1663e5`. Start/end size 1..30, maximum trace length 30, MaxBufferedDocs=3, NoMergePolicy and KeepLastNCommitsPolicy(64) were retained. Per-host assembly/runtime hashes were consistent across all cells; cross-platform binaries need not be identical.

Commands: `./devops test backup-restore-matrix -Framework net10.0 -NoRestore` and `.\devops.ps1 test backup-restore-matrix -Framework net10.0 -NoRestore -SourceManifest C:\source\issue87-38c52001c409\source-manifest.json`. Count=3 and normal iterations=100 were retained. Linux completed at 03:56:49 UTC on 14 October 2026; Windows ran from 08:02:41 to 13:24:58 UTC. Both matrix wrappers passed 15/15; the Windows scheduled controller exited zero and its task returned Ready/result 0.

## Matrix

Values are median seconds with minimum and maximum of three launches in parentheses. Sync share adds only disjoint file.sync, file.flush and directory.sync request scopes. Other category timings are inclusive and must not be summed.

| Platform | Configuration | Elapsed seconds | Sync request share, percent |
| --- | --- | --- | --- |
| Linux | baseline | 1527.765 (1509.171 to 1530.230) | 97.48 (97.40 to 97.49) |
| Linux | compound | 586.630 (582.370 to 589.961) | 96.01 (95.97 to 96.09) |
| Linux | source-nondurable | 1418.792 (1413.276 to 1435.870) | 97.48 (97.44 to 97.53) |
| Linux | combined | 454.415 (448.056 to 458.882) | 95.13 (95.12 to 95.21) |
| Linux | alternate-temp | 1181.373 (1157.680 to 1192.404) | 96.82 (96.80 to 96.87) |
| Windows | baseline | 1695.547 (1650.942 to 2208.904) | 91.92 (91.79 to 93.61) |
| Windows | compound | 588.119 (587.700 to 787.345) | 88.85 (88.81 to 91.06) |
| Windows | source-nondurable | 1590.974 (1581.887 to 1638.757) | 91.63 (91.50 to 91.66) |
| Windows | combined | 496.183 (492.930 to 496.961) | 87.13 (86.87 to 87.15) |
| Windows | alternate-temp | 1719.954 (1682.352 to 1726.803) | 92.23 (91.97 to 92.38) |

## Measured categories

Baseline counts are the same on both platforms. Durations below are median seconds. Full counts, bytes, retry delays and ranges for all variants are retained in the investigation evidence.

| Category | Count per process | Linux seconds | Windows seconds |
| --- | --- | --- | --- |
| file.sync | 8353 | 34.818 | 82.244 |
| file.flush | 24456 | 1411.117 | 1440.335 |
| directory.sync | 2175 | 41.857 | 36.290 |
| backup.file_copy | 19425 | 1121.932 | 1165.510 |
| restore.file_copy | 3843 | 227.909 | 247.301 |
| harness.validate | 806 | 1.572 | 7.747 |
| harness.reopen | 3081 | 13.148 | 33.308 |
| harness.search | 3081 | 4.552 | 10.190 |
| harness.reader_close | 3081 | 0.100 | 0.328 |
| mmap.open | 114881 | 3.935 | 13.868 |
| mmap.close | 114881 | 1.574 | 7.271 |
| input.drain | 114881 | 0.074 | 0.098 |
| directory.delete | 295 | 1.218 | 15.465 |
| harness.cleanup | 100 | 1.209 | 15.437 |

Loose/durable baseline: 34,791 tracked outputs; 19,425 backup copies carrying 3,313,068 bytes; 3,843 restore copies carrying 634,633 bytes. Compound mode: 5,517 backup copies carrying 3,569,024 bytes and 1,191 restore copies carrying 683,475 bytes. Compound mode transferred slightly more bytes while substantially reducing file fan-out. Baseline final tracked output lengths total about 9.76 MB; small variation is timestamp metadata. These lengths are not cumulative bytes written or physical disk throughput. Instant file.created/file.written activity durations are counter-emission bookkeeping. Copy bytes are actual transferred bytes, including rejected copies. All directory sync requests reported Succeeded.

## Conclusion

The healthy state-machine workload is dominated by durability requests, especially per-file durable stream flushes within backup and restore. Compound mode reduced median elapsed time by about 62% on Linux and 65% on Windows while reducing copy and flush counts. Source non-durable mode alone reduced the median by about 7% on Linux and 6% on Windows, leaving durable backup/restore fan-out intact. The combined control reduced the median further, but disabling source durability is a diagnostic control and is not a production recommendation. No production optimisation or durability-contract change was implemented.

Mapping open/close, reopening, input drain, reader close and cleanup were small relative to sync/flush requests. All 3000 temporary trees across the thirty reports were removed. No retry, deferred-delete, queued-finaliser-delete, input finaliser release or finaliser mapping-close event was observed. This supports a flush-dominated conclusion for this healthy model; it does not reproduce or clear historic CI lifetime failures.

The measured source mistakenly labelled delete-shared read opens as file.delete. That raw category mixes read-open and actual deletion and must not be reported as a deletion count or pure deletion time. It bounds the instrumented file-deletion contribution from above: below 0.91% of baseline elapsed time on Windows and 0.22% on Linux, below 1.67% across all cells. Directory deletion and cleanup were independently timed. The close-out correction names read opens file.read_open, retains file.delete for deletion, and adds regression coverage. It changes an activity name, not I/O, sharing, retry or lifetime policy. The full matrices belong to the measured fingerprint above; the corrected source has separate provenance and focused validation.

[Issue 86](https://github.com/jordansrowles/LeanCorpus/issues/86#issuecomment-5602038587) distinguishes a source-proven failed-IndexWriter-constructor write.lock leak from an inconclusive .seg publication access failure without a proven internal handle owner. This healthy performance model does not exercise the failed constructor or qualify the complete hosted stress profile. There is no demonstrated delayed-close/retry evidence here to transfer as a new lifetime diagnosis. No issue or discussion was changed.

The [spike discussion](https://github.com/jordansrowles/LeanCorpus/discussions/95) provides consistent mechanism context for flush cost and physical fan-out, not replacement measurements or evidence of machine-reset or physical-power-loss guarantees. Any future batching/staging proposal must first prove publication order, durable backup/restore semantics and crash consistency; no such change is proposed from these results.

## Environment and limits

Linux: Debian 13, .NET 10.0.3, four processors, Intel Xeon E3-1220 V2 with schedutil, rotational TOSHIBA DT01ABA1 root disk, ext4 /dev/sda2. Windows: disposable Server 2025 Core build 26100, .NET 10.0.11, two vCPUs, 8 GiB RAM, NTFS C drive, qcow2/IDE on the same host disk. Defender was observed disabled; no settings were changed. Runtime patch, CPU and storage-stack differences prevent pure operating-system attribution. The virtual IDE disk does not qualify release disk throughput.

Default and alternate paths were on the same volume on each platform. Configuration order was fixed rather than randomised; the baseline Windows range was wide. The path results do not establish a causal placement benefit or independent-volume effect. Both-framework correctness gates are separate from this net10.0 performance matrix. Linux AOT is separate from Windows AOT, which was not qualified.
