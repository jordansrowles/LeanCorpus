---
adr: ADR-S005
title: "AOT and framework compatibility boundary"
date: 2026-08-07
last-updated: 2026-10-17
status: Accepted
version-added: vNext
summary: "Keeps the public contracts Native AOT-ready while isolating framework-specific compatibility work."
areas: [server, aot, compatibility, framework]
relates-to: [ADR041]
---

# ADR-S005: AOT and framework compatibility boundary

## Context

The server needs Native AOT support, but ASP.NET Core, gRPC, Studio and optional clustering infrastructure have different trimming and runtime constraints.

## Decision

The abstractions package targets `net10.0` and `net11.0`, uses only the BCL, and supplies `System.Text.Json` source-generated metadata. It does not reference ASP.NET Core, gRPC, Blazor, DotNext or private infrastructure.

The affected Community Server and Orchard packages and their test hosts target both frameworks. The .NET 11 SDK and preview C# compiler remain build prerequisites; the framework matrix describes supported application runtimes. This extension was authorised on 17 October 2026 as part of physical-index lifecycle qualification under [ADR041](../ADR041-physical-index-runtime-lifecycle.md).

Framework compatibility is verified by the owning host or private module before it is claimed. The abstraction boundary itself is the shared AOT gate, not proof that every future host is AOT-compatible.

## Consequences

Public contracts remain safe to consume from different hosts. Each host retains responsibility for its own publish, trimming and smoke-test evidence on each framework. Managed tests on both frameworks do not establish Native AOT support for an unqualified host.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-08-07 | Accepted | Original decision accepted. | `fb02e9398` |
| 2026-10-17 | Amended | Added the supported .NET 10 and .NET 11 framework matrix and separated managed-test evidence from Native AOT qualification. | `0b84efc64` |
