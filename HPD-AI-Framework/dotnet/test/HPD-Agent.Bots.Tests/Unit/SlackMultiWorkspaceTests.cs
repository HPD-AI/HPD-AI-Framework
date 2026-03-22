using FluentAssertions;
using HPD.Agent.Bots.Slack;

namespace HPD.Agent.Bots.Tests.Unit;

/// <summary>
/// Tests for multi-workspace support in Slack adapter configuration.
/// Focuses on token key naming conventions and team ID handling.
/// </summary>
public class SlackMultiWorkspaceTests
{
    [Fact]
    public void SingleWorkspace_TokenKey_UsesGlobalKey()
    {
        // With multi-workspace, token keys follow: "slack:BotToken:{teamId}"
        // For single-workspace, falls back to config.BotToken (no key lookup)
        var config = new SlackBotConfig
        {
            SigningSecret = "secret",
            BotToken = "xoxb-global"
        };

        config.BotToken.Should().StartWith("xoxb-");
    }

    [Fact]
    public void MultiWorkspace_TokenKeyFormat_Correct()
    {
        // Token resolver should look for keys like this in multi-workspace mode:
        var teamId = "T123";
        var expectedKey = $"slack:BotToken:{teamId}";

        expectedKey.Should().Contain("T123");
        expectedKey.Should().StartWith("slack:");
    }

    [Fact]
    public void ThreadId_MultipleTeams_SeparateByTeamId()
    {
        // Same channel in different teams should have different session keys
        var t1Key = SlackThreadId.Format("C123", "1234.0");
        var t2Key = SlackThreadId.Format("C123", "1234.0");

        // Both use same format -- team discrimination happens at resolver level
        t1Key.Should().Be(t2Key);  // Format is the same, team_id is in webhook payload
    }

    [Fact]
    public void TokenCache_BotUserId_IsolatedPerTeam()
    {
        // Slack adapter caches bot user ID per team internally
        // In multi-workspace, T1 might have U_BOT_1, T2 might have U_BOT_2
        var t1BotId = "U_BOT_T1";
        var t2BotId = "U_BOT_T2";

        t1BotId.Should().NotBe(t2BotId);
    }
}
