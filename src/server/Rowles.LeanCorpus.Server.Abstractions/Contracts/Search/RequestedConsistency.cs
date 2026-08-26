using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Specifies the caller's read-consistency requirement.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequestedConsistency>))]
public enum RequestedConsistency
{
    /// <summary>Read the local copy.</summary>
    Local,
    /// <summary>Read from the primary local copy.</summary>
    Primary,
    /// <summary>Read from a replica. Unsupported by Community Server.</summary>
    Replica,
    /// <summary>Wait until the supplied local write token is readable.</summary>
    ReadYourWrites
}
