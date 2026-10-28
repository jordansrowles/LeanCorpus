using System.Net;
using System.Text;

namespace Rowles.DataForge.Workloads;


public enum WikipediaEligibilityRejection
{
    None,
    NonMainNamespace,
    Redirect,
    MissingRevision,
    MissingText,
    RawTooSmall,
    RawTooLarge,
    NormalisationFailed,
    NormalisedTooSmall,
    NormalisedTooLarge,
    TooFewTokens
}
