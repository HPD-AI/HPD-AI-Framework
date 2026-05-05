using FluentAssertions;
using HPD.Agent.Bots.Teams;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsFormatConverterTests
{
    [Fact]
    public void ToTeamsMarkdown_WrapsBareMentions()
    {
        var rendered = new TeamsFormatConverter().ToTeamsMarkdown("hi @Ada and @Grace-Hopper");

        rendered.Should().Be("hi <at>Ada</at> and <at>Grace-Hopper</at>");
    }

    [Fact]
    public void ToPlainText_UnwrapsTeamsMentions()
    {
        var plain = new TeamsFormatConverter().ToPlainText("hello <at>Ada</at>");

        plain.Should().Be("hello @Ada");
    }

    [Fact]
    public void RenderMention_UsesTeamsAtTag()
    {
        new TeamsFormatConverter().RenderMention("Ada").Should().Be("<at>Ada</at>");
    }
}
