namespace HPD.Agent.Providers.OpenAI;

/// <summary>Defines the model acceptance policy used by the experimental Codex transport.</summary>
public sealed class OpenAICodexModelPolicy
{
    private readonly HashSet<string> _supportedModels;

    /// <summary>Creates a policy that accepts models supplied by the shared OpenAI catalog.</summary>
    /// <param name="version">The reviewed policy revision.</param>
    public OpenAICodexModelPolicy(string version)
        : this(version, []) { }

    /// <summary>Creates a closed Codex model policy.</summary>
    /// <param name="version">The reviewed policy revision.</param>
    /// <param name="supportedModels">The exact supported model identifiers.</param>
    public OpenAICodexModelPolicy(string version, IEnumerable<string> supportedModels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(supportedModels);
        Version = version;
        _supportedModels = supportedModels
            .Select(static model => model.Trim())
            .Where(static model => model.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Gets the observed interoperability policy shipped with this package.</summary>
    /// <remarks>This legacy policy is retained for callers that require a closed, reviewed model set.</remarks>
    public static OpenAICodexModelPolicy ObservedV1 { get; } = new(
        "observed-v1",
        ["gpt-5.4"]);

    /// <summary>Gets the open transport policy used with account-scoped Codex model discovery.</summary>
    public static OpenAICodexModelPolicy AccountDiscoveredV1 { get; } = new(
        "account-discovered-v1");

    /// <summary>Gets the legacy name for the account-discovered transport policy.</summary>
    [Obsolete("Use AccountDiscoveredV1. Codex availability is not the public OpenAI model catalog.")]
    public static OpenAICodexModelPolicy SharedOpenAIModelsV1 => AccountDiscoveredV1;

    /// <summary>Gets the reviewed policy revision.</summary>
    public string Version { get; }

    /// <summary>Gets the exact supported model identifiers.</summary>
    public IReadOnlySet<string> SupportedModels => _supportedModels;

    /// <summary>Returns whether a model identifier is accepted by this policy revision.</summary>
    /// <param name="modelId">The model identifier to test.</param>
    public bool IsSupported(string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId)
        && (_supportedModels.Count == 0 || _supportedModels.Contains(modelId));
}
