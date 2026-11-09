using System.Runtime.CompilerServices;
using HPD.Agent;
using HPD.Agent.OpenApi;
using Microsoft.Extensions.AI;

namespace HPD.Agent.OpenApi.Tests.Agent;

public sealed class OpenApiBuilderIntegrationTests
{
    [Fact]
    public async Task WithOpenApi_AgentBuilderExposesGeneratedTools()
    {
        var client = new CaptureChatClient();
        var specPath = Path.Combine(AppContext.BaseDirectory, "TestSpecs", "petstore.json");

        await using var agent = await new AgentBuilder(new AgentConfig { Name = "openapi-builder" })
            .WithChatClient(client)
            .WithOpenApi("petstore", specPath)
            .BuildAsync();

        var sessionId = await agent.CreateSessionAsync();
        await agent.RunAsync("List the tools.", sessionId);

        Assert.Contains("petstore_listPets", client.ToolNames);
        Assert.Contains("petstore_getPetById", client.ToolNames);
    }

    [Fact]
    public async Task WithOpenApi_CollapsedSourceBuildsContainerHierarchy()
    {
        var client = new CaptureChatClient();
        var specPath = Path.Combine(AppContext.BaseDirectory, "TestSpecs", "petstore.json");

        await using var agent = await new AgentBuilder(new AgentConfig { Name = "openapi-collapsed" })
            .WithChatClient(client)
            .WithOpenApi("petstore", specPath, options =>
            {
                options.CollapseWithinToolHarness = true;
            })
            .BuildAsync();

        var sessionId = await agent.CreateSessionAsync();
        await agent.RunAsync("List the tools.", sessionId);

        Assert.Contains("OpenApi_petstore", client.ToolNames);
        Assert.DoesNotContain("petstore_listPets", client.ToolNames);
    }

    [Fact]
    public async Task WithOpenApi_CollapsedContainerRevealsOperationTools()
    {
        var client = new ExpandingChatClient();
        var specPath = Path.Combine(AppContext.BaseDirectory, "TestSpecs", "petstore.json");

        await using var agent = await new AgentBuilder(new AgentConfig { Name = "openapi-expand" })
            .WithChatClient(client)
            .WithOpenApi("petstore", specPath, options =>
            {
                options.CollapseWithinToolHarness = true;
            })
            .BuildAsync();

        var sessionId = await agent.CreateSessionAsync();
        var result = await agent.RunAsync("Expand Petstore and list its tools.", sessionId);

        Assert.Equal("OpenAPI tools expanded.", result.Text);
        Assert.Contains("petstore_listPets", client.ExpandedToolNames);
    }

    private sealed class CaptureChatClient : IChatClient
    {
        internal List<string> ToolNames { get; } = [];

        public ChatClientMetadata Metadata { get; } = new("capture", null, "test");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Capture(options);
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "done"))
            {
                FinishReason = ChatFinishReason.Stop
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Capture(options);
            await Task.CompletedTask;
            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent("done")],
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }

        private void Capture(ChatOptions? options)
        {
            ToolNames.Clear();
            ToolNames.AddRange(options?.Tools?.OfType<AIFunction>().Select(tool => tool.Name) ?? []);
        }
    }

    private sealed class ExpandingChatClient : IChatClient
    {
        internal List<string> ExpandedToolNames { get; } = [];
        private int _calls;

        public ChatClientMetadata Metadata { get; } = new("expanding", null, "test");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The expansion test uses streaming.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            var toolNames = options?.Tools?
                .OfType<AIFunction>()
                .Select(static tool => tool.Name)
                .ToArray() ?? [];

            if (_calls++ == 0)
            {
                Assert.Contains("OpenApi_petstore", toolNames);
                yield return new ChatResponseUpdate
                {
                    Contents =
                    [
                        new FunctionCallContent(
                            "expand-petstore",
                            "OpenApi_petstore",
                            new Dictionary<string, object?>())
                    ],
                    FinishReason = ChatFinishReason.ToolCalls
                };
                yield break;
            }

            ExpandedToolNames.AddRange(toolNames);
            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent("OpenAPI tools expanded.")],
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
