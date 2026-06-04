using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Audio.V2.Tests.TestInfrastructure;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Events.Core;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Audio.V2.Tests;

public sealed class AudioFunctionCallingValidationTests
{
    [Fact]
    public async Task AgentIterations_MathToolHarness_ExecutesMultipleToolCallsAndPersistsToolHarnessEvents()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var chatClient = new ScriptedToolLoopChatClient();
        chatClient.EnqueueToolCall("Add", "call-add", new Dictionary<string, object?>
        {
            ["left"] = 2,
            ["right"] = 3
        });
        chatClient.EnqueueToolCall("Multiply", "call-multiply", new Dictionary<string, object?>
        {
            ["left"] = 5,
            ["right"] = 4
        });
        chatClient.EnqueueTextResponse("The answer is 20.");

        var agent = await new AgentBuilder(CreateConfig(repository), new TestProviderRegistry(chatClient))
            .WithName("audio-function-validation-agent")
            .WithToolHarness<MathToolHarness>()
            .BuildAsync();

        await agent.CreateSessionAsync("audio-function-session");

        await agent.RunAsync(
            "Use math tools to add 2 and 3, then multiply the result by 4.",
            "audio-function-session",
            "main");

        var document = await repository.LoadBranchDocumentAsync("audio-function-session", "main");
        Assert.NotNull(document);

        var starts = document.Events.OfType<ToolCallStartEvent>().ToArray();
        var args = document.Events.OfType<ToolCallArgsEvent>().ToArray();
        var results = document.Events.OfType<ToolCallResultEvent>().ToArray();
        var ends = document.Events.OfType<ToolCallEndEvent>().ToArray();

        Assert.Collection(
            starts,
            start =>
            {
                Assert.Equal("call-add", start.CallId);
                Assert.Equal("Add", start.Name);
                Assert.Equal(nameof(MathToolHarness), start.ToolHarnessName);
            },
            start =>
            {
                Assert.Equal("call-multiply", start.CallId);
                Assert.Equal("Multiply", start.Name);
                Assert.Equal(nameof(MathToolHarness), start.ToolHarnessName);
            });

        Assert.Collection(
            args,
            arg =>
            {
                Assert.Equal("call-add", arg.CallId);
                AssertJsonProperty(arg.ArgsJson, "left", 2);
                AssertJsonProperty(arg.ArgsJson, "right", 3);
            },
            arg =>
            {
                Assert.Equal("call-multiply", arg.CallId);
                AssertJsonProperty(arg.ArgsJson, "left", 5);
                AssertJsonProperty(arg.ArgsJson, "right", 4);
            });

        Assert.Collection(
            ends,
            end => Assert.Equal("call-add", end.CallId),
            end => Assert.Equal("call-multiply", end.CallId));

        Assert.Collection(
            results,
            result =>
            {
                Assert.Equal("call-add", result.CallId);
                Assert.Equal(nameof(MathToolHarness), result.ToolHarnessName);
                Assert.Contains("5", result.Result.Text ?? string.Empty);
            },
            result =>
            {
                Assert.Equal("call-multiply", result.CallId);
                Assert.Equal(nameof(MathToolHarness), result.ToolHarnessName);
                Assert.Contains("20", result.Result.Text ?? string.Empty);
            });

        var branch = await repository.LoadBranchAsync("audio-function-session", "main");
        Assert.NotNull(branch);
        var finalAssistantMessage = Assert.Single(branch.Messages, message =>
            message.Role == ChatRole.Assistant &&
            message.Text == "The answer is 20.");
        Assert.Equal("The answer is 20.", finalAssistantMessage.Text);
        Assert.Equal(3, chatClient.CapturedRequests.Count);
    }

    private static AgentConfig CreateConfig(ISessionRepository repository)
        => new()
        {
            Name = "AudioFunctionValidationAgent",
            MaxAgenticIterations = 10,
            SessionRepository = repository,
            Clients = new AgentClientConfig
            {
                Chat = new ClientProviderConfig
                {
                    ProviderKey = "test",
                    ModelName = "test-model"
                }
            },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromMinutes(1)
            },
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 0,
                NormalizeErrors = true
            }
        };

    private static void AssertJsonProperty(string json, string propertyName, int expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty(propertyName, out var property));
        Assert.Equal(expected, property.GetInt32());
    }

    private sealed class ScriptedToolLoopChatClient : IChatClient
    {
        private readonly Queue<QueuedResponse> _responses = new();
        private readonly List<IReadOnlyList<ChatMessage>> _capturedRequests = [];

        public ChatClientMetadata Metadata { get; } = new("test", defaultModelId: "test-model");

        public IReadOnlyList<IReadOnlyList<ChatMessage>> CapturedRequests => _capturedRequests;

        public void EnqueueToolCall(string functionName, string callId, Dictionary<string, object?> arguments)
            => _responses.Enqueue(new QueuedResponse(
                Text: null,
                ToolCall: new FunctionCallContent(callId, functionName, arguments)));

        public void EnqueueTextResponse(string text)
            => _responses.Enqueue(new QueuedResponse(text, ToolCall: null));

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _capturedRequests.Add(chatMessages.ToArray());
            var response = DequeueResponse();
            var message = response.ToolCall is not null
                ? new ChatMessage(ChatRole.Assistant, [response.ToolCall])
                : new ChatMessage(ChatRole.Assistant, response.Text ?? string.Empty);
            return Task.FromResult(new ChatResponse([message]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _capturedRequests.Add(chatMessages.ToArray());
            var response = DequeueResponse();

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            if (response.ToolCall is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Contents = [response.ToolCall],
                    FinishReason = ChatFinishReason.ToolCalls
                };
                yield break;
            }

            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent(response.Text ?? string.Empty)],
                FinishReason = ChatFinishReason.Stop
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private QueuedResponse DequeueResponse()
        {
            if (_responses.TryDequeue(out var response))
                return response;

            throw new InvalidOperationException("No scripted chat responses remain.");
        }

        private sealed record QueuedResponse(string? Text, FunctionCallContent? ToolCall);
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

        public string DisplayName => "Test Provider";

        public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
            => chatClient;

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
                            ["SupportsStreaming"] = true,
                            ["SupportsFunctionCalling"] = true
                        }
                    }
                }
            };

        public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }
}
