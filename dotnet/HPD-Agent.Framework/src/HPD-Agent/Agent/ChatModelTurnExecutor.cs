using System.Runtime.CompilerServices;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal sealed class ChatModelTurnExecutor : IAgentModelTurnExecutor
{
    private readonly AgentTurn _agentTurn;

    public ChatModelTurnExecutor(AgentTurn agentTurn)
    {
        _agentTurn = agentTurn ?? throw new ArgumentNullException(nameof(agentTurn));
    }

    public AgentModelTransport Transport => AgentModelTransport.Chat;

    public async IAsyncEnumerable<AgentModelUpdate> RunAsync(
        AgentModelTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (request.Transport is not AgentModelTransport.Chat)
        {
            throw new InvalidOperationException(
                $"Chat model turn executor cannot run '{request.Transport}' transport.");
        }

        if (request.ChatModel is null)
        {
            throw new InvalidOperationException(
                "No chat model is configured for this agent run. Configure Clients.Chat on AgentConfig or AgentRunConfig, including Clients.Chat.Override when supplying a client directly.");
        }

        await foreach (var update in _agentTurn.RunAsync(
                           request.Messages,
                           request.Options,
                           request.ChatModel,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return new AgentChatModelUpdate(update);
        }
    }
}
