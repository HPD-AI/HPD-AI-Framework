// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Evaluations.Tests.Integration;

public sealed class AgentEvaluationConvenienceExtensionsTests
{
    [Fact]
    public async Task EvaluateAsync_Prompts_BuildsStringDatasetAndRunsEvaluators()
    {
        var agent = CreateAgent();

        var report = await agent.EvaluateAsync(
            ["alpha", "beta"],
            [Eval.Contains("response to")],
            experimentName: "quick");

        report.ExperimentName.Should().Be("quick");
        report.Cases.Should().HaveCount(2);
        report.Cases.Select(c => c.Name).Should().BeEquivalentTo(["case-1", "case-2"]);
        report.PassRate("Output Contains").Should().Be(1.0);
    }

    [Fact]
    public async Task EvaluateAsync_GroundTruthCases_StoresGroundTruthOnRunConfig()
    {
        var agent = CreateAgent();
        var store = new InMemoryScoreStore();

        var report = await agent.EvaluateAsync(
            [("alpha", "expected alpha")],
            [Eval.Contains("response")],
            new RunEvalsOptions<string>
            {
                PersistResults = true,
                ScoreStore = store,
            },
            experimentName: "grounded");

        report.Cases.Should().ContainSingle()
            .Which.EvaluationResult.Metrics.Should().ContainKey("Output Contains");

        var records = new List<ScoreRecord>();
        await foreach (var record in store.GetScoresAsync(sessionId: "grounded"))
            records.Add(record);

        records.Should().ContainSingle();
        records[0].SessionId.Should().Be("grounded");
    }

    [Fact]
    public async Task CheckAsync_RunsSinglePrompt()
    {
        var agent = CreateAgent();

        var report = await agent.CheckAsync(
            "alpha",
            Eval.Contains("response to alpha"));

        report.Cases.Should().ContainSingle()
            .Which.Name.Should().Be("case-1");
        report.PassRate("Output Contains").Should().Be(1.0);
    }

    [Fact]
    public void EvalFactories_ReturnExistingDeterministicEvaluators()
    {
        Eval.Contains("x").Should().BeOfType<OutputContainsEvaluator>();
        Eval.ContainsAny("x", "y").Should().BeOfType<ContainsAnyEvaluator>();
        Eval.ContainsAll("x", "y").Should().BeOfType<ContainsAllEvaluator>();
        Eval.ContainsIgnoringCase("x").Should().BeOfType<CaseInsensitiveContainsEvaluator>();
        Eval.OutputEquals("x").Should().BeOfType<OutputEqualsEvaluator>();
        Eval.MatchesRegex("x").Should().BeOfType<OutputMatchesRegexEvaluator>();
        Eval.StartsWith("x").Should().BeOfType<StartsWithEvaluator>();
        Eval.WordCount(min: 1).Should().BeOfType<WordCountEvaluator>();
        Eval.ToolCalled("search").Should().BeOfType<ToolWasCalledEvaluator>();
        Eval.ToolCallCount("search", 1).Should().BeOfType<ToolCallCountEvaluator>();
        Eval.ToolArgumentMatches("search", "query", "x").Should().BeOfType<ToolArgumentMatchesEvaluator>();
        Eval.ToolResultContains("search", "x").Should().BeOfType<ToolResultContainsEvaluator>();
        Eval.NoToolsCalled().Should().BeOfType<NoToolsCalledEvaluator>();
        Eval.ToolCallOrder("a", "b").Should().BeOfType<ToolCallOrderEvaluator>();
        Eval.ToolCallF1("a", "b").Should().BeOfType<ToolCallF1Evaluator>();
    }

    private static HPD.Agent.Agent CreateAgent()
    {
        var chatClient = new EchoChatClient();
        var options = new ChatOptions();

        return new HPD.Agent.Agent(
            new AgentConfig
            {
                Name = "EvalConvenienceAgent",
                Clients = new AgentClientsConfig
                {
                    Chat = new ChatClientConfig
                    {
                        ProviderKey = "test",
                        ModelName = "gpt-test",
                    },
                },
            },
            chatClient,
            options,
            providerRegistry: new EchoProviderRegistry(chatClient));
    }

    private sealed class EchoChatClient : IChatClient
    {
        public ChatClientMetadata Metadata => new("EchoChatClient");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateResponse(messages));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent(ResponseText(messages))],
                ModelId = "provider-reported-gpt-test",
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }

        private static ChatResponse CreateResponse(IEnumerable<ChatMessage> messages)
            => new([new ChatMessage(ChatRole.Assistant, ResponseText(messages))]);

        private static string ResponseText(IEnumerable<ChatMessage> messages)
            => $"response to {messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text}";
    }

    private sealed class EchoProviderRegistry(IChatClient client) : IProviderRegistry
    {
        public IProvider? GetProvider(string providerKey) => new EchoProvider(providerKey, client);

        public TProvider? GetProvider<TProvider>(string providerKey)
            where TProvider : class, IProvider
            => GetProvider(providerKey) as TProvider;

        public IReadOnlyCollection<string> GetRegisteredProviders() => ["test"];

        public void Register(IProvider provider) { }

        public bool IsRegistered(string providerKey) => true;

        public void Clear() { }
    }

    private sealed class EchoProvider(string providerKey, IChatClient client) : IChatClientProvider
    {
        public string ProviderKey => providerKey;

        public string DisplayName => providerKey;

        public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default) => client;

        public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler() => new StubErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = providerKey,
            DisplayName = providerKey,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                    },
                },
            },
        };

        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }
}
