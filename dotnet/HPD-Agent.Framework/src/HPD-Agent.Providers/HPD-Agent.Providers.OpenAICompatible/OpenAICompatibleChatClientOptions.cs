using System;

namespace HPD.Agent.Providers.OpenAICompatible;

public sealed class OpenAICompatibleChatClientOptions
{
    public required string ProviderKey { get; init; }
    public required string DisplayName { get; init; }
    public required Uri ProviderUri { get; init; }
    public required string DefaultModelId { get; init; }
    public string ChatCompletionsPath { get; init; } = "chat/completions";
    public bool IncludeStreamingUsage { get; init; } = true;
}
