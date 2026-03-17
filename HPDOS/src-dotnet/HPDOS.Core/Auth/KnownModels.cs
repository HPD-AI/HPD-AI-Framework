namespace HPDOS.Core.Auth;

/// <summary>
/// Registry of known popular models for each provider.
/// Used for interactive model selection in /model command.
/// </summary>
public static class KnownModels
{
    /// <summary>
    /// Known models organized by provider key.
    /// </summary>
    public static readonly Dictionary<string, List<ModelInfo>> ByProvider = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new()
        {
            new("gpt-5.2", "GPT-5.2 - Latest flagship model", IsRecommended: true),
            new("gpt-5.2-thinking", "GPT-5.2 Thinking - Extended reasoning"),
            new("gpt-5.2-codex", "GPT-5.2 Codex - Best agentic coding"),
            new("gpt-5.1-codex-max", "GPT-5.1 Codex Max - Long-running tasks"),
            new("gpt-5-codex", "GPT-5 Codex - Agentic coding"),
            new("codex-mini-latest", "Codex Mini - Fast & affordable coding"),
            new("gpt-5", "GPT-5 - Unified intelligence"),
            new("gpt-5-pro", "GPT-5 Pro - Professional tier"),
            new("o1", "o1 - Advanced reasoning"),
            new("o1-mini", "o1 Mini - Fast reasoning"),
            new("gpt-4o", "GPT-4o - Legacy (retiring Feb 2026)"),
        },
        ["anthropic"] = new()
        {
            new("claude-opus-4-5-20251101", "Claude Opus 4.5 - Most intelligent", IsRecommended: true),
            new("claude-sonnet-4-5-20251022", "Claude Sonnet 4.5 - Best coding model"),
            new("claude-haiku-4-5-20251022", "Claude Haiku 4.5 - Fast & efficient"),
            new("claude-opus-4-1-20250801", "Claude Opus 4.1"),
            new("claude-sonnet-4-20250514", "Claude Sonnet 4"),
            new("claude-opus-4-20250514", "Claude Opus 4"),
        },
        ["openrouter"] = new()
        {
            new("anthropic/claude-opus-4.5", "Claude Opus 4.5", IsRecommended: true),
            new("anthropic/claude-sonnet-4.5", "Claude Sonnet 4.5"),
            new("anthropic/claude-haiku-4.5", "Claude Haiku 4.5"),
            new("openai/gpt-5.2", "GPT-5.2"),
            new("openai/gpt-5", "GPT-5"),
            new("google/gemini-3-pro", "Gemini 3 Pro"),
            new("google/gemini-3-flash", "Gemini 3 Flash"),
            new("google/gemini-2.5-pro", "Gemini 2.5 Pro"),
            new("meta-llama/llama-4-maverick", "Llama 4 Maverick"),
            new("meta-llama/llama-4-scout", "Llama 4 Scout"),
            new("mistralai/mistral-large-3", "Mistral Large 3"),
            new("deepseek/deepseek-v3", "DeepSeek V3"),
            new("deepseek/deepseek-r1", "DeepSeek R1"),
        },
        ["googleai"] = new()
        {
            new("gemini-3-pro", "Gemini 3 Pro - Latest reasoning model", IsRecommended: true),
            new("gemini-3-flash", "Gemini 3 Flash - Fast & capable"),
            new("gemini-2.5-pro", "Gemini 2.5 Pro - Complex reasoning"),
            new("gemini-2.5-flash", "Gemini 2.5 Flash - Balanced"),
            new("gemini-2.5-deep-think", "Gemini 2.5 Deep Think - Advanced reasoning"),
        },
        ["mistral"] = new()
        {
            new("mistral-large-3", "Mistral Large 3 - 675B MoE flagship", IsRecommended: true),
            new("mistral-medium-3", "Mistral Medium 3 - Cost-efficient"),
            new("mistral-small-3.1", "Mistral Small 3.1 - Lightweight"),
            new("magistral-medium", "Magistral Medium - Chain-of-thought"),
            new("magistral-small", "Magistral Small - Open-source reasoning"),
            new("devstral-2", "Devstral 2 - Code specialized"),
            new("devstral-small-2", "Devstral Small 2 - Code 24B"),
            new("codestral-latest", "Codestral - Code specialized"),
        },
        ["azureopenai"] = new()
        {
            new("gpt-5.2", "GPT-5.2", IsRecommended: true),
            new("gpt-5", "GPT-5"),
            new("gpt-4o", "GPT-4o"),
            new("o1", "o1 - Reasoning"),
        },
        ["bedrock"] = new()
        {
            new("anthropic.claude-opus-4-5-20251101-v1:0", "Claude Opus 4.5", IsRecommended: true),
            new("anthropic.claude-sonnet-4-5-20251022-v1:0", "Claude Sonnet 4.5"),
            new("anthropic.claude-haiku-4-5-20251022-v1:0", "Claude Haiku 4.5"),
            new("anthropic.claude-opus-4-20250514-v1:0", "Claude Opus 4"),
            new("anthropic.claude-sonnet-4-20250514-v1:0", "Claude Sonnet 4"),
            new("meta.llama4-maverick-v1:0", "Llama 4 Maverick"),
            new("meta.llama4-scout-v1:0", "Llama 4 Scout"),
            new("mistral.mistral-large-3-v1:0", "Mistral Large 3"),
            new("amazon.titan-text-premier-v1:0", "Titan Text Premier"),
        },
        ["huggingface"] = new()
        {
            new("meta-llama/Llama-4-Scout", "Llama 4 Scout", IsRecommended: true),
            new("meta-llama/Llama-4-Maverick", "Llama 4 Maverick"),
            new("mistralai/Mistral-Large-3", "Mistral Large 3"),
            new("mistralai/Ministral-3-14B", "Ministral 3 14B"),
            new("deepseek-ai/DeepSeek-R1", "DeepSeek R1"),
            new("deepseek-ai/DeepSeek-V3", "DeepSeek V3"),
            new("Qwen/Qwen2.5-72B-Instruct", "Qwen 2.5 72B"),
            new("google/gemma-2-27b-it", "Gemma 2 27B"),
        },
        ["github-copilot"] = new()
        {
            new("gpt-5.2", "GPT-5.2", IsRecommended: true),
            new("gpt-5", "GPT-5"),
            new("claude-sonnet-4.5", "Claude Sonnet 4.5"),
            new("claude-opus-4.5", "Claude Opus 4.5"),
        },
    };

    /// <summary>
    /// Gets known models for a provider, or an empty list if none defined.
    /// </summary>
    public static List<ModelInfo> GetModelsForProvider(string providerKey)
    {
        return ByProvider.TryGetValue(providerKey, out var models) ? models : new List<ModelInfo>();
    }

    /// <summary>
    /// Gets all known provider keys.
    /// </summary>
    public static IEnumerable<string> GetKnownProviders() => ByProvider.Keys;
}

/// <summary>
/// Represents a known model for a provider.
/// </summary>
public record ModelInfo(
    string Id,
    string Description,
    bool IsRecommended = false,
    bool IsFree = false,        // populated by OpenRouter live fetch only
    bool SupportsTools = true   // populated by OpenRouter live fetch only; true by default for static lists
)
{
    public override string ToString()
    {
        var rec = IsRecommended ? " (Recommended)" : "";
        return $"{Id} - {Description}{rec}";
    }
}
