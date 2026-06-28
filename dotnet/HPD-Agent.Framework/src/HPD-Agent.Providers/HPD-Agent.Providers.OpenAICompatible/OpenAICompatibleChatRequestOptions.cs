using Microsoft.Extensions.AI;
using System;

namespace HPD.Agent.Providers.OpenAICompatible;

public static class OpenAICompatibleChatRequestOptions
{
    public static ChatOptions Apply(
        string defaultModelId,
        ChatOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultModelId);

        var merged = options?.Clone() ?? new ChatOptions();
        if (string.IsNullOrWhiteSpace(merged.ModelId))
            merged.ModelId = defaultModelId;

        return merged;
    }
}
