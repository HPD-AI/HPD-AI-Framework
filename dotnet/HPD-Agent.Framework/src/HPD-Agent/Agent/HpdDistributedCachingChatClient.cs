using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Uses Microsoft's distributed response cache while excluding requests whose response state is
/// owned by the provider.
/// </summary>
internal sealed class HpdDistributedCachingChatClient(
    IChatClient innerClient,
    Microsoft.Extensions.Caching.Distributed.IDistributedCache storage)
    : DistributedCachingChatClient(innerClient, storage)
{
    protected override bool EnableCaching(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options)
    {
        return options?.AllowBackgroundResponses != true &&
               options?.ContinuationToken is null &&
               base.EnableCaching(messages, options);
    }
}
