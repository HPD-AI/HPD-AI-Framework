using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent.Tests;

public sealed class StructuredConfigTests
{
    [Theory]
    [InlineData(StructuredOutputMode.Native)]
    [InlineData(StructuredOutputMode.Tool)]
    public async Task Yaml_LoadAndExport_PreservesStructuredTypeAndMode(StructuredOutputMode mode)
    {
        var path = Path.Combine(Path.GetTempPath(), $"structured-{Guid.NewGuid():N}.yaml");
        try
        {
            MultiAgentConfigSerializer.WriteFile(path, Config(typeof(Answer).AssemblyQualifiedName!, mode));
            var workflow = await MultiAgent.FromFile(path).BuildAsync();
            using var json = JsonDocument.Parse(workflow.ExportConfigJson());
            var node = json.RootElement.GetProperty("agents").GetProperty("reviewer");
            Assert.Equal(typeof(Answer).AssemblyQualifiedName, node.GetProperty("structuredOutputType").GetString());
            Assert.Equal(mode.ToString(), node.GetProperty("structuredOutputMode").GetString());
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Missing.OutputType, Missing.Assembly")]
    [InlineData("System.Int32")]
    public void InvalidStructuredType_FailsDuringConfiguration(string? type)
    {
        Assert.Throws<ArgumentException>(() => new MultiAgentFactory().CreateBuilder(Config(type)));
    }

    [Theory]
    [InlineData("{\"value\":\"accepted\"}", true)]
    [InlineData("not valid json", false)]
    public async Task StructuredNode_UsesTypedExecution_AndRejectsInvalidAnswers(string response, bool valid)
    {
        using var client = new JsonClient(response);
        var path = Path.Combine(Path.GetTempPath(), $"structured-run-{Guid.NewGuid():N}.yaml");
        MultiAgentWorkflowConfig loaded;
        try
        {
            MultiAgentConfigSerializer.WriteFile(path, Config(typeof(Answer).AssemblyQualifiedName!));
            loaded = MultiAgentConfigSerializer.ReadFile(path)!;
        }
        finally { File.Delete(path); }
        // Host-only runtime capabilities are intentionally not serialized.
        var agentConfig = loaded.Agents["reviewer"].Agent!;
        agentConfig.EventComposition = AnswerComposition;
        agentConfig.Clients = new AgentClientsConfig
        {
            Chat = new ChatClientConfig { Override = ClientOverride<IChatClient>.Borrow(client, "test", "local", "test.adapter") }
        };
        var workflow = await new MultiAgentFactory().CreateBuilder(loaded)
            .WithSessionStore(new InMemorySessionStore(AnswerComposition.Codec))
            .WithConversation(MultiAgentConversationPolicies.ThreadPerAgent("review-structured"))
            .BuildAsync();
        var events = new List<HPD.Events.Event>();
        await foreach (var evt in workflow.ExecuteStreamingAsync("Review")) events.Add(evt);
        Assert.NotNull(client.Format);
        if (valid)
        {
            Assert.True(events.OfType<StructuredResultEvent<Answer>>().Any(e => !e.IsPartial && e.Value.Value == "accepted" && e.SessionId == "review-structured"), string.Join("\n", events.Select(e => e.ToString())));
            Assert.Contains(events.OfType<WorkflowAgentCompletedEvent>(), e => e.AgentId == "reviewer" && e.Success);
        }
        else
        {
            Assert.DoesNotContain(events.OfType<WorkflowAgentCompletedEvent>(), e => e.AgentId == "reviewer" && e.Success);
        }
    }

    private static MultiAgentWorkflowConfig Config(string? type, StructuredOutputMode mode = StructuredOutputMode.Native) => new()
    {
        Name = "Structured review",
        Agents = new()
        {
            ["reviewer"] = new() { Agent = new AgentConfig(), OutputMode = AgentOutputMode.Structured,
                StructuredOutputType = type, StructuredOutputMode = mode }
        }
    };

    private static AgentEventComposition AnswerComposition { get; } = AgentEventComposition.Create([
        CoreAgentEventModule.Fragment,
        new AgentEventModuleFragment
        {
            ModuleId = "test.structured-answer",
            Events = [new AgentEventDescriptor
            {
                ModuleId = "test.structured-answer", Discriminator = "TEST_STRUCTURED_ANSWER",
                EventType = typeof(StructuredResultEvent<Answer>),
                JsonTypeInfo = StructuredConfigJsonContext.Default.GetTypeInfo(typeof(StructuredResultEvent<Answer>))!,
                Durability = AgentEventDurability.Durable
            }]
        }
    ]);

    public sealed class Answer { public string Value { get; set; } = ""; }

    private sealed class JsonClient(string response) : IChatClient
    {
        public ChatResponseFormat? Format { get; private set; }
        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Format = options?.ResponseFormat;
            yield return new ChatResponseUpdate(ChatRole.Assistant, response) { MessageId = "answer", FinishReason = ChatFinishReason.Stop };
            await Task.CompletedTask;
        }
    }
}

[JsonSerializable(typeof(StructuredResultEvent<StructuredConfigTests.Answer>))]
internal partial class StructuredConfigJsonContext : JsonSerializerContext { }
