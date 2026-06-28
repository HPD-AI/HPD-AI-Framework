using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HPD.Agent.Providers.OpenAICompatible;

/// <summary>
/// Applies static provider defaults before delegating to an OpenAI-compatible chat client.
/// </summary>
public sealed class OpenAICompatibleConfiguredChatClient<TConfig> : IChatClient
    where TConfig : OpenAICompatibleProviderConfig
{
    private readonly IChatClient _innerClient;
    private readonly string _providerKey;
    private readonly string _defaultModelId;
    private readonly Uri _providerUri;
    private readonly TConfig? _config;
    private ChatClientMetadata? _metadata;

    public OpenAICompatibleConfiguredChatClient(
        IChatClient innerClient,
        string providerKey,
        string defaultModelId,
        Uri providerUri,
        TConfig? config)
    {
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _providerKey = providerKey ?? throw new ArgumentNullException(nameof(providerKey));
        _defaultModelId = defaultModelId ?? throw new ArgumentNullException(nameof(defaultModelId));
        _providerUri = providerUri ?? throw new ArgumentNullException(nameof(providerUri));
        _config = config;
    }

    public ChatClientMetadata Metadata =>
        _metadata ??= new ChatClientMetadata(_providerKey, _providerUri, _defaultModelId);

    public void Dispose() => _innerClient.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return Metadata;
        }

        if (serviceType == typeof(TConfig))
        {
            return _config;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return _innerClient.GetService(serviceType, serviceKey);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _innerClient.GetResponseAsync(
            messages,
            OpenAICompatibleChatRequestOptions.Apply(_defaultModelId, options),
            cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _innerClient.GetStreamingResponseAsync(
            messages,
            OpenAICompatibleChatRequestOptions.Apply(_defaultModelId, options),
            cancellationToken);
}
