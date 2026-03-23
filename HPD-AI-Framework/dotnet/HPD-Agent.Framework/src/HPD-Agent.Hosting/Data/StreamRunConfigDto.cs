namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Serializable agent run configuration for streaming requests.
/// Maps to AgentRunConfig but only includes properties that can be sent over the wire.
/// </summary>
/// <param name="Chat">Chat-specific options (temperature, maxTokens)</param>
/// <param name="ProviderKey">Provider identifier (e.g., "anthropic", "openai")</param>
/// <param name="ModelId">Model identifier (e.g., "claude-sonnet-4-5-20250929")</param>
/// <param name="AdditionalSystemInstructions">Extra system instructions for this run</param>
/// <param name="ContextOverrides">Context key-value overrides</param>
/// <param name="PermissionOverrides">Permission overrides</param>
/// <param name="CoalesceDeltas">Whether to coalesce text deltas</param>
/// <param name="SkipTools">Whether to skip tool execution</param>
/// <param name="RunTimeout">Optional timeout for this run (ISO 8601 duration)</param>
public record StreamRunConfigDto(
    ChatRunConfigDto? Chat,
    string? ProviderKey,
    string? ModelId,
    string? AdditionalSystemInstructions,
    Dictionary<string, object>? ContextOverrides,
    Dictionary<string, bool>? PermissionOverrides,
    bool? CoalesceDeltas,
    bool? SkipTools,
    string? RunTimeout);
