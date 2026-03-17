using FluentAssertions;
using HPD.Agent.Adapters.Slack;

namespace HPD.Agent.Adapters.Tests.Unit;

/// <summary>
/// Tests for <see cref="SlackThreadId"/> — verifies the source-generated
/// <c>Format</c> / <c>Parse</c> codec and the hand-written <see cref="SlackThreadId.IsDM"/>
/// and <see cref="SlackThreadId.ChannelKey"/> helpers.
/// </summary>
public class SlackThreadIdTests
{
    // ── Format ────────────────────────────────────────────────────────

    [Fact]
    public void Format_ChannelAndThreadTs_ProducesExpectedKey()
    {
        var key = SlackThreadId.Format("C1234567", "1234567890.000100");

        key.Should().Be("slack:C1234567:1234567890.000100");
    }

    [Fact]
    public void Format_EmptyThreadTs_ProducesKeyWithTrailingColon()
    {
        // DM top-level: ThreadTs = ""
        var key = SlackThreadId.Format("D1234567", string.Empty);

        key.Should().Be("slack:D1234567:");
    }

    // ── Parse ─────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidKey_RoundTripsFromFormat()
    {
        const string channel  = "C1234567";
        const string threadTs = "1234567890.000100";

        var key      = SlackThreadId.Format(channel, threadTs);
        var threadId = SlackThreadId.Parse(key);

        threadId.Channel.Should().Be(channel);
        threadId.ThreadTs.Should().Be(threadTs);
    }

    [Fact]
    public void Parse_DmTopLevel_EmptyThreadTs()
    {
        var key      = SlackThreadId.Format("D9876543", string.Empty);
        var threadId = SlackThreadId.Parse(key);

        threadId.Channel.Should().Be("D9876543");
        threadId.ThreadTs.Should().BeEmpty();
    }

    // ── IsDM ──────────────────────────────────────────────────────────

    [Fact]
    public void IsDM_DChannel_ReturnsTrue()
    {
        var threadId = new SlackThreadId("D1234567", "ts");

        threadId.IsDM.Should().BeTrue();
    }

    [Fact]
    public void IsDM_CChannel_ReturnsFalse()
    {
        var threadId = new SlackThreadId("C1234567", "ts");

        threadId.IsDM.Should().BeFalse();
    }

    [Fact]
    public void IsDM_GChannel_ReturnsFalse()
    {
        // G-prefixed channels are private channels / group DMs but not the 'D' DM type
        var threadId = new SlackThreadId("G1234567", "ts");

        threadId.IsDM.Should().BeFalse();
    }

    // ── ChannelKey ────────────────────────────────────────────────────

    [Fact]
    public void ChannelKey_DerivesChannelOnlyKey()
    {
        var threadId = new SlackThreadId("C1234567", "ts");

        threadId.ChannelKey.Should().Be("slack:C1234567");
    }

    [Fact]
    public void ChannelKey_DmChannel_DerivesCorrectKey()
    {
        var threadId = new SlackThreadId("D9876543", string.Empty);

        threadId.ChannelKey.Should().Be("slack:D9876543");
    }

    // ── Record equality ───────────────────────────────────────────────

    [Fact]
    public void SlackThreadId_RecordEquality_SameValues_AreEqual()
    {
        var a = new SlackThreadId("C123", "ts1");
        var b = new SlackThreadId("C123", "ts1");

        a.Should().Be(b);
    }

    [Fact]
    public void SlackThreadId_RecordEquality_DifferentValues_AreNotEqual()
    {
        var a = new SlackThreadId("C123", "ts1");
        var b = new SlackThreadId("C123", "ts2");

        a.Should().NotBe(b);
    }
}
