using System.Text.Json.Serialization;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Specifies the durability a caller requires before a write is acknowledged.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RequestedWriteDurability>))]
public enum RequestedWriteDurability
{
    /// <summary>Accept the write in the local writer without requiring a commit.</summary>
    Memory,
    /// <summary>Require a local durable commit.</summary>
    LocalFsync,
    /// <summary>Require a cluster quorum. Unsupported by Community Server.</summary>
    Quorum,
    /// <summary>Require replica acknowledgement. Unsupported by Community Server.</summary>
    Replicated
}
