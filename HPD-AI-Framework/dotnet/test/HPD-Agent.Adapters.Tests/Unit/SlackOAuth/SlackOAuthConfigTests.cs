using FluentAssertions;
using HPD.Agent.Adapters.Slack.OAuth;

namespace HPD.Agent.Adapters.Tests.Unit.SlackOAuth;

public class SlackOAuthConfigTests
{
    [Fact]
    public void ClientId_Setter_TrimsWhitespace()
    {
        var cfg = new SlackOAuthConfig { ClientId = "  my-client-id  " };
        cfg.ClientId.Should().Be("my-client-id");
    }

    [Fact]
    public void ClientSecret_Setter_TrimsWhitespace()
    {
        var cfg = new SlackOAuthConfig { ClientSecret = "  my-secret  " };
        cfg.ClientSecret.Should().Be("my-secret");
    }

    [Fact]
    public void RedirectUri_Setter_TrimsWhitespace()
    {
        var cfg = new SlackOAuthConfig { RedirectUri = "  https://example.com/callback  " };
        cfg.RedirectUri.Should().Be("https://example.com/callback");
    }

    [Fact]
    public void ClientId_Setter_NullThrows()
    {
        var act = () => new SlackOAuthConfig { ClientId = null! };
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Scopes_DefaultsToMinimumSet()
    {
        var cfg = new SlackOAuthConfig();
        cfg.Scopes.Should().Contain("chat:write")
            .And.Contain("channels:history")
            .And.Contain("app_mentions:read");
    }
}
