namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Thrown when a document fails <see cref="IndexSchema"/> validation.
/// </summary>
public sealed class SchemaValidationException : InvalidOperationException
{
    /// <summary>
    /// Initialises a new <see cref="SchemaValidationException"/> with the specified message.
    /// </summary>
    /// <param name="message">The message describing the validation failure.</param>
    public SchemaValidationException(string message) : base(message) { }
}
