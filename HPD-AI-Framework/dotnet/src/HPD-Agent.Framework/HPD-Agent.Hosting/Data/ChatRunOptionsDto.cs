using System.Collections.Generic;

namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Chat-specific run options.
/// </summary>
/// <param name="Temperature">Sampling temperature (0.0-1.0)</param>
/// <param name="MaxOutputTokens">Maximum number of output tokens</param>
/// <param name="TopP">Top-p sampling parameter</param>
/// <param name="FrequencyPenalty">Frequency penalty parameter</param>
/// <param name="PresencePenalty">Presence penalty parameter</param>
/// <param name="AdditionalProperties">Provider-specific additional properties (e.g. thinkingBudgetTokens for Anthropic)</param>
/// <param name="Reasoning">Reasoning effort/output configuration</param>
public record ChatRunConfigDto(
    double? Temperature,
    int? MaxOutputTokens,
    double? TopP,
    double? FrequencyPenalty,
    double? PresencePenalty,
    Dictionary<string, object>? AdditionalProperties = null,
    HPD.Agent.ReasoningOptions? Reasoning = null);
