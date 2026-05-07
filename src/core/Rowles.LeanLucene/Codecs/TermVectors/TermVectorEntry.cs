namespace Rowles.LeanLucene.Codecs.TermVectors;

/// <summary>A single term vector entry: term text, frequency in the document, and positions.</summary>
/// <param name="Term">The term text.</param>
/// <param name="Freq">The number of times the term appears in the document.</param>
/// <param name="Positions">The token positions at which the term appears.</param>
public readonly record struct TermVectorEntry(string Term, int Freq, int[] Positions);
