namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Identifies an extension port required by an endpoint.</summary>
public enum InterceptionPort
{
    /// <summary>Operation routing.</summary>
    Routing,
    /// <summary>Caller authorisation.</summary>
    Authorisation,
    /// <summary>Feature entitlement.</summary>
    Entitlement,
    /// <summary>Write acknowledgement.</summary>
    WriteAcknowledgement,
    /// <summary>Index lifecycle.</summary>
    Lifecycle,
    /// <summary>Audit publication.</summary>
    Audit,
    /// <summary>Read consistency.</summary>
    Consistency,
    /// <summary>Inspection bounds.</summary>
    Inspection
}
