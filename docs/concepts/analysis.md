# Analysis and Rowles.Text

Use this page when text queries do not match as expected or when you need language-aware tokenisation.

Analysis turns input text into indexed terms. The same analyser must be used at index and query time for terms to line up. A pipeline can apply character filters, tokenisation, token filters and stemming.

`Rowles.Text` packages this pipeline without the index or search engine. Install it for tokenisation, filtering and stemming in applications that do not need LeanCorpus indexing. Do not install it alongside LeanCorpus because LeanCorpus already includes the same analysis types.

See also: [Analysis overview](../analysis/index.md), [Rowles.Text](https://leancorpus.com/analysis/08-rowles-text.html), and <xref:Rowles.LeanCorpus.Analysis.Analysers.IAnalyser>.
