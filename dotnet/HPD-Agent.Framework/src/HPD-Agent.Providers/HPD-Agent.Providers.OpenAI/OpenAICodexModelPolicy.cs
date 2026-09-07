using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>Account-discovered request constraints for one exact Codex model.</summary>
/// <param name="ModelId">The model whose catalog entry supplied these constraints.</param>
/// <param name="SupportedReasoningEfforts">Raw catalog levels, including unknown levels that are not selectable.</param>
/// <param name="DefaultReasoningEffort">The advertised default; omission on a request still uses the server default.</param>
public sealed record OpenAICodexModelPolicy(
    string ModelId,
    IReadOnlyList<string> SupportedReasoningEfforts,
    string? DefaultReasoningEffort = null)
{
    /// <summary>Validates an explicit request against implemented and account-discovered levels.</summary>
    /// <remarks>Off is explicitly normalized to Low before validation. Unknown levels are never coerced.</remarks>
    internal static void Validate(string? model, ChatOptions? options, OpenAICodexModelPolicy? policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (policy is not null && !string.Equals(policy.ModelId, model, StringComparison.Ordinal))
            throw new InvalidOperationException("The Codex model policy belongs to a different model.");
        if (options?.Reasoning?.Effort is not { } effort)
            return;
        var level = effort == Microsoft.Extensions.AI.ReasoningEffort.Low ? "low"
            : effort == Microsoft.Extensions.AI.ReasoningEffort.Medium ? "medium"
            : effort == Microsoft.Extensions.AI.ReasoningEffort.High ? "high"
            : effort == Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh ? "xhigh"
            : throw new NotSupportedException($"Codex reasoning effort '{effort}' is not implemented.");
        if (policy is not null && !policy.SupportedReasoningEfforts.Contains(level, StringComparer.Ordinal))
            throw new NotSupportedException($"Codex model '{model}' does not support reasoning effort '{level}'.");
    }
}
