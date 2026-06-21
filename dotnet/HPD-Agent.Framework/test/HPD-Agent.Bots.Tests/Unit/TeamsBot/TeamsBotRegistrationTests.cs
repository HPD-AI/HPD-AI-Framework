using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Bots.AspNetCore;
using HPD.Agent.Bots.Session;
using HPD.Agent.Bots.Streaming;
using HPD.Agent.Bots.Teams;
using HPD.Agent.Bots.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsBotRegistrationTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<SessionManager>(
            new TestSessionManager(new InMemorySessionStore()));
        services.AddSingleton<AgentManager>(
            new TestAgentManager(new InMemoryAgentStore()));

        extra?.Invoke(services);

        services.AddTeamsBot(config =>
        {
            config.AppId = "app-id";
            config.AppPassword = "secret";
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTeamsBot_RegistersTeamsBot()
    {
        using var sp = BuildProvider();

        sp.GetService<HPD.Agent.Bots.Teams.TeamsBot>().Should().NotBeNull();
    }

    [Fact]
    public void AddTeamsBot_RegistersPlatformSessionMapper()
    {
        using var sp = BuildProvider();

        sp.GetService<PlatformSessionMapper>().Should().NotBeNull();
    }

    [Fact]
    public void AddTeamsBot_RegistersMemoryStorageFallback()
    {
        using var sp = BuildProvider();

        sp.GetService<IStorage>().Should().BeOfType<MemoryStorage>();
    }

    [Fact]
    public void AddTeamsBot_RegistersTeamsCardRenderer()
    {
        using var sp = BuildProvider();

        sp.GetService<TeamsCardRenderer>().Should().NotBeNull();
    }

    [Fact]
    public void AddTeamsBot_RegistersTeamsFormatConverter()
    {
        using var sp = BuildProvider();

        sp.GetService<TeamsFormatConverter>().Should().NotBeNull();
    }

    [Fact]
    public void AddTeamsBot_RegistersTeamsModalConverter()
    {
        using var sp = BuildProvider();

        sp.GetService<TeamsModalConverter>().Should().NotBeNull();
    }

    [Fact]
    public void AddTeamsBot_RegistersNoopTeamsHistoryService()
    {
        using var sp = BuildProvider();

        sp.GetRequiredService<ITeamsHistoryService>().Should().BeOfType<NoopTeamsHistoryService>();
    }

    [Fact]
    public void AddTeamsBot_RegistersRegistryProvider()
    {
        using var sp = BuildProvider();

        var registration = sp.GetServices<IBotRegistryProvider>()
            .SelectMany(provider => provider.GetAll())
            .Should()
            .ContainSingle(item => item.Name == "teams")
            .Subject;

        registration.DefaultPath.Should().Be("/api/messages");
        registration.BotType.Should().Be(typeof(HPD.Agent.Bots.Teams.TeamsBot));
    }

    [Fact]
    public void AddTeamsBot_DoesNotReplaceExistingStorage()
    {
        var storage = new MemoryStorage();

        using var sp = BuildProvider(services => services.AddSingleton<IStorage>(storage));

        sp.GetRequiredService<IStorage>().Should().BeSameAs(storage);
    }

    [Fact]
    public void AddTeamsBot_ConfigApplied()
    {
        using var sp = BuildProvider();

        var options = sp.GetRequiredService<IOptions<TeamsBotConfig>>().Value;

        options.AppId.Should().Be("app-id");
        options.AppPassword.Should().Be("secret");
    }

    [Fact]
    public void AddTeamsBot_AgentIdConfigApplied()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SessionManager>(
            new TestSessionManager(new InMemorySessionStore()));
        services.AddSingleton<AgentManager>(
            new TestAgentManager(new InMemoryAgentStore()));

        services.AddTeamsBot(config =>
        {
            config.AppId = "app-id";
            config.AppPassword = "secret";
            config.AgentId = "support";
        });

        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IOptions<TeamsBotConfig>>().Value.AgentId.Should().Be("support");
    }

    [Fact]
    public void AddTeamsBot_RegistersNativeStreamingOptions()
    {
        using var sp = BuildProvider();

        var options = sp.GetRequiredService<IOptionsMonitor<BotStreamingOptions>>().Get("teams");

        options.Strategy.Should().Be(StreamingStrategy.Native);
        options.DebounceMs.Should().Be(0);
    }

    [Fact]
    public void AddTeamsM365AttachmentDownloaders_RegistersInputFileDownloaderList()
    {
        var services = new ServiceCollection();

        services.AddTeamsM365AttachmentDownloaders();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IList<IInputFileDownloader>));
    }

    [Fact]
    public void AddTeamsBot_NullServices_Throws()
    {
        var act = () => TeamsBotServiceCollectionExtensions
            .AddTeamsBot((IServiceCollection)null!, config => config.AppId = "app-id");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTeamsBot_NullConfigure_Throws()
    {
        var act = () => new ServiceCollection().AddTeamsBot(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
