using HPD.Agent.Providers;
using HPD.Agent.Providers.OpenAI;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

public sealed class DeferredClientSelectionTests
{
    [Fact]
    public async Task OpenAIMissingModel_ReturnsStructuredProviderIssue()
    {
        var builder = new AgentBuilder()
            .WithOpenAI("placeholder")
            .WithEventComposition(TestEventApplication.Composition);
        builder.Config.Clients.Chat!.ModelName = null;
        await using var agent = await builder.BuildAsync();
        var events = new List<MessageTurnErrorEvent>();
        using var subscription = agent.Subscribe<MessageTurnErrorEvent>(events.Add);

        var exception = await Assert.ThrowsAsync<ClientSetupException>(() => agent.RunAsync("hello"));

        Assert.Equal("provider.model_required", exception.Diagnostic.Code);
        Assert.Equal("openai", exception.Diagnostic.ProviderKey);
        Assert.Equal("platform", exception.Diagnostic.BackendKey);
        Assert.Equal(ProviderClientFamily.Chat, exception.Diagnostic.Family);
        Assert.Equal("modelName", exception.Diagnostic.Path);
        Assert.Equal(exception.Diagnostic, Assert.Single(events).ClientSetup);
        Assert.Single(exception.Diagnostic.Issues);
    }

    [Fact]
    public async Task MissingCredential_ReturnsSafeProviderGuidanceToCallerAndEvent()
    {
        var builder = new AgentBuilder()
            .WithOpenAI(
                "test-model",
                authentication: new ApiKeyProviderAuthentication { SecretKey = "missing:ApiKey" })
            .WithEventComposition(TestEventApplication.Composition);
        await using var agent = await builder.BuildAsync();
        var events = new List<MessageTurnErrorEvent>();
        using var subscription = agent.Subscribe<MessageTurnErrorEvent>(events.Add);

        var exception = await Assert.ThrowsAsync<ProviderAuthenticationException>(() => agent.RunAsync("hello"));

        Assert.Equal("SecretNotFound", exception.Diagnostic.Code);
        Assert.Equal("openai", exception.Diagnostic.ProviderKey);
        Assert.Equal(ProviderClientFamily.Chat, exception.Diagnostic.Family);
        Assert.Equal("Configure the selected secret in the host's secret resolver and try again.",
            exception.Diagnostic.Guidance);
        Assert.DoesNotContain("missing:ApiKey", exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(exception.Diagnostic, Assert.Single(events).ClientSetup);
    }

    [Fact]
    public async Task MissingChatProvider_ExposesSameDiagnosticToCallerAndTurnEvent()
    {
        await using var agent = await new AgentBuilder()
            .WithEventComposition(TestEventApplication.Composition)
            .BuildAsync();
        var events = new List<MessageTurnErrorEvent>();
        using var subscription = agent.Subscribe<MessageTurnErrorEvent>(events.Add);

        var exception = await Assert.ThrowsAsync<ClientSetupException>(() => agent.RunAsync("hello"));

        Assert.Equal("ProviderDefaultRequired", exception.Diagnostic.Code);
        Assert.Equal(ProviderClientFamily.Chat, exception.Diagnostic.Family);
        var error = Assert.Single(events);
        Assert.Equal(exception.Diagnostic, error.ClientSetup);
        Assert.Equal("1.0", error.ClientSetup.SchemaVersion);
        var json = TestEventApplication.Codec.Serialize(error);
        var replayed = Assert.IsType<MessageTurnErrorEvent>(TestEventApplication.Codec.DeserializeEvent(json));
        Assert.NotNull(replayed.ClientSetup);
        Assert.Equal(exception.Diagnostic.Code, replayed.ClientSetup.Code);
        Assert.Equal(exception.Diagnostic.Family, replayed.ClientSetup.Family);
    }

    [Fact]
    public async Task UnknownProvider_ReturnsStableRegistrationDiagnostic()
    {
        var builder = new AgentBuilder(new AgentConfig
        {
            Name = "unknown-provider",
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Provider = new ProviderReference { Key = "not-installed" },
                    ModelName = "test-model"
                }
            }
        }).WithEventComposition(TestEventApplication.Composition);
        await using var agent = await builder.BuildAsync();

        var exception = await Assert.ThrowsAsync<ClientSetupException>(() => agent.RunAsync("hello"));

        Assert.Equal("provider.not_registered", exception.Diagnostic.Code);
        Assert.Equal("not-installed", exception.Diagnostic.ProviderKey);
        Assert.Equal("Install and register the selected provider package, or choose a registered provider.",
            exception.Diagnostic.Guidance);
    }

    [Fact]
    public async Task AgentWithoutDefaultClient_CanUseClientSelectedForRun()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("selected at run time");
        await using var agent = await new AgentBuilder()
            .WithEventComposition(TestEventApplication.Composition)
            .BuildAsync();

        var result = await agent.RunAsync("hello", runConfig: new AgentRunConfig
        {
            Clients = new AgentClientsConfig
            {
                Chat = new ChatClientConfig
                {
                    Override = ClientOverride<IChatClient>.Borrow(client, "test", "local", "test.chat")
                }
            }
        });

        Assert.Equal("selected at run time", result.Text);
        Assert.Single(client.CapturedRequests);
    }
}
