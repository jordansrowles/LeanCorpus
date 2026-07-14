# Rowles.LeanCorpus

[![Build](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml/badge.svg)](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml) ![AOT Compatible](https://img.shields.io/badge/AOT%20Compatible-8A2BE2) [![Docs](https://img.shields.io/badge/Docs-blue)](https://leancorpus.com) [![Docs](https://img.shields.io/badge/Changelog-blue)](https://github.com/jordansrowles/LeanCorpus/blob/main/CHANGELOG.md)

A .NET-native full-text search engine. Segment-centric indexing, memory-mapped reads, and atomic commit semantics. Targets `net10.0` and `net11.0`. The core library has no external dependencies; stored-field compression uses BCL types only. Optional extension packages add LZ4, Snappy, and Zstandard support.

Inspired by Apache Lucene.

## Projects

All projects target .NET 10, and .NET 11. Versions < 10 are not supported. (`LeanCorpus.SourceGen` is a .NET Standard library, for obvious reasons though).

### Core library
- ![NuGet Version](https://img.shields.io/nuget/v/LeanCorpus?style=flat&label=LeanCorpus&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FLeanLucene%2F)
  - 0 dependencies, AOT compatible, includes LINQ, default compressors (Deflate or Brotli)

### Optional libraries
- ![NuGet Version](https://img.shields.io/nuget/v/LeanCorpus.SourceGen?style=flat&label=LeanCorpus.SourceGen&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FLeanCorpus.SourceGen%2F) Typed mapping source generator
- ![NuGet Version](https://img.shields.io/nuget/v/LeanCorpus.Compression.Zstandard?style=flat&label=LeanCorpus.Compression.Zstandard&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FLeanCorpus.Compression.Zstandard%2F) Zstandard stored field compression
- ![NuGet Version](https://img.shields.io/nuget/v/LeanCorpus.Compression.LZ4?style=flat&label=LeanCorpus.Compression.LZ4&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FLeanCorpus.Compression.LZ4) LZ4 stored field compression
- ![NuGet Version](https://img.shields.io/nuget/v/LeanCorpus.Compression.Snappy?style=flat&label=LeanCorpus.Compression.Snappy&link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FLeanCorpus.Compression.Snappy) Snappy stored field compression

## Native AOT

`Rowles.LeanCorpus` is marked AOT-compatible for `net10.0` and `net11.0`. The core library avoids reflection-based JSON metadata and is validated by a dedicated xUnit smoke executable (which can be ran with `.\scripts\aot-smoke.ps1`) rather than the ASP.NET JSON API example.

This publishes `src\examples\Rowles.LeanCorpus.Example.NativeAot\Rowles.LeanCorpus.Example.NativeAot.csproj` for `win-x64` with `PublishAot=true`, then runs the native executable. The smoke executable several different bits of the library that may have proved some difficulty.

**A note if you're using on of the optional compression libraries, and are using native AOT:**
The core library has no native sidecar dependencies for compression. Optional packages (`Rowles.LeanCorpus.Compression.LZ4`, `Rowles.LeanCorpus.Compression.Snappy`, `Rowles.LeanCorpus.Compression.Zstandard`) may include RID-specific native binaries; AOT consumers using those packages must call their respective `Register()` methods at startup.

> [!IMPORTANT]
> While LeanLucene is AOT capable, it does not support (and does not intend to support) Blazor WASM. LeanLucene (and the other segment-centric engines) require use of the OS's filesystem to achieve its performance. It would be too much work (and a project itself) to support a dual approach with a filesystem, and the browsers limited storage. 
> 
> Blazor Server/Hybrid remains supported (naturally), as long as the indexing happens server-side.


# SonarQube Scan (`main` branch)

[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Lines of Code](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=ncloc)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) 
[![Bugs](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=bugs)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Code Smells](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=code_smells)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Duplicated Lines (%)](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=duplicated_lines_density)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Technical Debt](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=sqale_index)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Vulnerabilities](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=vulnerabilities)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus)
[![Reliability Rating](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=reliability_rating)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) [![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus) 
