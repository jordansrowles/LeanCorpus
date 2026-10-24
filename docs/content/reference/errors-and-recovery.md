# Errors and recovery

| Symptom | Likely cause | Action | Index usable? |
|---|---|---|---|
| No hits | Field, analyser or query does not match indexed terms | Check field type and use the same analyser at index and query time | Yes |
| Locked files on Windows | Reader, writer or output remains open | Dispose the owner and retry after lifecycle cleanup | Usually |
| Slow search | Broad query, cold file cache or excessive candidate window | Measure the workload, inspect metrics and reduce work deliberately | Yes |
| Merge pressure | Indexing outpaces background merge capacity | Reduce ingestion pressure or review merge and storage settings | Yes |
| Corrupt index | Missing or invalid files | Run the checker, recover a valid commit or restore a backup | Not until recovered |

See [Validation and recovery](../index-management/03-validation-recovery.md) and [Production deployment](../tips/04-production-deployment.md).
