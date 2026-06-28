using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Anthropic;

internal sealed class AnthropicConfiguredChatClient(
    IChatClient innerClient,
    string defaultModelId,
    int defaultMaxTokens)
    : IChatClient
{
    public void Dispose() => innerClient.Dispose();

    public object? GetService(System.Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
            return null;

        if (serviceType.IsInstanceOfType(this))
            return this;

        return innerClient.GetService(serviceType, serviceKey);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var appliedOptions = ApplyOptions(options);
        return innerClient.GetResponseAsync(
            ApplyCachePolicy(messages, appliedOptions),
            appliedOptions,
            cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var appliedOptions = ApplyOptions(options);
        return innerClient.GetStreamingResponseAsync(
            ApplyCachePolicy(messages, appliedOptions),
            appliedOptions,
            cancellationToken);
    }

    private ChatOptions ApplyOptions(ChatOptions? options)
    {
        var merged = options?.Clone() ?? new ChatOptions();
        AnthropicChatRequestOptionKeys.ApplyRawRequestOptions(merged, defaultModelId, defaultMaxTokens);
        return merged;
    }

    private IReadOnlyList<ChatMessage> ApplyCachePolicy(IEnumerable<ChatMessage> messages, ChatOptions options)
    {
        var cacheControl = AnthropicChatRequestOptionKeys.GetCacheControl(options);
        if (cacheControl is null)
            return messages as IReadOnlyList<ChatMessage> ?? messages.ToList();

        var materialized = messages.ToList();
        if (materialized.Count == 0)
            return materialized;

        var result = materialized.Select(CloneMessage).ToList();

        if (cacheControl.SystemMessages is { } systemTtl)
        {
            foreach (var message in result.Where(static m => m.Role == ChatRole.System))
            {
                ApplyCacheControlToTextContents(message, systemTtl);
            }
        }

        if (cacheControl.LastUserMessage is { } userTtl)
        {
            var lastUserMessage = result.LastOrDefault(static m => m.Role == ChatRole.User);
            if (lastUserMessage is not null)
            {
                ApplyCacheControlToTextContents(lastUserMessage, userTtl);
            }
        }

        return result;
    }

    private static ChatMessage CloneMessage(ChatMessage message)
    {
        var clone = message.Clone();
        clone.Contents = message.Contents.Select(CloneContent).ToList();
        return clone;
    }

    private static AIContent CloneContent(AIContent content)
        => content switch
        {
            TextContent text => new TextContent(text.Text)
            {
                AdditionalProperties = CloneAdditionalProperties(text.AdditionalProperties),
                Annotations = text.Annotations,
                RawRepresentation = text.RawRepresentation
            },
            _ => content
        };

    private static AdditionalPropertiesDictionary? CloneAdditionalProperties(
        AdditionalPropertiesDictionary? properties)
    {
        if (properties is null)
            return null;

        var clone = new AdditionalPropertiesDictionary();
        foreach (var property in properties)
        {
            clone[property.Key] = property.Value;
        }

        return clone;
    }

    private static void ApplyCacheControlToTextContents(
        ChatMessage message,
        AnthropicCacheTtl ttl)
    {
        foreach (var content in message.Contents.OfType<TextContent>())
        {
            content.WithCacheControl(ToAnthropicTtl(ttl));
        }
    }

    private static Ttl ToAnthropicTtl(AnthropicCacheTtl ttl)
        => ttl switch
        {
            AnthropicCacheTtl.FiveMinutes => Ttl.Ttl5m,
            AnthropicCacheTtl.OneHour => Ttl.Ttl1h,
            _ => throw new ArgumentOutOfRangeException(nameof(ttl), ttl, null)
        };
}
