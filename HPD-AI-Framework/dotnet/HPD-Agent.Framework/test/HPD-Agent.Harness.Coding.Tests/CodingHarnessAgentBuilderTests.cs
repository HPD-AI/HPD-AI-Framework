using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Harness.Coding.Tests;

public class CodingHarnessAgentBuilderTests
{
    [Fact]
    public async Task AgentBuilder_CanCreateAgentWithCodingHarness()
    {
        using var chatClient = new TestChatClient();
        var config = new AgentConfig
        {
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model"
            }
        };

        var agent = await new AgentBuilder(config, new TestProviderRegistry(chatClient))
            .WithName("coding-harness-test-agent")
            .WithHarness<CodingHarness>()
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
            "Grep"
        ]);
    }

    [Fact]
    public async Task GeneratedHarnessTools_ApplyDeclaredOptionalParameterDefaults()
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
                        Provider = new ProviderConfig
                        {
                            ProviderKey = "test",
                            ModelName = "test-model"
                        }
                    },
                    new TestProviderRegistry(chatClient))
                .WithName("coding-harness-test-agent")
                .WithHarness<CodingHarness>()
                .BuildAsync();

            var tools = agent.DefaultOptions?.Tools?.OfType<AIFunction>().ToDictionary(tool => tool.Name)
                ?? throw new InvalidOperationException("No coding harness tools were registered.");

            var listResult = await tools["ListDirectory"].InvokeAsync(new AIFunctionArguments
            {
                ["path"] = tempRoot
            });

            var globResult = await tools["GlobSearch"].InvokeAsync(new AIFunctionArguments
            {
                ["pattern"] = "*.txt"
            });

            var grepResult = await tools["Grep"].InvokeAsync(new AIFunctionArguments
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

    private sealed class TestProviderRegistry(IChatClient chatClient) : IProviderRegistry
    {
        public IProviderFeatures? GetProvider(string providerKey)
            => string.Equals(providerKey, "test", StringComparison.Ordinal)
                ? new TestProviderFeatures(chatClient)
                : null;

        public IReadOnlyCollection<string> GetRegisteredProviders() => ["test"];

        public void Register(IProviderFeatures provider)
        {
        }

        public bool IsRegistered(string providerKey) => string.Equals(providerKey, "test", StringComparison.Ordinal);

        public void Clear()
        {
        }
    }

    private sealed class TestProviderFeatures(IChatClient chatClient) : IProviderFeatures
    {
        public string ProviderKey => "test";

        public string DisplayName => "Test";

        public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null) => chatClient;

        public IProviderErrorHandler CreateErrorHandler() => new GenericErrorHandler();

        public ProviderMetadata GetMetadata()
            => new()
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName,
                SupportsFunctionCalling = true,
                SupportsStreaming = true
            };

        public ProviderValidationResult ValidateConfiguration(ProviderConfig config) => ProviderValidationResult.Success();
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
