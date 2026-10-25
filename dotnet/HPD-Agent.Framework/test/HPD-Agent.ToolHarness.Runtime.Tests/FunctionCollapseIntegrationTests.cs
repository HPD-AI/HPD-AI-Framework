using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Collapsing;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests;

public sealed class FunctionCollapseIntegrationTests
{
    [Fact]
    public async Task CollapsedActionFunctionRequiresBothHarnessAndFunctionActivation()
    {
        await using var agent = await BuildAgentAsync();
        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToList();
        var visibility = new ToolVisibilityManager(tools);

        var initial = visibility.GetToolsForAgentTurn(tools, ImmutableHashSet<string>.Empty);
        Assert.Contains(initial, tool => tool.Name == nameof(FunctionCollapseTestHarness));
        Assert.DoesNotContain(initial, tool => tool.Name == "CollapsedEchoTools");
        Assert.DoesNotContain(initial, tool => tool.Name == "CollapsedEcho");

        var harnessExpanded = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(nameof(FunctionCollapseTestHarness)));
        Assert.Contains(harnessExpanded, tool => tool.Name == "CollapsedEchoTools");
        Assert.Contains(harnessExpanded, tool => tool.Name == "Visible");
        Assert.DoesNotContain(harnessExpanded, tool => tool.Name == "CollapsedEcho");

        var functionExpanded = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(
                nameof(FunctionCollapseTestHarness),
                "CollapsedEchoTools"));
        Assert.Contains(functionExpanded, tool => tool.Name == "CollapsedEcho");
        Assert.DoesNotContain(functionExpanded, tool => tool.Name == "CollapsedEchoTools");
    }

    [Fact]
    public async Task NeverCollapseFunctionsForcesOnlyTheTargetOpen()
    {
        await using var agent = await BuildAgentAsync();
        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToList();
        var visibility = new ToolVisibilityManager(tools);

        var expanded = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(nameof(FunctionCollapseTestHarness)),
            neverCollapseFunctions: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"{nameof(FunctionCollapseTestHarness)}.Echo"
            });

        Assert.Contains(expanded, tool => tool.Name == "CollapsedEcho");
        Assert.DoesNotContain(expanded, tool => tool.Name == "CollapsedEchoTools");
    }

    [Fact]
    public async Task OrdinaryFunctionUsesTheSameFunctionActivationPath()
    {
        await using var agent = await BuildAgentAsync();
        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToList();
        var visibility = new ToolVisibilityManager(tools);

        var harnessExpanded = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(nameof(FunctionCollapseTestHarness)));
        Assert.Contains(harnessExpanded, tool => tool.Name == "CollapsedDataTools");
        Assert.DoesNotContain(harnessExpanded, tool => tool.Name == "CollapsedData");

        var targetExpanded = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(nameof(FunctionCollapseTestHarness), "CollapsedDataTools"));
        Assert.Contains(targetExpanded, tool => tool.Name == "CollapsedData");
    }

    [Fact]
    public async Task DisablingCollapsingLeavesTheOriginalFunctionDirectlyVisible()
    {
        await using var agent = await BuildAgentAsync(collapsingEnabled: false);
        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToList();
        var visibility = new ToolVisibilityManager(tools);

        Assert.DoesNotContain(tools, tool => tool.Name == "CollapsedEchoTools");
        var expanded = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(nameof(FunctionCollapseTestHarness)));
        Assert.Contains(expanded, tool => tool.Name == "CollapsedEcho");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalFunctionAndActivationShareAdmission(bool enabled)
    {
        var metadata = new FunctionCollapseTestMetadata { EnableConditional = enabled };
        var factory = Assert.Single(HPD.Agent.Generated.ToolHarnessRegistry.All,
            candidate => candidate.ToolHarnessType == typeof(FunctionCollapseTestHarness));
        Assert.Equal(typeof(FunctionCollapseTestMetadata), factory.MetadataType);
        var directlyGenerated = factory.CreateFunctions(factory.CreateInstance(), metadata, null);
        Assert.True(enabled == directlyGenerated.Any(tool => tool.Name == "Conditional"),
            $"Conditional metadata was {metadata.EnableConditional}; generated: {string.Join(", ", directlyGenerated.Select(tool => tool.Name))}");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "function-collapse-conditional",
            Collapsing = new CollapsingConfig()
        })
        .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
        .WithToolHarness<FunctionCollapseTestHarness>(metadata)
        .BuildAsync();
        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToList();

        Assert.Equal(enabled, tools.Any(tool => tool.Name == "Conditional"));
        Assert.Equal(enabled, tools.Any(tool => tool.Name == "CollapsedConditionalTools"));
    }

    [Fact]
    public async Task RuntimeActivatesAndExecutesTheOriginalFunction()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall(nameof(FunctionCollapseTestHarness), "expand-harness");
        client.EnqueueToolCall("CollapsedDataTools", "expand-function");
        using var payload = JsonDocument.Parse("""{"input":{"value":"hello","count":2}}""");
        client.EnqueueToolCall("CollapsedData", "data", new Dictionary<string, object?>
        {
            ["input"] = payload.RootElement.GetProperty("input").Clone()
        });
        client.EnqueueTextResponse("done");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "function-collapse-runtime",
            Collapsing = new CollapsingConfig()
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();

        var factory = Assert.Single(HPD.Agent.Generated.ToolHarnessRegistry.All,
            candidate => candidate.ToolHarnessType == typeof(FunctionCollapseTestHarness));
        Assert.Contains(factory.Middleware ?? [], descriptor =>
            descriptor.MiddlewareType == typeof(FunctionCollapseProbeMiddleware));
        var target = Assert.Single(agent.DefaultOptions!.Tools!.OfType<AIFunction>(),
            function => function.Name == "CollapsedData");
        Assert.Equal(nameof(FunctionCollapseTestHarness),
            target.AdditionalProperties!["ToolHarnessName"]);
        var containerMiddleware = Assert.Single(agent.Middlewares.OfType<ContainerMiddleware>());
        var ownerMap = Assert.IsType<Dictionary<string, string>>(
            typeof(ContainerMiddleware).GetField(
                "_itemToToolHarnessMap",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(containerMiddleware));
        Assert.Equal(nameof(FunctionCollapseTestHarness), ownerMap["CollapsedData"]);
        FunctionCollapseProbeMiddleware.Reset();
        FunctionCollapseTestHarness.ResetDataCalls();
        await agent.RunAsync("use the collapsed echo");

        Assert.True(client.CapturedRequestSnapshots.Count >= 4);
        Assert.Contains(nameof(FunctionCollapseTestHarness), client.CapturedRequestSnapshots[0].ToolNames);
        Assert.DoesNotContain("CollapsedDataTools", client.CapturedRequestSnapshots[0].ToolNames);
        Assert.Contains("CollapsedDataTools", client.CapturedRequestSnapshots[1].ToolNames);
        Assert.Contains("CollapsedData", client.CapturedRequestSnapshots[2].ToolNames);
        Assert.DoesNotContain("CollapsedDataTools", client.CapturedRequestSnapshots[2].ToolNames);
        Assert.True(FunctionCollapseProbeMiddleware.ActivationCalls > 0);
        Assert.True(FunctionCollapseProbeMiddleware.BeforeCalls > 0);
        Assert.True(FunctionCollapseProbeMiddleware.WrapCalls > 0);
        Assert.True(FunctionCollapseTestHarness.DataCalls == 1,
            $"Data tool was not executed. Schema: {target.JsonSchema.GetRawText()}\n" +
            $"Results: {string.Join("\n", client.CapturedRequests[^1].SelectMany(message => message.Contents).OfType<FunctionResultContent>().Select(content => content.Result?.ToString()))}");
        Assert.Contains("CollapsedData", FunctionCollapseProbeMiddleware.SeenNames);
    }

    [Fact]
    public async Task PerRunOverrideReachesTheContainerMiddleware()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall(nameof(FunctionCollapseTestHarness), "expand-harness");
        client.EnqueueTextResponse("done");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "function-collapse-run-policy",
            Collapsing = new CollapsingConfig()
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();
        await agent.CreateSessionAsync("function-collapse-run-policy-session");

        await agent.RunAsync(new UserMessagesInputEvent
        {
            Messages = [new(ChatRole.User, "expand the harness")],
            SessionId = "function-collapse-run-policy-session",
            ThreadId = "main",
            RunConfig = new AgentRunConfig
            {
                Collapsing = new CollapsingRunPolicy
                {
                    NeverCollapseFunctions = [nameof(FunctionCollapseTestHarness).ToLowerInvariant() + ".echo"]
                }
            }
        });

        Assert.True(client.CapturedRequestSnapshots.Count >= 2);
        Assert.Contains("CollapsedEcho", client.CapturedRequestSnapshots[1].ToolNames);
        Assert.DoesNotContain("CollapsedEchoTools", client.CapturedRequestSnapshots[1].ToolNames);
    }

    [Fact]
    public async Task HiddenFunctionRecoveryExpandsItsActivation()
    {
        var client = new FakeChatClient();
        using var payload = JsonDocument.Parse("""{"input":{"value":"recovered","count":2}}""");
        var arguments = new Dictionary<string, object?>
        {
            ["input"] = payload.RootElement.GetProperty("input").Clone()
        };
        client.EnqueueToolCall(nameof(FunctionCollapseTestHarness), "expand-harness");
        client.EnqueueToolCall("CollapsedData", "hidden-call", arguments);
        client.EnqueueToolCall("CollapsedData", "recovered-call", arguments);
        client.EnqueueTextResponse("done");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "function-collapse-recovery",
            Collapsing = new CollapsingConfig { EnableErrorRecovery = true }
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();
        FunctionCollapseTestHarness.ResetDataCalls();

        await agent.RunAsync("recover the hidden function");

        Assert.Equal(1, FunctionCollapseTestHarness.DataCalls);
        Assert.True(client.CapturedRequestSnapshots.Count >= 4);
        Assert.Contains("CollapsedData", client.CapturedRequestSnapshots[2].ToolNames);
    }

    [Fact]
    public async Task FunctionActivationWithArgumentsRecovers()
    {
        var client = new FakeChatClient();
        using var payload = JsonDocument.Parse("""{"input":{"value":"arguments","count":2}}""");
        var arguments = new Dictionary<string, object?>
        {
            ["input"] = payload.RootElement.GetProperty("input").Clone()
        };
        client.EnqueueToolCall(nameof(FunctionCollapseTestHarness), "expand-harness");
        client.EnqueueToolCall("CollapsedDataTools", "expand-with-args", new()
        {
            ["unexpected"] = new Dictionary<string, object?>()
        });
        client.EnqueueToolCall("CollapsedData", "valid-call", arguments);
        client.EnqueueTextResponse("done");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "function-activation-with-arguments",
            Collapsing = new CollapsingConfig { EnableErrorRecovery = true }
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();
        FunctionCollapseTestHarness.ResetDataCalls();

        await agent.RunAsync("recover activation arguments");

        Assert.Equal(1, FunctionCollapseTestHarness.DataCalls);
        Assert.Contains("CollapsedData", client.CapturedRequestSnapshots[2].ToolNames);
        Assert.DoesNotContain("CollapsedDataTools", client.CapturedRequestSnapshots[2].ToolNames);
    }

    [Fact]
    public async Task QualifiedHiddenFunctionRecovers()
    {
        var client = new FakeChatClient();
        using var payload = JsonDocument.Parse("""{"input":{"value":"qualified","count":2}}""");
        var arguments = new Dictionary<string, object?>
        {
            ["input"] = payload.RootElement.GetProperty("input").Clone()
        };
        client.EnqueueToolCall(nameof(FunctionCollapseTestHarness), "expand-harness");
        client.EnqueueToolCall("CollapsedDataTools.CollapsedData", "qualified-call", arguments);
        client.EnqueueToolCall("CollapsedData", "valid-call", arguments);
        client.EnqueueTextResponse("done");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "qualified-function-recovery",
            Collapsing = new CollapsingConfig { EnableErrorRecovery = true }
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();
        FunctionCollapseTestHarness.ResetDataCalls();

        await agent.RunAsync("recover qualified function");

        Assert.Equal(1, FunctionCollapseTestHarness.DataCalls);
        Assert.Contains("CollapsedData", client.CapturedRequestSnapshots[2].ToolNames);
        Assert.DoesNotContain("CollapsedDataTools", client.CapturedRequestSnapshots[2].ToolNames);
    }

    [Fact]
    public async Task UnknownDeclarationSelectorFailsBeforeExecution()
    {
        var error = await Assert.ThrowsAsync<AgentRunConfigurationException>(() =>
            new AgentBuilder(new AgentConfig
            {
                Name = "invalid-function-collapse-selector",
                Collapsing = new CollapsingConfig
                {
                    NeverCollapseFunctions = [
                        nameof(FunctionCollapseTestHarness) + ".Debgu"
                    ]
                }
            })
            .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
            .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
            .BuildAsync());

        Assert.Equal("function_collapse_selector_invalid", error.Code);
        Assert.Contains("Debgu", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentRunsKeepDifferentFunctionCollapsePoliciesIsolated()
    {
        var client = new ConcurrentPolicyChatClient();
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "function-collapse-concurrent-policy",
            Collapsing = new CollapsingConfig()
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();
        await agent.CreateSessionAsync("function-collapse-open");
        await agent.CreateSessionAsync("function-collapse-closed");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.WhenAll(
            agent.RunAsync(new UserMessagesInputEvent
            {
                Messages = [new(ChatRole.User, "open-policy")],
                SessionId = "function-collapse-open",
                ThreadId = "main",
                RunConfig = new AgentRunConfig
                {
                    Collapsing = new CollapsingRunPolicy
                    {
                        NeverCollapseFunctions = [nameof(FunctionCollapseTestHarness) + ".Echo"]
                    }
                }
            }, timeout.Token),
            agent.RunAsync(new UserMessagesInputEvent
            {
                Messages = [new(ChatRole.User, "closed-policy")],
                SessionId = "function-collapse-closed",
                ThreadId = "main",
                RunConfig = new AgentRunConfig
                {
                    Collapsing = new CollapsingRunPolicy
                    {
                        NeverCollapseFunctions = []
                    }
                }
            }, timeout.Token));

        var openRequests = client.RequestsFor("open-policy");
        var closedRequests = client.RequestsFor("closed-policy");
        Assert.Equal(2, openRequests.Count);
        Assert.Equal(2, closedRequests.Count);
        Assert.Contains("CollapsedEcho", openRequests[1]);
        Assert.DoesNotContain("CollapsedEchoTools", openRequests[1]);
        Assert.Contains("CollapsedEchoTools", closedRequests[1]);
        Assert.DoesNotContain("CollapsedEcho", closedRequests[1]);
    }

    [Fact]
    public async Task CapabilityRefreshRepublishesOneConsistentActivationGraph()
    {
        await using var agent = await BuildAgentAsync();

        await agent.RefreshCapabilitiesAsync();
        await agent.RefreshCapabilitiesAsync();

        var tools = agent.DefaultOptions!.Tools!.OfType<AIFunction>().ToList();
        var activation = Assert.Single(tools, tool => tool.Name == "CollapsedEchoTools");
        var target = Assert.Single(tools, tool => tool.Name == "CollapsedEcho");
        var activationMetadata = (HPDCapabilityMetadata)activation.AdditionalProperties![
            HPDCapabilityMetadata.AdditionalPropertiesKey]!;
        var targetMetadata = (HPDCapabilityMetadata)target.AdditionalProperties![
            HPDCapabilityMetadata.AdditionalPropertiesKey]!;
        Assert.Equal(targetMetadata.Id, Assert.Single(activationMetadata.Reveals));
        Assert.Equal(activationMetadata.Id, Assert.Single(targetMetadata.ParentContainerIds));

        var visibility = new ToolVisibilityManager(tools);
        var visible = visibility.GetToolsForAgentTurn(
            tools,
            ImmutableHashSet.Create(nameof(FunctionCollapseTestHarness)));
        Assert.Contains(visible, tool => tool.Name == "CollapsedEchoTools");
        Assert.DoesNotContain(visible, tool => tool.Name == "CollapsedEcho");
    }

    [Fact]
    public async Task SameNameHistoryFilteringRemovesActivationButKeepsTargetCall()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall(nameof(SameNameCollapseHarness), "expand-harness");
        client.EnqueueToolCall("Echo", "expand-function");
        client.EnqueueToolCall("Echo", "call-target");
        client.EnqueueTextResponse("done");
        await using var agent = await new AgentBuilder(new AgentConfig
        {
            Name = "same-name-history",
            Collapsing = new CollapsingConfig()
        })
        .WithChatClient(new IdentifiedClient(client))
        .WithToolHarness<SameNameCollapseHarness>()
        .BuildAsync();

        await agent.RunAsync("exercise same-name collapse");

        var calls = client.CapturedRequests[^1]
            .SelectMany(message => message.Contents)
            .OfType<FunctionCallContent>()
            .Select(call => call.CallId)
            .ToArray();
        Assert.DoesNotContain("expand-function", calls);
        Assert.Contains("call-target", calls);
    }

    private static Task<Agent> BuildAgentAsync(bool collapsingEnabled = true)
        => new AgentBuilder(new AgentConfig
        {
            Name = "function-collapse-integration",
            Collapsing = new CollapsingConfig { Enabled = collapsingEnabled }
        })
        .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
        .WithToolHarness<FunctionCollapseTestHarness>(DefaultMetadata())
        .BuildAsync();

    private static FunctionCollapseTestMetadata DefaultMetadata() =>
        new() { EnableConditional = true };

    private sealed class IdentifiedClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(ProviderClientExecutionIdentity)
                ? ProviderClientExecutionIdentity.CreateSafe(
                    "test",
                    "test",
                    ProviderClientFamily.Chat,
                    "fake",
                    "test/chat",
                    "test/final")
                : base.GetService(type, key);
    }

    private sealed class ConcurrentPolicyChatClient : IChatClient
    {
        private readonly ConcurrentDictionary<string, int> _stages = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ConcurrentQueue<IReadOnlyList<string>>> _requests =
            new(StringComparer.Ordinal);

        public IReadOnlyList<IReadOnlyList<string>> RequestsFor(string mode) =>
            _requests.TryGetValue(mode, out var requests) ? requests.ToArray() : [];

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var mode = chatMessages
                .Where(static message => message.Role == ChatRole.User)
                .SelectMany(static message => message.Contents)
                .OfType<TextContent>()
                .Select(static content => content.Text)
                .Last(static text => text is "open-policy" or "closed-policy");
            var request = (options?.Tools ?? [])
                .Select(static tool => tool.Name)
                .ToArray();
            var queue = _requests.GetOrAdd(mode, static _ => new ConcurrentQueue<IReadOnlyList<string>>());
            queue.Enqueue(request);
            var stage = _stages.AddOrUpdate(mode, 1, static (_, current) => current + 1);
            if (stage == 1)
            {
                return new ChatResponse(
                    [new ChatMessage(ChatRole.Assistant,
                        [(AIContent)new FunctionCallContent(
                            $"{mode}-harness",
                            nameof(FunctionCollapseTestHarness),
                            new Dictionary<string, object?>())])]);
            }
            if (stage == 2)
            {
                return new ChatResponse(
                    [new ChatMessage(ChatRole.Assistant, "policy observed")]);
            }
            throw new InvalidOperationException($"Unexpected request stage {stage} for {mode}.");
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
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
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(EchoCollapseAction), "echo")]
public abstract record FunctionCollapseAction;

[AIFunctionAction("echo")]
public sealed record EchoCollapseAction(string Value) : FunctionCollapseAction;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FunctionCollapseAction))]
internal partial class FunctionCollapseActionJsonContext : JsonSerializerContext;

public sealed class FunctionCollapseTestMetadata : IToolMetadata
{
    public bool EnableConditional { get; init; }

    public T? GetProperty<T>(string propertyName, T? defaultValue = default)
        => propertyName == nameof(EnableConditional) && EnableConditional is T value
            ? value
            : defaultValue;

    public bool HasProperty(string propertyName) =>
        propertyName == nameof(EnableConditional);

    public IEnumerable<string> GetPropertyNames() =>
        [nameof(EnableConditional)];
}

[Collapse("Same-name test harness")]
public partial class SameNameCollapseHarness
{
    [AIFunction]
    [CollapseFunction("Expand to use Echo.")]
    public string Echo() => "echo";
}

public sealed record CollapseDataInput(string Value, int Count);

[Collapse(
    "Function collapsing test harness",
    FunctionResult = "harness expanded",
    Middlewares = [typeof(FunctionCollapseProbeMiddleware)])]
public partial class FunctionCollapseTestHarness
{
    private static int _dataCalls;

    public static int DataCalls => Volatile.Read(ref _dataCalls);

    public static void ResetDataCalls() => Interlocked.Exchange(ref _dataCalls, 0);

    [AIFunction]
    public string Visible() => "visible";

    [AIFunction(Name = "CollapsedEcho")]
    [CollapseFunction(
        "CollapsedEchoTools",
        "Expand to use the echo action.",
        FunctionResult = "collapsed echo expanded")]
    public string Echo(FunctionCollapseAction operation) =>
        operation is EchoCollapseAction echo ? echo.Value : "unknown";

    [AIFunction(Name = "CollapsedData")]
    [CollapseFunction(
        "CollapsedDataTools",
        "Expand to use the data function.")]
    public string Data(CollapseDataInput input) =>
        DataValue(input);

    [AIFunction<FunctionCollapseTestMetadata>]
    [ConditionalFunction(nameof(FunctionCollapseTestMetadata.EnableConditional))]
    [CollapseFunction(
        "CollapsedConditionalTools",
        "Expand to use the conditional function.")]
    public string Conditional() => "conditional";

    private static string DataValue(CollapseDataInput input)
    {
        Interlocked.Increment(ref _dataCalls);
        return $"{input.Value}:{input.Count}";
    }
}

public sealed class FunctionCollapseProbeMiddleware : IToolHarnessMiddleware, IToolHarnessMiddlewareLifecycle
{
    private static int _activationCalls;
    private static int _wrapCalls;
    private static int _beforeCalls;
    private static readonly ConcurrentBag<string> _seenNames = [];

    public static int ActivationCalls => Volatile.Read(ref _activationCalls);
    public static int WrapCalls => Volatile.Read(ref _wrapCalls);
    public static int BeforeCalls => Volatile.Read(ref _beforeCalls);
    public static IReadOnlyCollection<string> SeenNames => _seenNames;

    public static void Reset()
    {
        Interlocked.Exchange(ref _activationCalls, 0);
        Interlocked.Exchange(ref _wrapCalls, 0);
        Interlocked.Exchange(ref _beforeCalls, 0);
        while (_seenNames.TryTake(out _)) { }
    }

    public ValueTask OnHarnessActivatedAsync(
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activationCalls);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnHarnessDeactivatingAsync(
        ToolHarnessDeactivationContext context,
        CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _beforeCalls);
        if (context.Function?.Name is { } functionName)
            _seenNames.Add(functionName);
        return Task.CompletedTask;
    }

    public async Task<object?> WrapFunctionCallAsync(
        FunctionRequest request,
        Func<FunctionRequest, Task<object?>> handler,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _wrapCalls);
        _seenNames.Add(request.FunctionName);
        return await handler(request).ConfigureAwait(false);
    }
}
