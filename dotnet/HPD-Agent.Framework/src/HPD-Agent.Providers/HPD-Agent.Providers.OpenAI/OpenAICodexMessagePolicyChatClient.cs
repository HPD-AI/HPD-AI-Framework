using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Lowers provider-neutral privileged conversation messages to the closed Codex
/// Responses message policy without changing the public OpenAI platform backend.
/// </summary>
internal sealed class OpenAICodexMessagePolicyChatClient(IChatClient innerClient) : IChatClient
{
    private static readonly ChatRole DeveloperRole = new("developer");

    public void Dispose() => innerClient.Dispose();

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        return serviceType.IsInstanceOfType(this)
            ? this
            : innerClient.GetService(serviceType, serviceKey);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        innerClient.GetResponseAsync(
            ApplyMessagePolicy(messages),
            options,
            cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        innerClient.GetStreamingResponseAsync(
            ApplyMessagePolicy(messages),
            options,
            cancellationToken);

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
