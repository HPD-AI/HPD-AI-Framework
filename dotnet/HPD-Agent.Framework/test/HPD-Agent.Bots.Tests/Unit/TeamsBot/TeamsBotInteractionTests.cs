using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Teams;
using HPD.Agent.Bots.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Agents.Builder.App;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using HpdAgent = HPD.Agent.Agent;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsBotInteractionTests
{
    [Fact]
    public async Task ProcessInvokeAsync_AdaptiveCardAction_RaisesCardAction()
    {
        var bot = CreateBot();
        TeamsCardActionEvent? received = null;
        bot.OnCardAction += evt => received = evt;

        await bot.ProcessInvokeAsync(new FakeTeamsTurn
        {
            ActivityName = "adaptiveCard/action",
            Values = new Dictionary<string, string>
            {
                ["action.data.actionId"] = "approve",
                ["action.data.value"] = "yes"
            }
        }, CancellationToken.None);

        received.Should().NotBeNull();
        received!.ActionId.Should().Be("approve");
        received.Values["action.data.value"].Should().Be("yes");
    }

    [Fact]
    public async Task ProcessInvokeAsync_TaskSubmit_RaisesModalSubmit()
    {
        var bot = CreateBot();
        TeamsModalSubmitEvent? received = null;
        bot.OnModalSubmit += evt => received = evt;

        await bot.ProcessInvokeAsync(new FakeTeamsTurn
        {
            ActivityName = "task/submit",
            Values = new Dictionary<string, string> { ["actionId"] = "deploy" }
        }, CancellationToken.None);

        received.Should().NotBeNull();
        received!.ActionId.Should().Be("deploy");
    }

    [Fact]
    public async Task ProcessActionAsync_MessageSubmit_RaisesCardAction()
    {
        var bot = CreateBot();
        TeamsCardActionEvent? received = null;
        bot.OnCardAction += evt => received = evt;

        await bot.ProcessActionAsync(new FakeTeamsTurn
        {
            Values = new Dictionary<string, string>
            {
                ["actionId"] = "save",
                ["value"] = "yes"
            }
        }, CancellationToken.None);

        received.Should().NotBeNull();
        received!.ActionId.Should().Be("save");
        received.Values["value"].Should().Be("yes");
    }

    [Fact]
    public async Task ProcessActionAsync_PermissionAction_DoesNotRaiseCardAction()
    {
        var agent = new HpdAgent(CreateAgentConfig(), new NoOpChatClient(), mergedOptions: null);
        var bot = CreateBot(new StaticAgentManager(agent));
        var cardActionRaised = false;
        bot.OnCardAction += _ => cardActionRaised = true;

        await bot.ProcessActionAsync(new FakeTeamsTurn
        {
            Values = new Dictionary<string, string>
            {
                ["actionId"] = "hpd.permission.approve",
                ["permissionId"] = "permission-1"
            }
        }, CancellationToken.None);

        cardActionRaised.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessReactionAsync_RaisesReaction()
    {
        var bot = CreateBot();
        TeamsReactionEvent? received = null;
        bot.OnReaction += evt => received = evt;

        await bot.ProcessReactionAsync(new FakeTeamsTurn
        {
            ReplyToId = "message-1",
            Values = new Dictionary<string, string> { ["reactionsAdded.0.type"] = "like" }
        }, added: true, CancellationToken.None);

        received.Should().NotBeNull();
        received!.MessageId.Should().Be("message-1");
        received.Reaction.Should().Be("like");
        received.Added.Should().BeTrue();
    }

    [Fact]
    public async Task ReactionMutationMethods_ThrowNotSupported()
    {
        var bot = CreateBot();

        await bot.Invoking(subject => subject.AddReactionAsync("thread", "message", "like"))
            .Should()
            .ThrowAsync<NotSupportedException>();
        await bot.Invoking(subject => subject.RemoveReactionAsync("thread", "message", "like"))
            .Should()
            .ThrowAsync<NotSupportedException>();
    }

    private static HPD.Agent.Bots.Teams.TeamsBot CreateBot()
        => CreateBot(new TestAgentManager(new InMemoryAgentStore()));

    private static HPD.Agent.Bots.Teams.TeamsBot CreateBot(AgentManager agentManager)
    {
        var sessionManager = new TestSessionManager(new InMemorySessionStore());
        var mapper = new PlatformSessionMapper(sessionManager, "teams-test-agent");
        var options = Options.Create(new TeamsBotConfig
        {
            AppId = "app-id",
            AppPassword = "secret",
            AgentId = "teams-test-agent"
        });

        return new HPD.Agent.Bots.Teams.TeamsBot(options, sessionManager, agentManager, mapper);
    }

    private static AgentConfig CreateAgentConfig()
        => new()
        {
            Name = "TeamsPermissionTestAgent",
            MaxAgenticIterations = 1,
            SystemInstructions = "You are a Teams permission test agent.",
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig {
                ProviderKey = "test",
                ModelName = "test-model",
            } },
            AgenticLoop = new AgenticLoopConfig
            {
                MaxTurnDuration = TimeSpan.FromMinutes(1),
            },
            ErrorHandling = new ErrorHandlingConfig
            {
                MaxRetries = 0,
                NormalizeErrors = true,
            },
            SessionStore = new InMemorySessionStore(),
        };

    private sealed class StaticAgentManager(HpdAgent agent) : AgentManager(new InMemoryAgentStore())
    {
        public override Task<HpdAgent> GetOrBuildAgentAsync(string agentId, CancellationToken ct = default)
            => Task.FromResult(agent);

        protected override Task<HpdAgent> BuildAgentAsync(string agentId, CancellationToken ct)
            => Task.FromResult(agent);

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(5);
    }

    private sealed class NoOpChatClient : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]));

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

        public Task QueueInformativeUpdateAsync(string text, CancellationToken ct) => Task.CompletedTask;

        public void QueueTextChunk(string text) { }

        public Task CompleteCardAsync(TeamsAdaptiveCard card, CancellationToken ct) => Task.CompletedTask;

        public Task SendCardAsync(TeamsAdaptiveCard card, CancellationToken ct) => Task.CompletedTask;

        public Task EndStreamAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
