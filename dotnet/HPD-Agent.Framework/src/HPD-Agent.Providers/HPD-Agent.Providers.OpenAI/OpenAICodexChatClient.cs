#pragma warning disable OPENAI001 // Codex uses the experimental Responses event surface.

using System.Runtime.CompilerServices;
using OpenAI.Responses;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Implements both chat operations over the Codex streaming Responses transport.
/// Validates terminal protocol state and lowers provider-neutral request policy.
/// </summary>
/// <remarks>Codex maps an explicit Off/None reasoning request to Low. Unspecified reasoning
/// uses the server default; other explicit effort levels are preserved.</remarks>
internal sealed class OpenAICodexChatClient(IChatClient innerClient, string modelId, OpenAICodexModelPolicy? modelPolicy = null) : IChatClient
{
    private static readonly ChatRole DeveloperRole = new("developer");

    /// <inheritdoc />
    public void Dispose() => innerClient.Dispose();

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        return serviceType.IsInstanceOfType(this)
            ? this
            : innerClient.GetService(serviceType, serviceKey);
    }

    /// <summary>Collects the validated streaming operation using MEAI response aggregation.</summary>
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken: cancellationToken);

    /// <summary>Streams one request and rejects missing or failed terminal protocol events.</summary>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var normalized = ApplyReasoningPolicy(options);
        OpenAICodexModelPolicy.Validate(normalized?.ModelId ?? modelId, normalized, modelPolicy);
        var terminal = false;
        await foreach (var update in innerClient.GetStreamingResponseAsync(
                           ApplyMessagePolicy(messages), normalized, cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (update.RawRepresentation)
            {
                case StreamingResponseFailedUpdate:
                    throw new InvalidOperationException("The Codex response failed before completion.");
                case StreamingResponseCompletedUpdate:
                    terminal = true;
                    break;
                case StreamingResponseIncompleteUpdate incomplete:
                    terminal = true;
                    // MEAI 10.7 exposes the raw event but does not populate its finish reason.
                    update.FinishReason = incomplete.Response.IncompleteStatusDetails?.Reason switch
                    {
                        var reason when reason == ResponseIncompleteStatusReason.MaxOutputTokens => ChatFinishReason.Length,
                        var reason when reason == ResponseIncompleteStatusReason.ContentFilter => ChatFinishReason.ContentFilter,
                        _ => new ChatFinishReason("incomplete")
                    };
                    break;
            }
            if (update.Contents.OfType<ErrorContent>().Any())
                throw new InvalidOperationException("The Codex response contained an error or refusal.");
            yield return update;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!terminal)
            throw new InvalidOperationException("The Codex response stream ended without a terminal response event.");
    }

    /// <summary>Maps explicit Off to Codex's low effort without mutating caller-owned options.</summary>
    private static ChatOptions? ApplyReasoningPolicy(ChatOptions? options)
    {
        if (options?.Reasoning?.Effort != Microsoft.Extensions.AI.ReasoningEffort.None)
            return options;

        var normalized = options.Clone();
        normalized.Reasoning = new Microsoft.Extensions.AI.ReasoningOptions
        {
            Effort = Microsoft.Extensions.AI.ReasoningEffort.Low,
            Output = options.Reasoning.Output
        };
        return normalized;
    }

    internal static IReadOnlyList<ChatMessage> ApplyMessagePolicy(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var normalized = new List<ChatMessage>();
        foreach (var message in messages)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (message.Role != ChatRole.System)
            {
                normalized.Add(message);
                continue;
            }

            if (message.Contents.Any(static content => content is not TextContent))
            {
                throw new NotSupportedException(
                    "The Codex backend supports only text content in privileged conversation messages.");
            }

            var lowered = message.Clone();
            lowered.Role = DeveloperRole;
            normalized.Add(lowered);
        }

        return normalized;
    }
}
