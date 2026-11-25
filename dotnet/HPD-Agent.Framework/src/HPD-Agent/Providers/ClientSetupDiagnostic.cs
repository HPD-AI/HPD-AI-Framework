namespace HPD.Agent.Providers;

/// <summary>A safe, serializable description of a client setup failure.</summary>
public sealed record ClientSetupDiagnostic
{
    /// <summary>Increments when the diagnostic contract changes incompatibly.</summary>
    public string SchemaVersion { get; init; } = "1.0";
    /// <summary>Stable diagnostic code.</summary>
    public required string Code { get; init; }
    /// <summary>Safe description of the failure.</summary>
    public required string Message { get; init; }
    /// <summary>The required client family, when known.</summary>
    public ProviderClientFamily? Family { get; init; }
    /// <summary>The selected provider, when known.</summary>
    public string? ProviderKey { get; init; }
    /// <summary>The selected backend, when known.</summary>
    public string? BackendKey { get; init; }
    /// <summary>The selected model, when known and safe to expose.</summary>
    public string? ModelName { get; init; }
    /// <summary>Configuration path associated with the failure.</summary>
    public string? Path { get; init; }
    /// <summary>Safe corrective guidance, when available.</summary>
    public string? Guidance { get; init; }
    /// <summary>The expected provider payload type, when applicable.</summary>
    public string? ExpectedType { get; init; }
    /// <summary>The incompatible supplied payload type, when safe to disclose.</summary>
    public string? ActualType { get; init; }
    /// <summary>Individual provider validation issues.</summary>
    public IReadOnlyList<ProviderValidationIssue> Issues { get; init; } = [];
}

/// <summary>Exposes the safe setup diagnostic carried by a failure.</summary>
public interface IClientSetupFailure
{
    /// <summary>Gets the safe structured diagnostic.</summary>
    ClientSetupDiagnostic Diagnostic { get; }
}

/// <summary>Reports a failure to resolve the client required for an input.</summary>
public sealed class ClientSetupException : AgentRunConfigurationException, IClientSetupFailure
{
    /// <summary>Creates a client setup exception.</summary>
    public ClientSetupException(ClientSetupDiagnostic diagnostic, Exception? innerException = null)
        : base(diagnostic.Code, diagnostic.Path ?? string.Empty, diagnostic.Message,
            diagnostic.ProviderKey, innerException: innerException) => Diagnostic = diagnostic;

    /// <inheritdoc />
    public ClientSetupDiagnostic Diagnostic { get; }

    internal static ClientSetupException FromValidation(
        ProviderValidationResult validation, EffectiveProviderClientConfig effective)
    {
        var issues = validation.Errors.ToArray();
        return new ClientSetupException(new ClientSetupDiagnostic
        {
            Code = issues.Length == 1 ? issues[0].Code : "provider.configuration_invalid",
            Message = string.Join("; ", issues.Select(static issue => issue.Message)),
            Family = effective.Family,
            ProviderKey = effective.Provider.Backend.ProviderKey,
            BackendKey = effective.Provider.Backend.BackendKey,
            ModelName = effective.ModelName,
            Path = issues.Length == 1 ? issues[0].Path : null,
            Guidance = issues.Length == 1
                ? issues[0].Guidance ?? GuidanceFor(issues[0].Code)
                : null,
            Issues = issues
        });
    }

    internal static ClientSetupException ForConfiguration(
        EffectiveProviderClientConfig effective,
        string code,
        string path,
        string message,
        string? guidance = null) => new(new ClientSetupDiagnostic
    {
        Code = code,
        Message = message,
        Family = effective.Family,
        ProviderKey = effective.Provider.Backend.ProviderKey,
        BackendKey = effective.Provider.Backend.BackendKey,
        ModelName = effective.ModelName,
        Path = path,
        Guidance = guidance
    });

    private static string? GuidanceFor(string code) => code switch
    {
        "provider.model_required" =>
            "Select a model in the agent, provider profile, or run configuration.",
        "provider.model_path_required" =>
            "Configure a valid local model path in the agent or run configuration.",
        "provider.family_unsupported" =>
            "Select a provider that supports the required client family.",
        _ => null
    };
}
