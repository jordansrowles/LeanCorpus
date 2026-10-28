namespace Rowles.DataForge;


public sealed record DataForgeProfileDescriptor(
    string ProfileId,
    int ProfileVersion,
    string Kind,
    ulong DefaultSeed,
    int DefaultCount,
    string Description);
