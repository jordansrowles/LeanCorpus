using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial;

namespace Rowles.LeanCorpus.Search.Aggregations;


internal static class SpatialAggregationRequestValidation
{
    internal static string ValidateName(string name, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(name, parameterName);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Aggregation names must not be empty or whitespace.", parameterName);
        return name;
    }
}
