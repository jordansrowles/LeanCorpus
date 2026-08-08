# Rowles.Text

`Rowles.Text` provides LeanCorpus analysis without the search engine. It contains the same analysers, tokenisers, filters and stemmers under the existing `Rowles.LeanCorpus.Analysis` namespaces.

Install only one of `Rowles.Text` or `Rowles.LeanCorpus` in an application. LeanCorpus already compiles the analysis source into its own assembly.

```xml
<PackageReference Include="Rowles.Text" Version="1.0.0" />
```

```csharp
using Rowles.LeanCorpus.Analysis.Analysers;

var analyser = new StandardAnalyser();
```

The package targets .NET 10 and .NET 11 and supports Native AOT.
