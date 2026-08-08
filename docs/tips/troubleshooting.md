# Troubleshooting

Use this page to narrow a symptom before changing configuration.

| Symptom | First check |
|---|---|
| Query returns no documents | Field type, indexed terms and analyser parity |
| Results look stale | Searcher lifetime, refresh and commit behaviour |
| Windows file operation fails | Undisposed readers, writers, snapshots or output streams |
| Memory remains high | Retained searchers, snapshots, vector state or cache limits |
| Search latency spikes | Segment count, query breadth, candidate windows and filesystem cache |
| Index cannot open | Index checker, valid commit recovery and backup |

See [Errors and recovery](../reference/errors-and-recovery.md), [Refresh failures](../concurrency/04-refresh-failures.md) and [Validation and recovery](../index-management/03-validation-recovery.md).
