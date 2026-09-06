using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class ParentQuestionRoundTripTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task ChildReturnsAttentionAndAnswerResumesSameExecution(bool background, bool initial, bool controlled)
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var resolver = new Resolver();
        using var services = new ServiceCollection().AddSingleton<IAgentRuntimeResolver>(resolver).BuildServiceProvider();
        var childKey = new ThreadKey("session", "child");
        string? requestedExecution = null;
        var childClient = new ScriptedClient(step => step switch
        {
            0 => Call("Parent", "ask", """{"request":{"action":"ask","questions":[{"id":"choice","text":"Which environment?"}]}}"""),
            1 => Call("Parent", "complete", """{"request":{"action":"complete","report":"Verified staging result"}}"""),
            _ => throw new InvalidOperationException("Child ran beyond complete")
        });
        await using var child = await new AgentBuilder(new AgentConfig { AgentId = "worker", Name = "worker", SessionStore = store }, Composition(childClient))
            .WithEventComposition(CoreAgentEventComposition.Instance).WithServiceProvider(services)
            .WithChatClient(childClient).BuildAsync();
        resolver.Agents["worker"] = child;
        var parentClient = new ScriptedClient(step =>
        {
            if (step == 0) return Call("StartChild", "start", "{}");
            if (background && step == 1) return Call("WaitChild", "attention", "{}");
            if (background && step > 1) step--;
            if (step == 1)
            {
                var request = Assert.Single(child.GetPendingRequests(childKey).Select(p => p.Request).OfType<ParentQuestionRequestEvent>());
                requestedExecution = request.ThreadExecutionId;
                Assert.Equal(new ThreadKey("session", "main"), request.Controller);
                Assert.Equal("waiting for parent", SubAgentActivityReader.ReadAsync(store, childKey).GetAwaiter().GetResult().Status);
                var stolen = child.AnswerParentQuestionAsync(new(request.RequestId, "Parent", QuestionOutcome.Dismissed, []),
                    new("session", "unrelated"), CancellationToken.None).GetAwaiter().GetResult();
                Assert.Equal(AgentRespondStatus.TargetMismatch, stolen.Status);
                return Call("SubAgents", "answer", """{"request":{"action":"answer","child":"worker-1","requestId":"REQUEST","outcome":"Answered","answers":[{"questionId":"choice","selectedOptionIds":[],"customText":"staging"}]}}""".Replace("REQUEST", request.RequestId));
            }
            if (step == 2) return Call("WaitChild", "wait", "{}");
            return new(ChatRole.Assistant, "Done.");
        });
        var builder = new AgentBuilder(new AgentConfig { AgentId = "parent", Name = "parent", SessionStore = store }, Composition(parentClient))
            .WithEventComposition(CoreAgentEventComposition.Instance).WithServiceProvider(services).WithChatClient(parentClient);
        if (controlled)
            builder.WithNativeFunction(HPDAIFunctionFactory.Create(async (AIFunctionArguments _, FunctionExecutionContext context, CancellationToken token) =>
            {
                var submission = await SubAgentRuntime.SubmitControlledInputAsync(store, resolver, new("session", "main"), new("worker-1"),
                    "worker", childKey, new UserMessagesInputEvent { Messages = [new(ChatRole.User, "Do work")] }, token);
                Assert.Equal(AgentInputDisposition.Queued, submission.Disposition);
                return (SubAgentActionResult)new SubAgentOperationResult { Status = SubAgentOperationStatus.Running, ThreadExecutionId = submission.ThreadExecutionId };
            }, new HPDAIFunctionFactoryOptions
            {
                Name = "StartChild", ResultType = typeof(SubAgentActionResult), InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly,
                SchemaProvider = () => JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement.Clone()
            }));
        else if (initial)
            builder.WithNativeFunction(HPDAIFunctionFactory.Create(async (AIFunctionArguments _, FunctionExecutionContext context, CancellationToken token) =>
            {
                var result = await SubAgentRuntime.InvokeAsync(new()
                {
                    Definition = SubAgent.FromConfig("worker", "worker", "Do work", new AgentConfig
                    { Clients = new() { Chat = new() { Provider = new() { Key = "test" }, ModelName = "fake" } } },
                        SubAgentContextPolicy.Fresh, null, AgentInvocationModePolicy.ModelChoice, null),
                    Input = "Do work", CapabilityId = CapabilityId.Create("test:worker"), ParentContext = context,
                    RequestedMode = background ? AgentInvocationMode.Background : AgentInvocationMode.Synchronous
                }, token);
                childKey = (await new SubAgentChildRegistry(store).ProjectAsync(new("session", "main"))).AvailableChildren.Single().Value.ChildThread;
                return (SubAgentActionResult)result.ToolResult!;
            }, new HPDAIFunctionFactoryOptions
            {
                Name = "StartChild", ResultType = typeof(SubAgentActionResult),
                InvocationModePolicy = background ? AgentInvocationModePolicy.BackgroundOnly : AgentInvocationModePolicy.SynchronousOnly,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                SchemaProvider = () => JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement.Clone()
            }));
        else builder.WithNativeFunction(Control("StartChild", "continue", """{"child":"worker-1","input":"Do work"}""", background));
        builder.WithNativeFunction(Control("WaitChild", "wait", """{"children":["worker-1"],"mode":"all","timeoutSeconds":5}"""));
        await using var parent = await builder.BuildAsync();
        resolver.Agents["parent"] = parent;
        await parent.CreateSessionAsync("session");
        if (!initial)
        {
        await store.AppendThreadEventsAsync(childKey,
            [new ThreadCreatedEvent("worker", null, null, null, null, DateTime.UtcNow, ThreadKind.SubAgent,
                ThreadVisibility.Hidden, "session", "main", "worker", ParentToolCallId: "seed")]);
        await new SubAgentChildRegistry(store).RegisterAsync(new("session", "main"), new SubAgentChildReference
        {
            LocalId = new("worker-1"), RoleName = "worker", CapabilityId = CapabilityId.Create("test:worker"),
            ChildAgentId = "worker", ChildThread = childKey, CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "seed", ParentToolCallId = "seed", CreatedAt = DateTimeOffset.UtcNow,
            ExecutionPolicy = SubAgentExecutionPolicy.Create(null, new AgentClientsConfig { Chat = new() { Provider = new() { Key = "test" }, ModelName = "fake" } },
                new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource> { [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.ChildAgentConfig },
                new(), new NoSubAgentClientPropagation())
        });
        }
        await child.StartAsync();
        await parent.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await parent.RunAsync("Delegate", sessionId: "session", cancellationToken: timeout.Token);
        var history = (await store.CollectThreadEventsAsync(childKey))!;
        Assert.True(history.OfType<SubAgentResultSubmittedEvent>().Any(),
            "Child stopped without an explicit result: " + string.Join(" | ", history.OfType<ToolCallResultEvent>().Select(e => e.Result.Text)));
        Assert.Equal(requestedExecution, Assert.Single(history.OfType<SubAgentResultSubmittedEvent>()).ExecutionId);
        Assert.Equal("staging", Assert.Single(history.OfType<QuestionResponseEvent>()).Answers[0].CustomText);
        Assert.Single(history.OfType<ThreadExecutionStartedEvent>());
        Assert.Equal("completed", (await SubAgentActivityReader.ReadAsync(store, childKey)).Status);
        Assert.Empty(child.GetPendingRequests(childKey));
    }

    [Fact]
    public async Task NestedQuestionsResumeEachOriginalExecutionThroughImmediateParent()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var resolver = new Resolver();
        using var services = new ServiceCollection().AddSingleton<IAgentRuntimeResolver>(resolver).BuildServiceProvider();
        var supervisorKey = new ThreadKey("nested", "supervisor");
        var workerKey = new ThreadKey("nested", "worker");
        const string ask = """{"request":{"action":"ask","questions":[{"id":"choice","text":"Which environment?"}]}}""";
        const string complete = """{"request":{"action":"complete","report":"Verified staging"}}""";
        AgentBuilder Builder(string id, IChatClient client) => new AgentBuilder(new AgentConfig { AgentId = id, Name = id, SessionStore = store }, Composition(client))
            .WithEventComposition(CoreAgentEventComposition.Instance).WithServiceProvider(services).WithChatClient(client);
        await using var worker = await Builder("worker", new ScriptedClient(step => step == 0 ? Call("Parent", "ask", ask) : Call("Parent", "complete", complete))).BuildAsync();
        resolver.Agents["worker"] = worker;
        await using var supervisor = await Builder("supervisor", new ScriptedClient(step => step switch
        {
            0 => Call("StartChild", "start", "{}"),
            1 => Call("Parent", "ask-up", ask),
            2 => Answer(worker, workerKey, "worker-1"),
            3 => Call("WaitChild", "wait", "{}"),
            _ => Call("Parent", "complete", complete)
        })).WithNativeFunction(Control("StartChild", "continue", """{"child":"worker-1","input":"Work"}"""))
          .WithNativeFunction(Control("WaitChild", "wait", """{"children":["worker-1"],"mode":"all","timeoutSeconds":5}""")).BuildAsync();
        resolver.Agents["supervisor"] = supervisor;
        await using var root = await Builder("root", new ScriptedClient(step => step switch
        {
            0 => Call("StartChild", "start", "{}"),
            1 => Answer(supervisor, supervisorKey, "supervisor-1"),
            2 => Call("WaitChild", "wait", "{}"),
            _ => new(ChatRole.Assistant, "Done")
        })).WithNativeFunction(Control("StartChild", "continue", """{"child":"supervisor-1","input":"Delegate work"}"""))
          .WithNativeFunction(Control("WaitChild", "wait", """{"children":["supervisor-1"],"mode":"all","timeoutSeconds":5}""")).BuildAsync();
        resolver.Agents["root"] = root;
        await root.CreateSessionAsync("nested");
        await Register(new("nested", "main"), supervisorKey, "supervisor");
        await Register(supervisorKey, workerKey, "worker");
        await root.StartAsync(); await supervisor.StartAsync(); await worker.StartAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await root.RunAsync("Delegate", sessionId: "nested", cancellationToken: deadline.Token);
        foreach (var key in new[] { supervisorKey, workerKey })
        {
            var history = (await store.CollectThreadEventsAsync(key))!;
            var start = Assert.Single(history.OfType<ThreadExecutionStartedEvent>());
            Assert.Equal(start.ThreadExecutionId, Assert.Single(history.OfType<QuestionResponseEvent>()).ThreadExecutionId);
            Assert.Equal(start.ThreadExecutionId, Assert.Single(history.OfType<SubAgentResultSubmittedEvent>()).ExecutionId);
            Assert.Equal("completed", (await SubAgentActivityReader.ReadAsync(store, key)).Status);
        }

        async Task Register(ThreadKey controller, ThreadKey child, string agentId)
        {
            await store.AppendThreadEventsAsync(child, [new ThreadCreatedEvent(agentId, null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden, controller.SessionId, controller.ThreadId, agentId, ParentToolCallId: "seed")]);
            await new SubAgentChildRegistry(store).RegisterAsync(controller, new SubAgentChildReference
            {
                LocalId = new(agentId + "-1"), RoleName = agentId, CapabilityId = CapabilityId.Create("test:" + agentId),
                ChildAgentId = agentId, ChildThread = child, CreationContext = SubAgentCreationContext.Fresh,
                CreationInvocationId = "seed", ParentToolCallId = "seed", CreatedAt = DateTimeOffset.UtcNow,
                ExecutionPolicy = SubAgentExecutionPolicy.Create(null, new AgentClientsConfig { Chat = new() { Provider = new() { Key = "test" }, ModelName = "fake" } },
                    new Dictionary<ProviderClientFamily, SubAgentClientSelectionSource> { [ProviderClientFamily.Chat] = SubAgentClientSelectionSource.ChildAgentConfig },
                    new(), new NoSubAgentClientPropagation())
            });
        }
        static ChatMessage Answer(Agent child, ThreadKey key, string localId)
        {
            var pending = Assert.Single(child.GetPendingRequests(key).Select(p => p.Request).OfType<ParentQuestionRequestEvent>());
            return Call("SubAgents", "answer", """{"request":{"action":"answer","child":"CHILD","requestId":"REQUEST","outcome":"Answered","answers":[{"questionId":"choice","selectedOptionIds":[],"customText":"staging"}]}}""".Replace("CHILD", localId).Replace("REQUEST", pending.RequestId));
        }
    }

    private static ProviderComposition Composition(IChatClient client)
        => ProviderComposition.Create([new ProviderManifestFragment(
            [new SummaryDescriptor()], [new("test", ["local"], [ProviderClientFamily.Chat],
                () => new TestChatClientProvider(client))], [], [])]);

    private sealed class SummaryDescriptor : IProviderDescriptor
    {
        public string ProviderKey => "test";
        public string DisplayName => "Summary test";
        public Uri? DocumentationUri => null;
        public IReadOnlyList<string> Aliases => [];
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor> { [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat } };
        public IReadOnlyDictionary<string, ProviderBackendDescriptor> Backends { get; } =
            new Dictionary<string, ProviderBackendDescriptor> { ["local"] = new()
            {
                BackendKey = "local", IsDefault = true,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor> { [ProviderClientFamily.Chat] = new() { Family = ProviderClientFamily.Chat } },
                Authentication = [new() { Kind = ProviderAuthenticationKind.Anonymous, IsDefault = true, SupportedFamilies = new HashSet<ProviderClientFamily> { ProviderClientFamily.Chat } }]
            } };
    }

    private static AIFunction Control(string name, string action, string json, bool background = false)
        => HPDAIFunctionFactory.Create(async (AIFunctionArguments _, FunctionExecutionContext context, CancellationToken token) =>
            {
                try { return await SubAgentRuntime.ControlAsync(action, JsonDocument.Parse(json).RootElement, context, token); }
                catch (Exception exception) { throw new InvalidOperationException(exception.ToString(), exception); }
            },
            new HPDAIFunctionFactoryOptions
            {
                InvocationModePolicy = background ? AgentInvocationModePolicy.BackgroundOnly : AgentInvocationModePolicy.SynchronousOnly,
                Name = name, ResultType = typeof(SubAgentActionResult), InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                SchemaProvider = () => JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement.Clone()
            });

    private static ChatMessage Call(string name, string id, string json)
    {
        var arguments = JsonDocument.Parse(json).RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
        return new(ChatRole.Assistant, [new FunctionCallContent(id, name, arguments)]);
    }

    private sealed class Resolver : IAgentRuntimeResolver
    {
        internal Dictionary<string, Agent> Agents { get; } = [];
        public Task<IAgentRuntimeLease> GetOrBuildAsync(string agentId, string sessionId, string threadId, CancellationToken cancellationToken = default)
            => Task.FromResult<IAgentRuntimeLease>(new Lease(Agents[agentId]));
        private sealed record Lease(Agent Agent) : IAgentRuntimeLease
        { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }

    private sealed class ScriptedClient(Func<int, ChatMessage> next) : IChatClient
    {
        private int _step;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(next(_step++)));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            ChatMessage message;
            try { message = next(_step++); }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Tool results: " + string.Join(" | ", messages.SelectMany(m => m.Contents)
                    .OfType<FunctionResultContent>().Select(r => r.Result?.ToString())), exception);
            }
            yield return new() { Contents = message.Contents, FinishReason = message.Contents.Any(c => c is FunctionCallContent)
                ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop };
        }
        public object? GetService(Type type, object? key = null) => type == typeof(ProviderClientExecutionIdentity)
            ? ProviderClientExecutionIdentity.CreateSafe("test", "test", ProviderClientFamily.Chat, "fake", "test/chat", "test/final") : null;
        public void Dispose() { }
    }
}
