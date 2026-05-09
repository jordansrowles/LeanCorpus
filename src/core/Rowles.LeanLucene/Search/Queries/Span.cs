namespace Rowles.LeanLucene.Search.Queries;

/// <summary>A span: a contiguous range of positions in a document.</summary>
/// <param name="DocId">The identifier of the document containing this span.</param>
/// <param name="Start">The inclusive start position of the span within the document.</param>
/// <param name="End">The exclusive end position of the span within the document.</param>
public readonly record struct Span(int DocId, int Start, int End);
