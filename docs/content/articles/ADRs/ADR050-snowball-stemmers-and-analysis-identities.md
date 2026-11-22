---
adr: ADR050
title: "Snowball stemmers and analysis identities"
date: 2026-11-20
status: Accepted
version-added: v4.0.0
last-updated: 2026-11-22
summary: "The nine Snowball-language stemmers are generated from pinned official Snowball 3.2.0 sources and must match the reference exactly. Each default language pipeline has an identity that changes exactly when its output can change, and the factory can replace or remove a language's stemmer."
areas: [analysis, stemmers, devops]
relates-to: [ADR021]
---

# ADR050: Snowball stemmers and analysis identities

- **Date:** 2026-11-20
- **Status:** Accepted

## Context

Issue #122 reported that German `Tomaten` and `Tomate` did not conflate. The English, French,
German, Spanish, Italian, Portuguese, Dutch, Russian and Arabic stemmers were hand-written
suffix tables loosely modelled on Snowball. Against Snowball's published word lists they
differed by between 2,251 (English) and 21,120 (Dutch) words, and the German pre-processing
wrote past an input-sized buffer for words with several `ß`. Nothing told users which behaviour
an index had been built with, so a stemmer change could silently break retrieval for existing
fields.

Issue #123 asked for a way to keep a language's tokeniser and stop words while changing or
disabling its stemmer.

Snowball is BSD-licensed. Its French, Italian and Arabic word lists are CC BY-SA or GPL, and
LeanCorpus does not commit copyleft data.

## Decision

1. **Official algorithms, generated.** The nine stemmers run code that `./devops snowball generate`
   derives from the Snowball 3.2.0 compiler's C# output, pinned in the test fixtures with its
   `.sbl` source. The transform rewrites a fixed set of constructs onto a `char[]` runtime
   (`SnowballRuntime`), so the span path does not allocate. It fails on anything it does not
   recognise. Generated files are committed, never hand-edited, and checked in CI by
   `./devops snowball verify`.

   `generate` and `verify` check each pinned file against its own SHA-256. They do not re-run the
   Snowball compiler, so they do not prove that the pinned C# was compiled from the pinned `.sbl`.
   That link was checked when the fixtures were pinned, by compiling each `.sbl` with the compiler
   at the pinned commit and comparing the output byte for byte. It must be checked again whenever
   the fixtures are updated. Conformance does not depend on it, because production output is
   tested against the official word lists and the unchanged reference runtime.
2. **Conformance is exact.** Production output must equal the pinned reference on every word of
   the official lists. Vendored BSD lists are committed. French and Italian are downloaded at a
   pinned revision and SHA-256 by `./devops snowball fetch-data`, and their tests fail rather
   than skip without them. Arabic uses a LeanCorpus-written word table whose expected stems come
   from Snowball's C `stemwords`. A differential fuzz compares production with the unchanged
   reference runtime.
3. **Public shape is kept.** The built-in classes keep their names, `SnowballStemmer` base class
   and sealing, and are `IShareableSpanStemmer`, using one runtime per thread. `SnowballStemmer`
   stays as a base for custom suffix-table stemmers. `StemmedAnalyser` moves to Porter2, and
   `PorterStemmer`, `PorterStemmerFilter` and `StemmerAnalyser.Porter()` keep the original Porter
   algorithm.
4. **Analysis identities.** `AnalyserFactory.GetDefaultAnalysisId(code)` returns
   `rowles-analysis/<code>/<version>` for the `Create(code)` pipeline. There are two version lines:
   - **Snowball line** (en, fr, de, es, it, pt, nl, ru, ar): the Snowball version at which the
     generated code last changed (`3.2.0`). A LeanCorpus-only output change on the same Snowball
     version adds `+rN`.
   - **Rowles line** (sk, zh, ja, ko): a LeanCorpus `0.x.y` version. Releases before 4.0
     reported no identities, and their output matches no 4.0 identity, because 4.0 also keeps
     position gaps where stop words are removed and changes lower-casing and Unicode handling.
     The Rowles line numbers the earlier output eras traced from the source history, so the
     4.0 versions follow them: `zh`, `ja` and `ko` were `0.1.0` (1.1.1 to 1.4.2), `0.1.1` (2.0.0
     to 2.1.1), `0.1.2` (2.2.0) and `0.2.0` (2.3.0 to 3.1.1), and are `0.3.0` in 4.0; `sk` was
     `0.1.0` (2.2.0 to 3.1.1) and is `0.2.0` in 4.0, which also carries the Slovak stemmer rework.
     The Snowball line has no identity before 4.0.

   `analysis-versions.json` records each language's version, generated-code hash and a
   fingerprint of the default pipeline's output over a fixed corpus. The generated
   `AnalysisIdentities` table is compiled from it. The code hash covers the generated algorithm
   and the shared runtime (`SnowballRuntime.cs`, `SnowballPort.cs`). The fixed corpus is a pinned
   fixture whose hash is recorded in the manifest, so editing it is an explicit identity event.
   `generate --code-only` writes changed algorithms without touching identities, so the identity
   tests can fingerprint the new code before the decision is recorded. `generate` refuses a code or fingerprint
   change without an identity change, unless the change is passed explicitly with
   `--equivalent <code> --reason`, `--rebaseline <code>` or `--bump <code>=minor|patch`.
   Identities are opaque and compared for equality. ADR051 records them in each commit and
   enforces them when an index is written.
5. **Factory override.** `AnalyserFactory.Create(code, ISpanStemmer?)` builds the language's
   tokeniser and stop words with the given stemmer, or none. Ownership follows
   `LanguageAnalyser`: a stemmer must be shareable or thread-local, or analysis throws. A custom
   pipeline has no built-in identity.

## Rationale

Generating from pinned compiler output makes "matches Snowball" checkable rather than aspirational, and
makes an upgrade a regeneration plus a reviewed diff. A fixed set of rewrites with fail-closed
checks keeps the generated code reviewable. Naming identities after the Snowball version tells
users which algorithm they run. The Rowles line is versioned separately because those languages
are not Snowball. Tying the identity to a recorded fingerprint, rather than to judgement, means
no output change ships under an old identity.

## Consequences

- Fields analysed by an earlier release with the nine Snowball languages, or with
  `StemmedAnalyser`, must be reindexed. The German stop words also gained `ans`, `aufs`, `ins`
  and `vom`.
- Rowles.Text and Rowles.LeanCorpus both compile the analysis source (ADR021), so both packages
  change together and both ship `THIRD-PARTY-NOTICES.md` with the Snowball licence.
- CI downloads the French and Italian lists, and the Text suite fails without network access to
  `raw.githubusercontent.com` unless they are already present.
- Upgrading Snowball means updating the pinned fixtures, running `generate`, and reviewing the
  identity changes it reports.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-20 | Accepted | Original decision accepted. | `#122`, `#123` |
| 2026-11-21 | Clarified | Stated the limits of fixture provenance checks. | `#131` |
| 2026-11-21 | Amended | Corrected the pre-4.0 output claim; identities are recorded and enforced per index (ADR051). | `#131` |
| 2026-11-22 | Amended | Rowles-line versions follow the pre-4.0 output eras; the code hash covers the runtime; the fingerprint corpus is pinned; added `--code-only`; `sk` is `0.2.0`. | `#131` |
