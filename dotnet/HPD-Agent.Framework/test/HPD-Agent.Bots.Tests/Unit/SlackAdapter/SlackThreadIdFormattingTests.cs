using FluentAssertions;
using HPD.Agent.Bots.Slack;

namespace HPD.Agent.Bots.Tests.Unit.SlackBot;

/// <summary>
/// Tests for Slack thread ID formatting and routing logic.
/// </summary>
public class SlackThreadIdFormattingTests
{
    // ── Thread ID Format ──────────────────────────────────────────────────

    [Fact]
    public void Format_ChannelWithoutThread_Empty()
    {
        var key = SlackThreadId.Format("C123", "");
        key.Should().Be("slack:C123:");
    }

    [Fact]
    public void Format_ChannelWithThread_IncludesTs()
    {
        var key = SlackThreadId.Format("C123", "1234.5");
        key.Should().Be("slack:C123:1234.5");
    }

    [Fact]
    public void Format_DmTopLevel_EmptyTs()
    {
        var key = SlackThreadId.Format("D456", "");
        key.Should().Be("slack:D456:");
    }

    [Fact]
    public void Parse_DmChannel_IsDMTrue()
    {
        var parsed = SlackThreadId.Parse("slack:D456:");
        parsed.IsDM.Should().BeTrue();
    }

    [Fact]
    public void Parse_ChannelChannel_IsDMFalse()
    {
        var parsed = SlackThreadId.Parse("slack:C123:1234.0");
        parsed.IsDM.Should().BeFalse();
    }

    [Fact]
    public void Parse_GroupChannel_IsDMFalse()
    {
        var parsed = SlackThreadId.Parse("slack:G789:1234.0");
        parsed.IsDM.Should().BeFalse();
    }

    [Fact]
    public void Parse_RoundTrip_PreservesValues()
    {
        var channel = "C123";
        var ts = "1234.5";
        var formatted = SlackThreadId.Format(channel, ts);
        var parsed = SlackThreadId.Parse(formatted);

        parsed.Channel.Should().Be(channel);
        parsed.ThreadTs.Should().Be(ts);
    }
}
