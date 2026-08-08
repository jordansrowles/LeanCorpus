---
title: ADR004: JSON API lifecycle extraction boundary
description: Records which JSON API prototype behaviour may be carried into Server Core.
version: vNext
status: Accepted
date: 2026-08-07
---

# ADR004: JSON API lifecycle extraction boundary

## Context

The existing JSON API example demonstrates useful lifecycle and request-shaping behaviour, but its storage and request handling are not a safe server boundary.

## Decision

Carry forward explicit collection management, single-writer ownership, explicit create/delete, structured request identifiers and measured request timing. Reject direct user-controlled file paths, implicit index creation, commit or refresh per request, whole-body `JsonElement` handling, unbounded inspection and persisted schema omissions.

## Consequences

Server Core must adapt the existing engine behind the public service interfaces rather than promoting the example implementation into the public API.
