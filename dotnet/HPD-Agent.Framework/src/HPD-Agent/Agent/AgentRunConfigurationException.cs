namespace HPD.Agent;

/// <summary>Reports a deterministic run-configuration validation failure.</summary>
public sealed class AgentRunConfigurationException : InvalidOperationException
{
    /// <summary>Initializes the exception with structured mismatch information.</summary>
    public AgentRunConfigurationException(
        string code,
        string path,
        string message,
        string? providerKey = null,
        Type? expectedType = null,
        Type? actualType = null) : base(message)
    {
        Code = code;
        Path = path;
        ProviderKey = providerKey;
        ExpectedType = expectedType;
        ActualType = actualType;
    }

    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; }

    /// <summary>Gets the exact public configuration path.</summary>
    public string Path { get; }

    /// <summary>Gets the canonical selected provider when known.</summary>
    public string? ProviderKey { get; }

    /// <summary>Gets the expected generated contract type when applicable.</summary>
    public Type? ExpectedType { get; }

    /// <summary>Gets the contradictory runtime type when applicable.</summary>
    public Type? ActualType { get; }
}
