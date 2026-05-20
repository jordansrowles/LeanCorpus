using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.TermDictionary.Legacy;
using Rowles.LeanCorpus.Store;
namespace Rowles.LeanCorpus.Codecs.TermDictionary;

/// <summary>
/// Reads a .dic file produced by <see cref="TermDictionaryWriter"/>.
/// Detects format version automatically and dispatches to the matching legacy reader.
/// v2 (current live format): delegates to <see cref="TermDictionaryV2Reader"/> for byte-keyed O(log N) lookups.
/// v1 (legacy): delegates to <see cref="TermDictionaryV1Reader"/> for materialised string lookups.
/// </summary>
internal sealed class TermDictionaryReader : IDisposable
{
    private readonly TermDictionaryV2Reader? _v2;
    private readonly TermDictionaryV1Reader? _v1;
    private bool _disposed;

    private TermDictionaryReader(TermDictionaryV2Reader v2) { _v2 = v2; }
    private TermDictionaryReader(TermDictionaryV1Reader v1) { _v1 = v1; }

    public static TermDictionaryReader Open(string filePath)
    {
        using var input = new IndexInput(filePath);

        int magic = input.ReadInt32();
        if (magic != CodecConstants.Magic)
            throw new InvalidDataException(
                $"Invalid term dictionary (.dic) file: expected magic 0x{CodecConstants.Magic:X8}, got 0x{magic:X8}.");
        byte version = input.ReadByte();

        if (version == 2)
            return new TermDictionaryReader(TermDictionaryV2Reader.Open(input));

        if (version == 1)
            return new TermDictionaryReader(TermDictionaryV1Reader.Open(input));

        throw new InvalidDataException(
            $"Unsupported term dictionary format version {version}. This build supports up to version {CodecConstants.TermDictionaryVersion}.");
    }

    public bool TryGetPostingsOffset(string term, out long offset) => TryGetPostingsOffset(term.AsSpan(), out offset);

    public bool TryGetPostingsOffset(ReadOnlySpan<char> term, out long offset)
        => _v2 is not null ? _v2.TryGetPostingsOffset(term, out offset) : _v1!.TryGetPostingsOffset(term, out offset);

    public List<(string Term, long Offset)> GetTermsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
        => _v2 is not null ? _v2.GetTermsWithPrefix(qualifiedPrefix) : _v1!.GetTermsWithPrefix(qualifiedPrefix);

    public List<long> GetTermOffsetsWithPrefix(ReadOnlySpan<char> qualifiedPrefix)
        => _v2 is not null ? _v2.GetTermOffsetsWithPrefix(qualifiedPrefix) : _v1!.GetTermOffsetsWithPrefix(qualifiedPrefix);

    public List<(string Term, long Offset)> GetTermsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
        => _v2 is not null ? _v2.GetTermsMatching(fieldPrefix, pattern) : _v1!.GetTermsMatching(fieldPrefix, pattern);

    internal List<long> GetTermOffsetsMatching(string fieldPrefix, ReadOnlySpan<char> pattern)
        => _v2 is not null ? _v2.GetTermOffsetsMatching(fieldPrefix, pattern) : _v1!.GetTermOffsetsMatching(fieldPrefix, pattern);

    public List<(string Term, long Offset, int Distance)> GetFuzzyMatches(string fieldPrefix, ReadOnlySpan<char> queryTerm, int maxEdits, int maxExpansions = 64)
        => _v2 is not null
            ? _v2.GetFuzzyMatches(fieldPrefix, queryTerm, maxEdits, maxExpansions)
            : _v1!.GetFuzzyMatches(fieldPrefix, queryTerm, maxEdits, maxExpansions);

    public List<(string Term, long Offset)> GetAllTermsForField(string fieldPrefix) => GetTermsWithPrefix(fieldPrefix.AsSpan());

    public List<(string Term, long Offset)> EnumerateAllTerms()
        => _v2 is not null ? _v2.EnumerateAllTerms() : _v1!.EnumerateAllTerms();

    public List<(string Term, long Offset)> GetTermsInRange(string fieldPrefix, string? lower, string? upper, bool includeLower = true, bool includeUpper = true)
        => _v2 is not null
            ? _v2.GetTermsInRange(fieldPrefix, lower, upper, includeLower, includeUpper)
            : _v1!.GetTermsInRange(fieldPrefix, lower, upper, includeLower, includeUpper);

    public List<(string Term, long Offset)> GetTermsMatchingRegex(string fieldPrefix, Regex regex)
        => _v2 is not null ? _v2.GetTermsMatchingRegex(fieldPrefix, regex) : _v1!.GetTermsMatchingRegex(fieldPrefix, regex);

    public List<long> GetTermOffsetsContaining(string fieldPrefix, ReadOnlySpan<char> literal)
        => _v2 is not null ? _v2.GetTermOffsetsContaining(fieldPrefix, literal) : _v1!.GetTermOffsetsContaining(fieldPrefix, literal);

    /// <summary>
    /// Intersects the term dictionary with an automaton, returning matching terms.
    /// </summary>
    public List<(string Term, long Offset)> IntersectAutomaton(string fieldPrefix, IAutomaton automaton)
        => _v2 is not null ? _v2.IntersectAutomaton(fieldPrefix, automaton) : _v1!.IntersectAutomaton(fieldPrefix, automaton);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
