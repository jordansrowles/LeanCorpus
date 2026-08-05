---
adr: ADR022
title: Parent-linked manifests define incremental backup chains
date: 2026-08-05
status: Accepted
version-added: 2.2.0
summary: Reuse unchanged backup files through checksummed parent-linked manifests while keeping restore independent of filesystem links.
areas: [indexing, storage, operability]
---

# ADR022: Parent-linked manifests define incremental backup chains

- **Date:** 2026-08-05
- **Status:** Accepted

## Context

`IndexBackup` copied every file in each backup even when a later commit still
referenced unchanged segment files. Large indexes therefore paid the full copy
cost for every backup. The backup format also needs to remain portable across
filesystems and object-storage transports, where hard links and reflinks are not
reliable primitives.

## Decision

An incremental backup may name a previous backup directory through
`IndexBackupOptions.PreviousBackupDirectoryPath`. Its version 2 manifest records
the parent manifest SHA-256, chain depth, and whether each logical file is
physically present in the current directory. Unchanged files are inherited from
the parent chain and are copied only when first introduced or changed.

Validation and restore accept the ordered full-plus-delta directory list, verify
each parent link and checksum, and resolve every final logical file from the
newest physical copy. A single incremental directory is rejected for restore
because it is not self-contained.

## Rationale

Manifest links keep the format explicit and portable. Comparing length and CRC
avoids re-reading file content for unchanged files while the parent manifest
hash prevents a delta from silently attaching to the wrong base. A full backup
remains the recovery anchor and the chain can be copied between filesystems
without preserving link metadata.

## Consequences

- Incremental backups transfer only changed or new files.
- Operators must retain the full backup and all deltas needed by a restore.
- Restore validates the complete chain before publishing files.
- Existing version 1 full manifests remain readable and are treated as standalone
  full backups.
