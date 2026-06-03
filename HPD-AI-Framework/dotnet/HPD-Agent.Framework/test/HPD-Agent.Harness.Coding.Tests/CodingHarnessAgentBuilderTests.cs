using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Middleware;
using HPD.Agent.Providers;
using HPD.Events.Core;
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
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig
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
            "ExecuteCommand"
        ]);
    }

    [Fact]
    public void CodingToolHarnessPrompt_IncludesExecuteCommandGuidance()
    {
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Use ExecuteCommand for builds, tests, project scripts");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Prefer the workingDirectory argument over cd");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Use runInBackground for long-running servers or watchers.");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("Use ListBackground if you need to recover ids");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("ReadOutput delayMilliseconds");
        CodingToolHarnessPrompts.SystemPrompt.Should().Contain("content_list/content_read under /artifacts");
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
                        Clients = new AgentClientConfig { Chat = new ClientProviderConfig
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

            listResult.Should().BeOfType<string>()
                .Which.Should().Contain("<directory")
                .And.NotContain("Offset must be greater than or equal to 1.")
                .And.NotContain("Limit must be between 1 and 1000.");

            globResult.Should().BeOfType<string>()
                .Which.Should().Contain("<glob")
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

        return await hpdFunction.InvokeAsync(arguments, CreateFunctionContext(tool));
    }

    private static FunctionExecutionContext CreateFunctionContext(AIFunction function)
    {
        var state = AgentLoopState.InitialSafe([], "run-1", "conversation-1", "AgentA");
        var session = new Session("session-1");
        var branch = new Branch("session-1") { Id = "branch-1" };
        var eventCoordinator = new EventCoordinator();
        var agentContext = new AgentContext(
            "AgentA",
            "conversation-1",
            state,
            eventCoordinator,
            session,
            branch,
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

        public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null) => chatClient;

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

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family) => ProviderValidationResult.Success();
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
}
