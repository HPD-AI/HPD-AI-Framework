using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Ollama;

internal sealed class OllamaConfiguredChatClient(
    IChatClient innerClient,
    string defaultModelId,
    Uri providerUri,
    OllamaProviderConfig? config)
    : IChatClient
{
    private ChatClientMetadata? _metadata;

    public ChatClientMetadata Metadata =>
        _metadata ??= new ChatClientMetadata("ollama", providerUri, defaultModelId);

    public void Dispose() => innerClient.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
            return null;

        if (serviceType == typeof(ChatClientMetadata))
            return Metadata;

        if (serviceType == typeof(OllamaProviderConfig))
            return config;

        if (serviceType.IsInstanceOfType(this))
            return this;

        return innerClient.GetService(serviceType, serviceKey);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => innerClient.GetResponseAsync(
            messages,
            ApplyOptions(options),
            cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => innerClient.GetStreamingResponseAsync(
            messages,
            ApplyOptions(options),
            cancellationToken);

    private static ChatOptions? ApplyOptions(ChatOptions? options)
    {
        if (options?.AdditionalProperties is null)
            return options;

        ChatOptions? clone = null;
        foreach (var property in options.AdditionalProperties)
        {
            if (!OllamaChatRequestOptionKeys.IsKnown(property.Key))
                continue;

            clone ??= options.Clone();
            clone.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            clone.AdditionalProperties[property.Key] = OllamaChatRequestOptionKeys.Normalize(property.Key, property.Value);
        }

        return clone ?? options;
    }
}
