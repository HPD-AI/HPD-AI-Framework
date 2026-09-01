using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.ToolHarness.AotFixture;
using Microsoft.Extensions.AI;

await using (var agent = await new AgentBuilder(new AgentConfig
    {
        Name = "cross-assembly-toolharness-aot",
        MaxAgenticIterations = 5
    })
    .WithChatClient(new HarnessClient())
    .WithEventComposition(CoreAgentEventComposition.Instance)
    .WithToolHarness<ExternalExecutionHarness>()
    .BuildAsync())
{
    await agent.RunAsync("run-cross-assembly-harness");
}

return ExternalExecutionMiddleware.CreatedCount == 1 &&
       ExternalExecutionMiddleware.ActivatedCount == 1 &&
       ExternalExecutionMiddleware.DisposedCount == 1
    ? 0
    : 1;

internal sealed class HarnessClient : IChatClient
{
    private readonly ConcurrentDictionary<string, int> _stages = new(StringComparer.Ordinal);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var key = chatMessages
            .Where(message => message.Role == ChatRole.User)
            .SelectMany(message => message.Contents)
            .OfType<TextContent>()
            .Select(content => content.Text)
            .First();
        var stage = _stages.AddOrUpdate(key, 1, static (_, current) => current + 1);
        return Task.FromResult(stage switch
        {
            1 => ToolCall(nameof(ExternalExecutionHarness), "expand"),
            2 => ToolCall(nameof(ExternalExecutionHarness.Ping), "ping"),
            _ => new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")])
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        foreach (var message in response.Messages)
        {
            yield return new ChatResponseUpdate
            {
                Role = message.Role,
                Contents = message.Contents,
                FinishReason = message.Contents.OfType<FunctionCallContent>().Any()
                    ? ChatFinishReason.ToolCalls
                    : ChatFinishReason.Stop
            };
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    private static ChatResponse ToolCall(string name, string callId) => new(
        [new ChatMessage(ChatRole.Assistant,
            [(AIContent)new FunctionCallContent(callId, name, new Dictionary<string, object?>())])]);
}
