using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Providers;

public sealed class TransferredClientOverrideLifetimeTests
{
    [Fact]
    public async Task AgentLifetimeTransfer_ClonedConfigurationsLeaseOneConsumedHandle()
    {
        var client = new FakeChatClient();
        var owner = new CountingOwner();
        var transferred = ClientOverride<IChatClient>.Transfer(
            client, owner, RuntimeOverrideLifetime.Agent, "test", "local");
        var original = new ChatClientConfig { Override = transferred };
        var clone = (ChatClientConfig)ProviderClientConfigSnapshot.Clone(original);
        var agent = new AgentConfig
        {
            Clients = new AgentClientsConfig { Chat = original }
        };

        await using var resolver = new AgentChatClientResolver(null, null);
        await using (var first = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = agent,
            RunConfig = new AgentRunConfig
            {
                Clients = new AgentClientsConfig { Chat = clone }
            }
        }))
        {
            Assert.Same(client, first.Client);
        }

        await using (var second = await resolver.ResolveAsync(new AgentChatClientResolutionRequest
        {
            AgentConfig = agent
        }))
        {
            Assert.Same(client, second.Client);
        }

        Assert.Equal(0, owner.DisposeCount);
        await resolver.DisposeAsync();
        Assert.Equal(1, owner.DisposeCount);
    }

    private sealed class CountingOwner : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
