using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.StructuredOutput;
using HPD.MultiAgent.Config;
using HPD.MultiAgent.Serialization;
using HPD.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.MultiAgent.Tests;

public sealed class DeclarativeRuntimeParityTests
{
    [Theory]
    [InlineData(HpdConfigFormat.Json, false)]
    [InlineData(HpdConfigFormat.Yaml, false)]
    [InlineData(HpdConfigFormat.Json, true)]
    [InlineData(HpdConfigFormat.Yaml, true)]
    public void ProviderPayloads_BindInlineReferencedAndRunConfig(HpdConfigFormat format, bool reference)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            var composition = Composition();
            var agent = new AgentConfig { Clients = Clients(17) };
            var node = new AgentNodeConfig { Agent = reference ? null : agent, AgentFile = reference ? "agent.json" : null,
                RunConfig = new AgentRunConfig { Clients = Clients(23) } };
            var definition = new MultiAgentWorkflowConfig { Name = "Provider test", Agents = new() { ["reviewer"] = node } };
            HpdAgentConfigSerializer.WriteFile(Path.Combine(directory, "unused.json"), new AgentConfig());
            File.WriteAllText(Path.Combine(directory, "agent.json"), HpdAgentConfigSerializer.Serialize(agent, composition));
            var path = Path.Combine(directory, format == HpdConfigFormat.Json ? "workflow.json" : "workflow.yaml");
            MultiAgentConfigSerializer.WriteFile(path, definition, composition);
            var loaded = MultiAgentConfigSerializer.ReadFile(path, composition)!;
            Assert.Equal(17, Assert.IsType<Payload>(loaded.Agents["reviewer"].Agent!.Clients.Chat!.ProviderConfig).Number);
            Assert.Equal(23, Assert.IsType<Payload>(loaded.Agents["reviewer"].RunConfig!.Clients.Chat!.ProviderConfig).Number);
            Assert.Throws<JsonException>(() => MultiAgentConfigSerializer.ReadFile(path));
            var exported = MultiAgentConfigSerializer.Serialize(loaded, composition, format);
            var roundtrip = MultiAgentConfigSerializer.Deserialize(exported, composition, format)!;
            Assert.Equal(23, Assert.IsType<Payload>(roundtrip.Agents["reviewer"].RunConfig!.Clients.Chat!.ProviderConfig).Number);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("\"additionalInstructions\":\"retired\"")]
    [InlineData("\"onError\":{\"mode\":\"Skip\"}")]
    [InlineData("\"runConfig\":{\"streamng\":{}}")]
    [InlineData("\"runConfig\":{\"clients\":{\"chat\":{\"provider\":{\"key\":\"test\"},\"providerConfig\":{\"unknown\":1}}}}")]
    public void StrictBinding_RejectsRetiredAndUnknownMembers(string member)
        => Assert.Throws<JsonException>(() => MultiAgentConfigSerializer.Deserialize("{\"name\":\"invalid\",\"agents\":{\"reviewer\":{\"agent\":{}," + member + "}}}", Composition()));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"agent\":{},\"agentFile\":\"a.json\"}")]
    [InlineData("{\"agentFile\":\" \"}")]
    public void InvalidAgentSources_FailBeforeConstruction(string node)
        => Assert.Throws<JsonException>(() => MultiAgentConfigSerializer.Deserialize("{\"name\":\"invalid\",\"agents\":{\"reviewer\":" + node + "}}"));

    [Fact]
    public async Task RunConfigAndRetry_RoundTripWithoutLosingConcerns()
    {
        var config = new MultiAgentWorkflowConfig
        {
            Name = "Roundtrip", Agents = new()
            {
                ["reviewer"] = new()
                {
                    Agent = new AgentConfig(), OnError = new() { Mode = ErrorMode.Isolate },
                    Retry = new() { OnlyTransient = false, MaxAttempts = 2, RetryableExceptionTypeNames = [typeof(TimeoutException).AssemblyQualifiedName!] },
                    RunConfig = new()
                    {
                        SystemInstructions = new() { Append = "only this question" }, Streaming = new() { CoalesceDeltas = false },
                        Context = new() { Properties = new Dictionary<string, object> { ["reviewId"] = "review-001" } }
                    }
                }
            }
        };
        var loaded = MultiAgentConfigSerializer.Deserialize(MultiAgentConfigSerializer.Serialize(config, HpdConfigFormat.Yaml), HpdConfigFormat.Yaml)!;
        var workflow = await new MultiAgentFactory().BuildAsync(loaded);
        var output = MultiAgentConfigSerializer.Deserialize(workflow.ExportConfigJson())!.Agents["reviewer"];
        Assert.False(output.Retry!.OnlyTransient);
        Assert.Equal(ErrorMode.Isolate, output.OnError!.Mode);
        Assert.False(output.RunConfig!.Streaming!.CoalesceDeltas);
        Assert.Equal("only this question", output.RunConfig.SystemInstructions!.Append);
        Assert.Equal(typeof(TimeoutException).AssemblyQualifiedName, Assert.Single(output.Retry.RetryableExceptionTypeNames!));
    }

    [Fact]
    public async Task StructuredOutput_OmittedModePreservesToolIdentityAndRejectsConflicts()
    {
        var workflow = await AgentWorkflow.Create().AddAgent("reviewer", new AgentConfig(), node =>
        {
            node.StructuredOutput<Answer>(StructuredOutputMode.Tool);
            node.RunConfig.StructuredOutput = new() { StreamPartials = false, PartialDebounceMs = 9, ToolName = "submit_review" };
        }).BuildAsync();
        var output = MultiAgentConfigSerializer.Deserialize(workflow.ExportConfigJson())!.Agents["reviewer"].RunConfig!.StructuredOutput!;
        Assert.Equal("tool", output.Mode); Assert.False(output.StreamPartials); Assert.Equal(9, output.PartialDebounceMs); Assert.Equal("submit_review", output.ToolName);
        await Assert.ThrowsAsync<AgentRunConfigurationException>(() => AgentWorkflow.Create().AddAgent("reviewer", new AgentConfig(), node =>
        { node.StructuredOutput<Answer>(StructuredOutputMode.Tool); node.RunConfig.StructuredOutput = new() { Mode = "native" }; }).BuildAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => AgentWorkflow.Create().AddAgent("reviewer", new AgentConfig(), node => node.RunConfig.StructuredOutput = new()).BuildAsync());
    }

    [Fact]
    public async Task HostBinding_CreatesAndDisposesSeparateAttemptScopes()
    {
        var events = AgentEventComposition.Create([CoreAgentEventModule.Fragment]);
        using var client = new Client();
        var host = new Host(events, client);
        var config = new AgentConfig();
        var workflow = await AgentWorkflow.Create().WithHost(host)
            .AddAgent("one", config).AddAgent("two", config)
            .From("START").To("one", "two").From("one", "two").To("END").BuildAsync();
        var collected = new List<HPD.Events.Event>();
        await foreach (var evt in workflow.ExecuteStreamingAsync("review"))
            collected.Add(evt);
        var failed = collected.OfType<WorkflowAgentCompletedEvent>().Where(evt => !evt.Success).ToArray();
        Assert.True(failed.Length == 0,
            string.Join("\n", failed.Select(evt => $"{evt.AgentId}: {evt.ErrorMessage}")));
        Assert.Equal(2, host.Created); Assert.Equal(2, host.Disposed);
        Assert.Equal(2, host.Ids.Distinct().Count());
        Assert.False(config.Clients.Chat?.Override is not null);
        Assert.DoesNotContain("attemptService", workflow.ExportConfigJson());
    }

    [Fact]
    public async Task HostBinding_RejectsMismatchedCodecAndUnauthorizedSecurity()
    {
        var events = AgentEventComposition.Create([CoreAgentEventModule.Fragment]);
        var other = AgentEventComposition.Create([CoreAgentEventModule.Fragment]);
        Assert.Throws<ArgumentException>(() => new WorkflowHostBinding(null, events, new InMemorySessionStore(other.Codec), new()));
        var host = new WorkflowHostBinding(null, events, new InMemorySessionStore(events.Codec), new());
        await Assert.ThrowsAsync<AgentRunConfigurationException>(() => AgentWorkflow.Create().WithHost(host).AddAgent("reviewer", new AgentConfig(), node =>
            node.RunConfig.Security = new() { Approval = AgentApprovalPolicy.AutoApprove }).BuildAsync());
    }

    private static AgentClientsConfig Clients(int number) => new()
    { Chat = new() { Provider = new ProviderReference { Key = "test" }, ModelName = "model", ProviderConfig = new Payload { Number = number } } };
    private static ProviderComposition Composition() => ProviderComposition.Create([new ProviderManifestFragment([new Descriptor()], [],
        [new ProviderPayloadJsonContract("test", ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(Payload),
            ParityPayloadJsonContext.Default.Payload, value => new Payload { Number = ((Payload)value).Number })], [])]);
    public sealed class Payload : IProviderConfig { public int Number { get; set; } }
    public sealed class Answer { public string Text { get; set; } = ""; }
    private sealed class Descriptor : IProviderDescriptor
    {
        public string ProviderKey => "test"; public string DisplayName => "Test"; public Uri? DocumentationUri => null;
        public IReadOnlyList<string> Aliases => [];
        public IReadOnlyDictionary<string, ProviderBackendDescriptor> Backends => new Dictionary<string, ProviderBackendDescriptor>();
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families => new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        { [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat, Lifetime = ProviderFamilyLifetime.ReusableClient } };
    }
    private sealed class Host(AgentEventComposition events, IChatClient client)
        : WorkflowHostBinding(null, events, new InMemorySessionStore(events.Codec), new())
    {
        public int Created; public int Disposed; public System.Collections.Concurrent.ConcurrentBag<string> Ids { get; } = new();
        public override void ConfigureAgent(WorkflowNodeIdentity identity, AgentBuilder builder)
            => builder.Config.Clients.Chat = new()
            {
                Override = ClientOverride<IChatClient>.Borrow(
                    client, "test", "local", "test.parity")
            };
        public override ValueTask<WorkflowRunBinding> BindAttemptAsync(WorkflowNodeIdentity identity, AgentRunConfig intent, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Created); Ids.Add(identity.ExecutionId!);
            intent.Context = new() { Properties = new Dictionary<string, object> { ["node"] = identity.NodeId, ["attemptService"] = new object() }, SharedProperties = new HashSet<string> { "attemptService" } };
            return ValueTask.FromResult(new WorkflowRunBinding(intent, new Lease(this)));
        }
        private sealed class Lease(Host host) : IAsyncDisposable
        { public ValueTask DisposeAsync() { Interlocked.Increment(ref host.Disposed); return ValueTask.CompletedTask; } }
    }
    private sealed class Client : IChatClient
    {
        public void Dispose() { }
        public object? GetService(Type type, object? key = null) => null;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        { yield return new ChatResponseUpdate(ChatRole.Assistant, "reviewed") { MessageId = Guid.NewGuid().ToString(), FinishReason = ChatFinishReason.Stop }; await Task.CompletedTask; }
    }
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DeclarativeRuntimeParityTests.Payload))]
internal partial class ParityPayloadJsonContext : JsonSerializerContext { }
