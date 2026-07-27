# Hunspell

LeanCorpus can use Hunspell `.aff` and `.dic` data for dictionary-driven stemming. It supports common short-form flags and simple prefix and suffix rules.

## Load a dictionary

```csharp
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

var dictionary = HunspellDictionary.FromFile(
    "en_GB.aff",
    "en_GB.dic");

var analyser = StemmerAnalyser.Hunspell(dictionary);
```

Parsed dictionaries are cached by content and generation limit, so reuse the returned immutable dictionary rather than reparsing it per document.

Streams are also supported:

```csharp
var dictionary = HunspellDictionary.FromStream(affixStream, dictionaryStream);
```

The streams are read to completion and left open.

## Add the filter directly

```csharp
var filter = new HunspellStemFilter(
    dictionary,
    injectAlternates: true);
```

With `injectAlternates: false`, matching surface forms are replaced by stems. With alternates enabled, the pipeline can retain alternatives for broader recall. Confirm the resulting positions before using the same field for phrase queries.

## Dictionary scope

Hunspell files vary in their use of flags, compound rules, conversions, and language-specific directives. LeanCorpus intentionally implements a lightweight subset, not every extension supported by the reference Hunspell implementation.

Test the actual dictionaries and inflections used by your application. Unsupported directives should not be assumed to have equivalent behaviour.

`maxGeneratedFormsPerEntry` defaults to `4,096` and protects loading from explosive affix combinations:

```csharp
var dictionary = HunspellDictionary.FromFile(
    "language.aff",
    "language.dic",
    maxGeneratedFormsPerEntry: 1_024);
```

Lowering the limit bounds startup work but can omit generated forms.

## Index and query consistency

Use the same stemming behaviour for indexing and user queries. Changing a dictionary or its generation limit changes produced terms, so existing indexed content must be reindexed if uniform matching is required.

See [Stemmers](04-stemmers.md) and [Analysis overview](index.md).
