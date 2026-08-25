# Rowles.Text

[![NuGet](https://img.shields.io/nuget/v/Rowles.Text?label=Rowles.Text)](https://www.nuget.org/packages/Rowles.Text/)
![Native AOT](https://img.shields.io/badge/Native%20AOT-compatible-8A2BE2)

Rowles.Text is the standalone text-analysis package from LeanCorpus. Use it when an application needs tokenisers, filters, stemmers or analysers without indexing and search.

> [!IMPORTANT]
> The package is named `Rowles.Text`, but its public namespaces remain under `Rowles.LeanCorpus.Analysis`. This keeps analysis code source-compatible when the same implementation is compiled into LeanCorpus.

## Tokenise text in five minutes

Install the package:

```bash
dotnet add package Rowles.Text
```

Create an analyser and receive its tokens:

```csharp
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

var analyser = new StandardAnalyser();
analyser.Analyse(
    "LeanCorpus makes local search practical.",
    new ConsoleTokenSink());

file sealed class ConsoleTokenSink : ISpanTokenSink
{
    public void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null)
    {
        Console.WriteLine($"{startOffset}-{endOffset}: {text}");
    }
}
```

The sink receives each analysed token together with its source offsets and position increment.

A runnable version lives in [Rowles.Text.Example.Tokenise](../../examples/Rowles.Text.Example.Tokenise/).

## Choose an analysis path

| I need to... | Start with |
| --- | --- |
| Split ordinary prose into searchable terms | `StandardAnalyser` |
| Control tokenisation and filtering separately | A tokeniser followed by token filters |
| Remove common words | A stop-word filter or analyser configuration |
| Stem words for a language | The matching language analyser or stemmer |
| Segment Chinese, Japanese or Thai text | The relevant language tokeniser and lexicon |
| Preserve phrases, synonyms or graphs | Token positions, position lengths and graph-aware filters |

The [analysis documentation](../../../docs/analysis/index.md) describes the available components and language support.

## Use a lexicon-backed component

Some language components need data from the repository's `lexicons` directory or a custom path:

```csharp
var thai = ThaiTokeniser.FromFile("lexicons/thai-dict.txt");
var analyser = new IcuAnalyser(thaiTokeniser: thai);
```

See the [lexicons guide](../../../lexicons/README.md) before deploying Japanese, Chinese or Thai analysis.

> [!WARNING]
> Correct token text is only part of the contract. Filters must also preserve valid offsets, position increments, position lengths and token graphs.

## Rowles.Text or LeanCorpus?

Reference `Rowles.Text` when you only need analysis. Reference `LeanCorpus` when you need indexing or search; LeanCorpus already compiles the same analysis source directly.

Do not reference both packages merely to obtain the same analysers. If another dependency requires both, keep usage behind one public namespace boundary.

## Performance and AOT

The pipeline favours streaming and span-based APIs to avoid per-token allocation. Rowles.Text is Native AOT compatible and avoids reflection-based component discovery or runtime code generation.

Performance-sensitive changes should be checked with the registered analysis benchmark suites:

```bash
./devops benchmark -List
./devops benchmark -Suite analysis-filters -Strat fast
```

## Next steps

- Run the [tokenisation example](../../examples/Rowles.Text.Example.Tokenise/).
- Browse the [analysis guides](../../../docs/analysis/index.md).
- Learn how lexicon files are used in the [lexicons README](../../../lexicons/README.md).
- Contribute an analyser, filter, stemmer or tokeniser using [CONTRIBUTING.md](CONTRIBUTING.md).
