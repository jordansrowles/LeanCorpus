namespace Rowles.LeanCorpus.DevOps.Repository;

internal enum CheckoutGitMarkerKind
{
    Directory,
    File
}

internal sealed record CheckoutIdentity(
    string RepositoryRoot,
    string GitMetadataPath,
    CheckoutGitMarkerKind GitMarkerKind);
