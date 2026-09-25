using System.Net;
using System.Text;

namespace Rowles.DataForge.Workloads;


public sealed record WikipediaEligibilityResult(WikipediaEligibilityRejection Rejection, string? Text, int RawUtf8Bytes, int TextUtf8Bytes, int TokenCount)
{
    public bool IsEligible => Rejection == WikipediaEligibilityRejection.None;
}

