using FluentAssertions;
using HPD.Agent.Adapters.Slack.OAuth;
using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Adapters.Tests.Unit.SlackOAuth;

public class SlackOAuthRegistrationTests
{
    private static ServiceProvider BuildProvider(
        Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        extra?.Invoke(services);
        services.AddSlackOAuth(c =>
        {
            c.ClientId     = "test-client-id";
            c.ClientSecret = "test-client-secret";
            c.RedirectUri  = "https://example.com/callback";
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSlackOAuth_RegistersISlackTokenStore()
    {
        using var sp = BuildProvider();
        sp.GetService<ISlackTokenStore>().Should().NotBeNull();
    }

    [Fact]
    public void AddSlackOAuth_DefaultStore_IsInMemory()
    {
        using var sp = BuildProvider();
        sp.GetRequiredService<ISlackTokenStore>().Should().BeOfType<InMemorySlackTokenStore>();
    }

    [Fact]
    public void AddSlackOAuth_UserSuppliedStore_NotOverwritten()
    {
        var custom = new InMemorySlackTokenStore();
        using var sp = BuildProvider(services =>
            services.AddSingleton<ISlackTokenStore>(custom));

        sp.GetRequiredService<ISlackTokenStore>().Should().BeSameAs(custom);
    }

    [Fact]
    public void AddSlackOAuth_RegistersISecretResolver()
    {
        using var sp = BuildProvider();
        sp.GetService<ISecretResolver>().Should().NotBeNull();
    }

    [Fact]
    public void AddSlackOAuth_SecretResolver_IsTokenStoreResolver()
    {
        using var sp = BuildProvider();
        // The last registered ISecretResolver is the TokenStoreSecretResolver wrapper
        sp.GetServices<ISecretResolver>()
          .Should().ContainItemsAssignableTo<TokenStoreSecretResolver>();
    }

    [Fact]
    public void AddSlackOAuth_ConfigureCallback_AppliedToOptions()
    {
        using var sp = BuildProvider();
        var opts = sp.GetRequiredService<IOptions<SlackOAuthConfig>>().Value;
        opts.ClientId.Should().Be("test-client-id");
        opts.ClientSecret.Should().Be("test-client-secret");
        opts.RedirectUri.Should().Be("https://example.com/callback");
    }

    [Fact]
    public void AddSlackOAuth_NullServices_Throws()
    {
        var act = () => SlackOAuthServiceCollectionExtensions
            .AddSlackOAuth(null!, c => c.ClientId = "x");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSlackOAuth_NullConfigure_Throws()
    {
        var act = () => new ServiceCollection().AddSlackOAuth(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSlackOAuth_ReturnsServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        var returned = services.AddSlackOAuth(c =>
        {
            c.ClientId     = "id";
            c.ClientSecret = "secret";
            c.RedirectUri  = "https://x.com";
        });
        returned.Should().BeSameAs(services);
    }
}
