using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Teams;
using HPD.Agent.Bots.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Agents.Builder.App;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using HpdAgent = HPD.Agent.Agent;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsBotMessageTests
{
    [Fact]
    public async Task ProcessMessageAsync_EmptyMessage_ReturnsFalseWithoutStartingStream()
    {
        var (bot, _) = CreateBot();
        var turn = new FakeTeamsTurn { Text = "   " };

        var processed = await bot.ProcessMessageAsync(turn, CancellationToken.None);

        processed.Should().BeFalse();
        turn.InformativeUpdates.Should().BeEmpty();
        turn.EndStreamCalls.Should().Be(0);
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenStreamLockHeld_ReturnsFalseWithoutStartingStream()
    {
        var (bot, sessionManager) = CreateBot();
        var turn = new FakeTeamsTurn
        {
            Text = "hello",
            ConversationId = "19:abc@thread.tacv2",
            ServiceUrl = "https://smba.trafficmanager.net/amer/"
        };
        var platformKey = TeamsThreadId.FormatRaw(turn.ConversationId, turn.ServiceUrl);
        var (sessionId, branchId) = await bot.SessionMapper.ResolveAsync(platformKey);
        sessionManager.TryAcquireStreamLock(sessionId, branchId).Should().BeTrue();

        try
        {
            var processed = await bot.ProcessMessageAsync(turn, CancellationToken.None);

            processed.Should().BeFalse();
            turn.InformativeUpdates.Should().BeEmpty();
            turn.EndStreamCalls.Should().Be(0);
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sessionId, branchId);
        }
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenStreamLockHeld_CreatesOrFindsPlatformSession()
    {
        var (bot, sessionManager) = CreateBot();
        var turn = new FakeTeamsTurn
        {
            Text = "hello",
            ConversationId = "19:abc@thread.tacv2;messageid=123",
            ServiceUrl = "https://smba.trafficmanager.net/amer/"
        };
        var platformKey = TeamsThreadId.FormatRaw(turn.ConversationId, turn.ServiceUrl);
        var (sessionId, branchId) = await bot.SessionMapper.ResolveAsync(platformKey);
        sessionManager.TryAcquireStreamLock(sessionId, branchId).Should().BeTrue();

        try
        {
            await bot.ProcessMessageAsync(turn, CancellationToken.None);

            var session = await sessionManager.Store.LoadSessionAsync(sessionId);
            session!.Metadata["platformKey"].Should().Be(platformKey);
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sessionId, branchId);
        }
    }

    [Fact]
    public async Task ProcessMessageAsync_PersistsTeamsMetadata()
    {
        var (bot, sessionManager) = CreateBot();
        var turn = new FakeTeamsTurn
        {
            Text = "hello",
            ConversationId = "19:abc@thread.tacv2",
            ServiceUrl = "https://smba.trafficmanager.net/amer/",
            TenantId = "tenant-1",
            Values = new Dictionary<string, string>
            {
                ["channelData.team.id"] = "team-1",
                ["channelData.channel.id"] = "channel-1"
            }
        };
        var platformKey = TeamsThreadId.FormatRaw(turn.ConversationId, turn.ServiceUrl);
        var (sessionId, branchId) = await bot.SessionMapper.ResolveAsync(platformKey);
        sessionManager.TryAcquireStreamLock(sessionId, branchId).Should().BeTrue();

        try
        {
            await bot.ProcessMessageAsync(turn, CancellationToken.None);

            var session = await sessionManager.Store.LoadSessionAsync(sessionId);
            session!.Metadata["teams.serviceUrl"].Should().Be(turn.ServiceUrl);
            session.Metadata["teams.conversationId"].Should().Be(turn.ConversationId);
            session.Metadata["teams.tenantId"].Should().Be("tenant-1");
            session.Metadata.Should().NotContainKey("teams.conversationReference");
            session.Metadata["teams.channelContext"].Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["teamId"] = "team-1",
                ["channelId"] = "channel-1"
            });
        }
        finally
        {
            sessionManager.ReleaseStreamLock(sessionId, branchId);
        }
    }

    [Fact]
    public async Task ProcessMessageAsync_WithInputFiles_PassesAttachmentsToAgentMessage()
    {
        var sessionStore = new InMemorySessionStore();
        var sessionManager = new TestSessionManager(sessionStore);
        var chatClient = new CapturingChatClient("done");
        var agent = new HpdAgent(CreateAgentConfig(sessionStore), chatClient, mergedOptions: null);
        var bot = CreateBot(sessionManager, new StaticAgentManager(agent)).Bot;
        var fileBytes = new byte[] { 0x01, 0x02, 0x03 };
        var turn = new FakeTeamsTurn
        {
            Text = "describe this",
            InputFiles =
            [
                new InputFile(BinaryData.FromBytes(fileBytes), "image/png")
                {
                    Filename = "image.png"
                }
            ]
        };

        var processed = await bot.ProcessMessageAsync(turn, CancellationToken.None);

        processed.Should().BeTrue();
        turn.TextChunks.Should().ContainSingle().Which.Should().Be("done");
        var userMessage = chatClient.LastMessages.Should()
            .ContainSingle(message => message.Role == ChatRole.User)
            .Which;
        userMessage.Contents.OfType<TextContent>()
            .Should().ContainSingle().Which.Text.Should().Be("describe this");
        var data = userMessage.Contents.OfType<DataContent>()
            .Should().ContainSingle().Which;
        data.Name.Should().Be("image.png");
        data.MediaType.Should().Be("image/png");
        data.Data.ToArray().Should().Equal(fileBytes);
    }

    private static (HPD.Agent.Bots.Teams.TeamsBot Bot, SessionManager SessionManager) CreateBot()
    {
        var sessionManager = new TestSessionManager(new InMemorySessionStore());
        var agentManager = new TestAgentManager(new InMemoryAgentStore());
        return CreateBot(sessionManager, agentManager);
    }

    private static (HPD.Agent.Bots.Teams.TeamsBot Bot, SessionManager SessionManager) CreateBot(
        SessionManager sessionManager,
        AgentManager agentManager)
    {
        var mapper = new PlatformSessionMapper(sessionManager);
        var options = Options.Create(new TeamsBotConfig
        {
            AppId = "app-id",
            AppPassword = "secret",
            AgentId = "teams-test-agent"
        });

        return (new HPD.Agent.Bots.Teams.TeamsBot(options, sessionManager, agentManager, mapper), sessionManager);
    }

    private static AgentConfig CreateAgentConfig(ISessionStore sessionStore)
        => new()
        {
            Name = "TeamsTestAgent",
            MaxAgenticIterations = 1,
            SystemInstructions = "You are a Teams test agent.",
            Provider = new ProviderConfig
            {
                ProviderKey = "test",
                ModelName = "test-model",
            },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromMinutes(1),
            },
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 0,
                NormalizeErrors = true,
            },
            SessionStore = sessionStore,
            SessionStoreOptions = new SessionStoreOptions
            {
                PersistAfterTurn = true,
            },
        };

    private sealed class StaticAgentManager(HpdAgent agent) : AgentManager(new InMemoryAgentStore())
    {
        public override Task<HpdAgent> GetOrBuildAgentAsync(string agentId, CancellationToken ct = default)
            => Task.FromResult(agent);

        protected override Task<HpdAgent> BuildAgentAsync(string agentId, CancellationToken ct)
            => Task.FromResult(agent);

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(5);
    }

    private sealed class CapturingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToArray();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            yield return new ChatResponseUpdate
            {
                Contents = [new TextContent(responseText)],
            };

            yield return new ChatResponseUpdate
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToArray();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeTeamsTurn : ITeamsTurn
    {
        public string Text { get; init; } = "";

        public string ConversationId { get; init; } = "conversation";

        public string ServiceUrl { get; init; } = "https://service.example/";

        public string? ActivityName { get; init; }

        public string? ReplyToId { get; init; }

        public string? TenantId { get; init; }

        public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

        public IReadOnlyList<InputFile> InputFiles { get; init; } = [];

        public List<string> InformativeUpdates { get; } = [];

        public List<string> TextChunks { get; } = [];

        public List<TeamsAdaptiveCard> Cards { get; } = [];

        public int EndStreamCalls { get; private set; }

        public Task QueueInformativeUpdateAsync(string text, CancellationToken ct)
        {
            InformativeUpdates.Add(text);
            return Task.CompletedTask;
        }

        public void QueueTextChunk(string text) => TextChunks.Add(text);

        public Task CompleteCardAsync(TeamsAdaptiveCard card, CancellationToken ct)
        {
            Cards.Add(card);
            return Task.CompletedTask;
        }

        public Task SendCardAsync(TeamsAdaptiveCard card, CancellationToken ct)
        {
            Cards.Add(card);
            return Task.CompletedTask;
        }

        public Task EndStreamAsync(CancellationToken ct)
        {
            EndStreamCalls++;
            return Task.CompletedTask;
        }
    }
}
