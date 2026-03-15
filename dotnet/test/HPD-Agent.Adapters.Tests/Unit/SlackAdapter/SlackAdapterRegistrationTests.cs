using FluentAssertions;
using HPD.Agent.Adapters.Session;
using HPD.Agent.Adapters.Slack;
using HPD.Agent.Adapters.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HPD.Agent;

namespace HPD.Agent.Adapters.Tests.Unit.SlackAdapterRegistration;

/// <summary>
/// DI-integration tests for <see cref="SlackAdapterServiceCollectionExtensions.AddSlackAdapter(IServiceCollection, Action{SlackAdapterConfig}, bool)"/>.
/// Each test verifies that the specified service type is resolvable from the container
/// after calling <c>AddSlackAdapter(configure, registerDefaultSecretResolver: true)</c>.
/// </summary>
public class SlackAdapterRegistrationTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fully wired <see cref="ServiceProvider"/> for the Slack adapter.
    /// Satisfies all mandatory prerequisites:
    ///   • <see cref="IConfiguration"/>   — required by <see cref="ChainedSecretResolver"/>
    ///   • <see cref="IHttpClientFactory"/> — required by <see cref="SlackApiClient"/>
    ///   • <see cref="SessionManager"/> + <see cref="AgentManager"/> — required by <see cref="SlackAdapter"/> and <see cref="PlatformSessionMapper"/>
    /// </summary>
    private static ServiceProvider BuildProvider(
        Action<IServiceCollection>? extra = null,
        bool registerDefaultSecretResolver = true,
        Action<SlackAdapterConfig>? overrideConfig = null)
    {
        var services = new ServiceCollection();

        // IConfiguration: ChainedSecretResolver resolves ConfigurationSecretResolver from here.
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Slack:SigningSecret"] = "test-signing-secret",
                    ["Slack:BotToken"]      = "xoxb-test-token",
                })
                .Build());

        // IHttpClientFactory: SlackApiClient and SlackUserCache depend on named/typed HTTP clients.
        services.AddHttpClient();

        // SessionManager and AgentManager are abstract — register test doubles to satisfy DI.
        services.AddSingleton<SessionManager>(
            new TestSessionManager(new InMemorySessionStore()));
        services.AddSingleton<AgentManager>(
            new TestAgentManager(new InMemoryAgentStore()));

        // Allow individual tests to pre-register services before AddSlackAdapter runs.
        extra?.Invoke(services);

        services.AddSlackAdapter(
            overrideConfig ?? (c =>
            {
                c.SigningSecret = "test-signing-secret";
                c.BotToken      = "xoxb-test-token";
            }),
            registerDefaultSecretResolver);

        return services.BuildServiceProvider();
    }

    // ── 1. SlackAdapter ───────────────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_RegistersSlackAdapter()
    {
        using var sp = BuildProvider();
        sp.GetService<HPD.Agent.Adapters.Slack.SlackAdapter>().Should().NotBeNull();
    }

    // ── 2. PlatformSessionMapper ──────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_RegistersPlatformSessionMapper()
    {
        using var sp = BuildProvider();
        sp.GetService<PlatformSessionMapper>().Should().NotBeNull();
    }

    // ── 3. SlackApiClient ─────────────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_RegistersSlackApiClient()
    {
        using var sp = BuildProvider();
        sp.GetService<SlackApiClient>().Should().NotBeNull();
    }

    // ── 4. SlackFormatConverter ───────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_RegistersSlackFormatConverter()
    {
        using var sp = BuildProvider();
        sp.GetService<SlackFormatConverter>().Should().NotBeNull();
    }

    // ── 5. SlackUserCache ─────────────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_RegistersSlackUserCache()
    {
        using var sp = BuildProvider();
        sp.GetService<SlackUserCache>().Should().NotBeNull();
    }

    // ── 6. ISecretResolver ────────────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_RegistersISecretResolver()
    {
        using var sp = BuildProvider();
        sp.GetService<ISecretResolver>().Should().NotBeNull();
    }

    // ── 7. Config applied ─────────────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_ConfigApplied()
    {
        using var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptions<SlackAdapterConfig>>().Value;

        opts.SigningSecret.Should().Be("test-signing-secret");
        opts.BotToken.Should().Be("xoxb-test-token");
    }

    // ── 8. Null services guard ────────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_NullServices_Throws()
    {
        var act = () => SlackAdapterServiceCollectionExtensions
            .AddSlackAdapter(null!, c => c.SigningSecret = "x", registerDefaultSecretResolver: false);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── 9. Null configure guard ───────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_NullConfigure_Throws()
    {
        var act = () => new ServiceCollection()
            .AddSlackAdapter(null!, registerDefaultSecretResolver: false);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── AppToken: defaults to null ────────────────────────────────────────────

    [Fact]
    public void AddSlackAdapter_AppTokenDefaultsToNull()
    {
        using var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptions<SlackAdapterConfig>>().Value;

        opts.AppToken.Should().BeNull();
    }

    // ── Socket mode: AppToken present → SlackSocketModeService registered ─────

    [Fact]
    public void AddSlackAdapter_WithAppToken_RegistersSocketModeServiceAsHostedService()
    {
        using var sp = BuildProvider(extra: services =>
        {
            // Pre-register SessionManager and AgentManager before AddSlackAdapter
            // (BuildProvider already does this, but AddSlackAdapter is called inside it)
        }, overrideConfig: c =>
        {
            c.SigningSecret = "test-signing-secret";
            c.BotToken      = "xoxb-test-token";
            c.AppToken      = "xapp-test-app-token";
        });

        var hostedServices = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        hostedServices.Should().ContainItemsAssignableTo<HPD.Agent.Adapters.Slack.SocketMode.SlackSocketModeService>(
            "SlackSocketModeService must be registered as IHostedService when AppToken is set");
    }

    [Fact]
    public void AddSlackAdapter_WithoutAppToken_DoesNotRegisterSocketModeService()
    {
        using var sp = BuildProvider(); // no AppToken

        var hostedServices = sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        hostedServices.Should().NotContainItemsAssignableTo<HPD.Agent.Adapters.Slack.SocketMode.SlackSocketModeService>(
            "SlackSocketModeService must NOT be registered when AppToken is null");
    }

    [Fact]
    public void AddSlackAdapter_WithAppToken_RegistersSlackSocketModeClient()
    {
        using var sp = BuildProvider(overrideConfig: c =>
        {
            c.SigningSecret = "test-signing-secret";
            c.BotToken      = "xoxb-test-token";
            c.AppToken      = "xapp-test-app-token";
        });

        sp.GetService<HPD.Agent.Adapters.Slack.SocketMode.SlackSocketModeClient>()
            .Should().NotBeNull();
    }

    [Fact]
    public void AddSlackAdapter_WithAppToken_ConfigureInvokedOnce()
    {
        // configure is called once upfront (for the AppToken check) and once more
        // at options resolution time (by services.Configure<T>). The key constraint is
        // that it is NOT called twice at registration time.
        // We count calls at registration time only (before BuildServiceProvider).
        var registrationTimeCalls = 0;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpClient();
        services.AddSingleton<SessionManager>(new TestSessionManager(new InMemorySessionStore()));
        services.AddSingleton<AgentManager>(new TestAgentManager(new InMemoryAgentStore()));

        services.AddSlackAdapter(c =>
        {
            registrationTimeCalls++;
            c.SigningSecret = "s";
            c.BotToken      = "t";
            c.AppToken      = "xapp-x";
        });

        // configure is called exactly once during AddSlackAdapter (upfront capture)
        registrationTimeCalls.Should().Be(1,
            "configure must be called exactly once at registration time for the AppToken check");
    }
}