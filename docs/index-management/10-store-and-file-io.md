# Store and file I/O

LeanCorpus accesses index files through its Store abstraction. This centralises bounded reads, atomic publication, durability, retries, metrics, and cross-platform file-lifetime behaviour.

## `MMapDirectory`

```csharp
using var directory = new MMapDirectory("/srv/search/index");
using var searcher = new IndexSearcher(directory);
```

Memory mapping lets the operating system page cache serve immutable segment files without copying each file into a managed buffer. Resident memory therefore depends on the active working set and is not the same as managed allocation.

Keep the backing files on a local filesystem with reliable mapping, rename, and durability semantics. Network filesystems require workload-specific validation.

## Inputs and outputs

`IndexInput` provides bounded sequential reads, seeking, and slicing. A slice has its own logical bounds and file lifetime. Compound-file member views additionally share their container mapping because the container owner encloses every member lifetime. Internal decoders can hold one scoped read session across a bounded loop so primitive reads do not repeatedly enter the disposal drain. `IndexOutput` provides controlled writes and position tracking, avoids a second `FileStream` buffer, and accepts an expected size for preallocating large sequential outputs. Compound files use that reservation on POSIX and remain incremental sequential writes on Windows.

Codec code should accept these abstractions instead of opening files directly. Validate lengths before creating slices or allocating buffers.

## Atomic files and durability

`IndexAtomicFileWriter` follows a temporary-write and atomic-publication pattern. `DirectoryFsync` handles directory metadata durability where required. The commit manifest is published only after its referenced files are complete.

`DurableCommits = false` can reduce commit latency, but a reported commit may then be lost after power or kernel failure. It does not relax format validation or atomic visibility requirements.

## File-open retry

`FileOpenRetry` contains the bounded retry policy used for transient filesystem interference, including Windows sharing and antivirus races. It is not an unlimited retry loop and does not turn permanent permissions or missing paths into transient failures.

Avoid adding ad hoc `File.*` calls in indexing, search, codec, or recovery paths. Route operations through Store so platform handling stays consistent.

## Lifetime rules

Immutable segment files can outlive the commit that first published them. Searcher leases, snapshots, and retained commits may still reference them. Deletion is safe only when no live owner can open or use the file.

See [Architecture internals](../contributors/architecture-internals.md), [Snapshots and deletion policies](../concurrency/03-snapshots-and-policies.md), and [Production deployment](../tips/04-production-deployment.md).
