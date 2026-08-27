namespace HPD.Agent.Providers.OpenAI;

/// <summary>Defines one versioned, closed set of model identifiers accepted by the experimental Codex backend.</summary>
public sealed class OpenAICodexModelPolicy
{
    private readonly HashSet<string> _supportedModels;

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
        if (_supportedModels.Count == 0)
            throw new ArgumentException("At least one reviewed Codex model is required.", nameof(supportedModels));
    }

    /// <summary>Gets the observed interoperability policy shipped with this package.</summary>
    public static OpenAICodexModelPolicy ObservedV1 { get; } = new(
        "observed-v1",
        ["gpt-5.4"]);

    /// <summary>Gets the reviewed policy revision.</summary>
    public string Version { get; }

    /// <summary>Gets the exact supported model identifiers.</summary>
    public IReadOnlySet<string> SupportedModels => _supportedModels;

    /// <summary>Returns whether an exact model identifier is accepted by this policy revision.</summary>
    /// <param name="modelId">The model identifier to test.</param>
    public bool IsSupported(string? modelId) =>
        !string.IsNullOrWhiteSpace(modelId) && _supportedModels.Contains(modelId);
}
