using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Events.Core;
using HPDOS.ToolHarnesses.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public class CodingToolHarnessAgentBuilderTests
{
    [Fact]
    public async Task AgentBuilder_CanCreateAgentWithCodingToolHarness()
    {
        using var chatClient = new TestChatClient();
        var config = new AgentConfig
        {
            Clients = new AgentClientsConfig { Chat = new ProviderClientConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            } }
        };

        var agent = await new AgentBuilder(config, new TestProviderRegistry(chatClient))
            .WithName("coding-toolharness-test-agent")
            .WithToolHarness<CodingToolHarness>()
            .BuildAsync();

        var toolNames = agent.DefaultOptions?.Tools?
            .OfType<AIFunction>()
            .Select(tool => tool.Name)
            .ToArray();

        toolNames.Should().NotBeNull();
        toolNames.Should().Contain([
            "ReadFile",
            "ListDirectory",
            "GlobSearch",
            "Grep",
            "ExecuteCommand",
            "explore",
            "worker",
            "reviewer"
        ]);
    }

    [Fact]
    public void CodingSubAgents_UseFocusedToolProfilesWithoutRecursiveDelegation()
    {
        var harness = new CodingToolHarness();

        var explorer = harness.Explore();
        var worker = harness.Worker();
        var reviewer = harness.Reviewer();

        explorer.InvocationModePolicy.Should().Be(AgentInvocationModePolicy.ModelChoice);
        worker.InvocationModePolicy.Should().Be(AgentInvocationModePolicy.ModelChoice);
        reviewer.InvocationModePolicy.Should().Be(AgentInvocationModePolicy.ModelChoice);

        explorer.ContextPolicy.Should().Be(SubAgentContextPolicy.ModelChoice);
        worker.ContextPolicy.Should().Be(SubAgentContextPolicy.ModelChoice);
        reviewer.ContextPolicy.Should().Be(SubAgentContextPolicy.ModelChoice);

        explorer.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
        worker.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);
        reviewer.RunConfig.InheritedFields.Should().Be(SubAgentRunConfigFields.Default);

        GetCodingFunctions(explorer).Should().BeEquivalentTo([
            "ReadFile", "ListDirectory", "GlobSearch", "Grep"
        ]);
        GetCodingFunctions(reviewer).Should().BeEquivalentTo([
            "ReadFile", "ListDirectory", "GlobSearch", "Grep"
        ]);
        GetCodingFunctions(worker).Should().BeEquivalentTo([
            "ReadFile", "ListDirectory", "GlobSearch", "Grep",
            "EditFile", "WriteFile", "ExecuteCommand"
        ]);

        GetCodingFunctions(explorer).Should().NotContain(["explore", "worker", "reviewer"]);
        GetCodingFunctions(worker).Should().NotContain(["explore", "worker", "reviewer"]);
        GetCodingFunctions(reviewer).Should().NotContain(["explore", "worker", "reviewer"]);
    }

    [Fact]
    public async Task CodingSubAgentToolProfiles_PreserveCodingHarnessScopedMiddleware()
    {
        using var chatClient = new TestChatClient();
        var subAgent = new CodingToolHarness().Explore();
        var config = GetConfig(subAgent);
        config.Clients = new AgentClientsConfig
        {
            Chat = new ProviderClientConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        };

        var agent = await new AgentBuilder(config, new TestProviderRegistry(chatClient))
            .BuildAsync();

        var toolNames = agent.DefaultOptions?.Tools?
            .OfType<AIFunction>()
            .Select(tool => tool.Name)
            .ToArray();

        // DefaultOptions is the complete immutable catalog; turn middleware projects its
        // model-visible subset. The generated activation remains present so scoped middleware
        // and graph relationships can be resolved without reflection.
        toolNames.Should().BeEquivalentTo([
            nameof(CodingToolHarness), "ReadFile", "ListDirectory", "GlobSearch", "Grep"
        ]);
        agent.Middlewares.Should().ContainSingle(middleware => middleware is ContainerMiddleware);

        var collapse = typeof(CodingToolHarness)
            .GetCustomAttributes(typeof(CollapseAttribute), inherit: false)
            .OfType<CollapseAttribute>()
            .Should().ContainSingle().Subject;

        collapse.Middlewares.Should().BeEquivalentTo([
            typeof(EnvironmentContextMiddleware),
            typeof(CodingLanguageServerMiddleware),
            typeof(ExecuteCommandPermissionMiddleware),
            typeof(DebugPermissionMiddleware)
        ]);
    }

    [Fact]
    public async Task AutomaticCollapsing_RootRun_AdvertisesCodingHarnessContainerByDefault()
    {
        using var chatClient = new RecordingTestChatClient();
        var agent = await new AgentBuilder(CreateTestConfig(), new TestProviderRegistry(chatClient))
            .WithName("automatic-collapsing-root")
            .WithToolHarness<CodingToolHarness>()
            .BuildAsync();

        await agent.RunAsync("What tools do you see?");

        chatClient.ToolNamesByRequest.Should().ContainSingle();
        chatClient.ToolNamesByRequest[0].Should().Contain(nameof(CodingToolHarness));
        chatClient.ToolNamesByRequest[0].Should().NotContain([
            "ReadFile", "ListDirectory", "GlobSearch", "Grep", "ExecuteCommand"
        ]);
    }

    [Fact]
    public async Task AutomaticCollapsing_DepthOneReviewer_ExpandsOnlyItsReadOnlyAllowlist()
    {
        using var chatClient = new RecordingTestChatClient(expandContainerOnFirstRequest: true);
        var reviewerConfig = GetConfig(new CodingToolHarness().Reviewer());
        reviewerConfig.Clients = CreateTestConfig().Clients;

        var reviewer = await new AgentBuilder(reviewerConfig, new TestProviderRegistry(chatClient))
            .BuildAsync();

        await reviewer.RunAsync("Review this workspace.");

        chatClient.ToolNamesByRequest.Should().HaveCount(2);
        chatClient.ToolNamesByRequest[0].Should().BeEquivalentTo([nameof(CodingToolHarness)]);
        chatClient.ToolNamesByRequest[1].Should().BeEquivalentTo([
            "ReadFile", "ListDirectory", "GlobSearch", "Grep"
        ]);
        chatClient.ToolNamesByRequest[1].Should().NotContain([
            "EditFile", "WriteFile", "ExecuteCommand", "explore", "worker", "reviewer"
        ]);
    }

    [Fact]
    public void CodingToolHarnessPrompt_IncludesExecuteCommandGuidance()
    {
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Use ExecuteCommand for builds, tests, project scripts");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("one closed request shape");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("\"action\":\"readOutput\"");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Prefer the workingDirectory argument over cd");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Use executionMode: \"Background\" on the run request for long-running servers or watchers.");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Use ListBackground if you need to recover ids");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("background Run result means the process launched");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("the readOutput request branch");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("delayMilliseconds instead of running a separate sleep command");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("content IDs returned by ExecuteCommand");
    }

    [Fact]
    public async Task GeneratedToolHarnessTools_ApplyDeclaredOptionalParameterDefaults()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"hpd-coding-defaults-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        await File.WriteAllTextAsync(Path.Combine(tempRoot, "sample.txt"), "hello");

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tempRoot);

            using var chatClient = new TestChatClient();
            var agent = await new AgentBuilder(
                    new AgentConfig
                    {
                        Clients = new AgentClientsConfig { Chat = new ProviderClientConfig
                        {
                            ProviderKey = "test",
                            ModelName = "test-model"
                        } }
                    },
                    new TestProviderRegistry(chatClient))
                .WithName("coding-toolharness-test-agent")
                .WithToolHarness<CodingToolHarness>()
                .BuildAsync();

            var tools = agent.DefaultOptions?.Tools?.OfType<AIFunction>().ToDictionary(tool => tool.Name)
                ?? throw new InvalidOperationException("No coding toolharness tools were registered.");

            var listResult = await InvokeToolAsync(tools["ListDirectory"], new AIFunctionArguments
            {
                ["path"] = tempRoot
            });

            var globResult = await InvokeToolAsync(tools["GlobSearch"], new AIFunctionArguments
            {
                ["pattern"] = "*.txt"
            });

            var grepResult = await InvokeToolAsync(tools["Grep"], new AIFunctionArguments
            {
                ["pattern"] = "hello",
                ["path"] = tempRoot,
                ["outputMode"] = "Content",
                ["fixedStrings"] = true
            });

            GetStringResult(listResult).Should().Contain("<directory")
                .And.NotContain("Offset must be greater than or equal to 1.")
                .And.NotContain("Limit must be between 1 and 1000.");

            GetStringResult(globResult).Should().Contain("<glob")
                .And.NotContain("Offset must be greater than or equal to 1.")
                .And.NotContain("Limit must be between 1 and 1000.")
                .And.NotContain("Path is required.");

            var grepText = grepResult switch
            {
                string value => value,
                JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() ?? string.Empty,
                JsonElement json => json.GetRawText(),
                null => string.Empty,
                _ => grepResult.ToString() ?? string.Empty
            };

            grepText.Should().NotContain("The JSON value could not be converted to GrepOutputMode")
                .And.NotContain("type_conversion_error");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async ValueTask<object?> InvokeToolAsync(AIFunction tool, AIFunctionArguments arguments)
    {
        if (tool is not HPDAIFunctionFactory.HPDAIFunction hpdFunction)
            return await tool.InvokeAsync(arguments);

        arguments.SetJson(JsonSerializer.SerializeToElement(
            arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
        return await hpdFunction.InvokeAsync(arguments, CreateFunctionContext(tool));
    }

    private static string GetStringResult(object? result)
        => result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString()!,
            JsonElement element => element.GetRawText(),
            _ => throw new InvalidOperationException($"Expected a string tool result, but received {result?.GetType().FullName ?? "null"}.")
        };

    private static IReadOnlyList<string> GetCodingFunctions(SubAgent subAgent)
    {
        var reference = GetConfig(subAgent).ToolHarnesses.Should().ContainSingle().Subject;
        reference.Name.Should().Be(nameof(CodingToolHarness));
        return reference.Functions!;
    }

    private static AgentConfig GetConfig(SubAgent subAgent) =>
        subAgent.Configuration.Should().BeOfType<SuppliedAgentConfiguration>().Subject.Config;

    private static AgentConfig CreateTestConfig() => new()
    {
        MaxAgenticIterations = 3,
        Clients = new AgentClientsConfig
        {
            Chat = new ProviderClientConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        }
    };

    private static FunctionExecutionContext CreateFunctionContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new Session("session-1");
        var thread = new Thread("session-1", "test-agent") { Id = "thread-1" };
        var eventCoordinator = new EventCoordinator();
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            eventCoordinator,
            session,
            thread,
            CancellationToken.None);
        var cwd = Directory.GetCurrentDirectory();
        var runConfig = new AgentRunConfig
        {
            ContextOverrides = new()
            {
                [AgentWorkspace.ContextKey] = new AgentWorkspace(
                    "default",
                    cwd,
                    [new AgentWorkspaceRoot("default", cwd)])
            }
        };
        var beforeContext = agentContext.AsBeforeFunction(
            function,
            "call-1",
            new Dictionary<string, object?>(),
            runConfig,
            toolharnessName: null,
            skillName: null,
            invocation: null);
        var request = new FunctionRequest
        {
            Function = function,
            CallId = "call-1",
            Arguments = new Dictionary<string, object?>(),
            State = state,
            RunConfig = runConfig,
            ResultMetadata = new ToolResultMetadata(),
            EventCoordinator = eventCoordinator
        };

        return new FunctionExecutionContext(beforeContext, request);
    }

    private sealed class TestProviderRegistry(IChatClient chatClient) : IProviderRegistry
    {
        public IProvider? GetProvider(string providerKey)
            => string.Equals(providerKey, "test", StringComparison.Ordinal)
                ? new TestChatClientProvider(chatClient)
                : null;

        public TProvider? GetProvider<TProvider>(string providerKey)
            where TProvider : class, IProvider
            => GetProvider(providerKey) as TProvider;

        public IReadOnlyCollection<string> GetRegisteredProviders() => ["test"];

        public void Register(IProvider provider)
        {
        }

        public bool IsRegistered(string providerKey) => string.Equals(providerKey, "test", StringComparison.Ordinal);

        public void Clear()
        {
        }
    }

    private sealed class TestChatClientProvider(IChatClient chatClient) : IChatClientProvider
    {
        public string ProviderKey => "test";

        public string DisplayName => "Test";

        public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default) => chatClient;

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata()
            => new()
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
                {
                    [ProviderClientFamily.Chat] = new()
                    {
                        Family = ProviderClientFamily.Chat,
                        Capabilities = new Dictionary<string, object?>
                        {
                            ["SupportsFunctionCalling"] = true,
                            ["SupportsStreaming"] = true
                        }
                    }
                }
            };

        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) => ProviderValidationResult.Success();
    }

    private sealed class TestChatClient : IChatClient
    {
        public ChatClientMetadata Metadata { get; } = new("test", defaultModelId: "test-model");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent("ok")],
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public TService? GetService<TService>(object? serviceKey = null)
            where TService : class => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingTestChatClient(bool expandContainerOnFirstRequest = false) : IChatClient
    {
        private int _requestCount;

        public ChatClientMetadata Metadata { get; } = new("test", defaultModelId: "test-model");

        public List<string[]> ToolNamesByRequest { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ToolNamesByRequest.Add(options?.Tools?.OfType<AIFunction>().Select(tool => tool.Name).ToArray() ?? []);
            await Task.Yield();

            if (expandContainerOnFirstRequest && _requestCount++ == 0)
            {
                yield return new ChatResponseUpdate
                {
                    Contents = [new FunctionCallContent("expand-coding-harness", nameof(CodingToolHarness))],
                    FinishReason = ChatFinishReason.ToolCalls
                };
                yield break;
            }

            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent("ok")],
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public TService? GetService<TService>(object? serviceKey = null)
            where TService : class => null;

        public void Dispose()
        {
        }
    }
}
