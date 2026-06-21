using FluentAssertions;
using HPD.Agent.Bots.Teams;

namespace HPD.Agent.Bots.Tests.Unit.TeamsBot;

public class TeamsThreadIdFormattingTests
{
    [Fact]
    public void FormatRaw_EncodesDelimiterHeavyTeamsValues()
    {
        var key = TeamsThreadId.FormatRaw(
            "19:abc@thread.tacv2;messageid=1767297849909",
            "https://smba.trafficmanager.net/amer/");

        key.Should().StartWith("teams:");
        key.Split(':').Should().HaveCount(3);
        key.Should().NotContain("thread.tacv2;messageid");
        key.Should().NotContain("https://");
    }

    [Fact]
    public void Parse_DecodesRawValues()
    {
        var conversationId = "19:abc@thread.tacv2;messageid=1767297849909";
        var serviceUrl = "https://smba.trafficmanager.net/amer/";
        var key = TeamsThreadId.FormatRaw(conversationId, serviceUrl);

        var parsed = TeamsThreadId.Parse(key);

        parsed.DecodedConversationId.Should().Be(conversationId);
        parsed.DecodedServiceUrl.Should().Be(serviceUrl);
        parsed.BaseConversationId.Should().Be("19:abc@thread.tacv2");
    }

    [Theory]
    [InlineData("")]
    [InlineData("teams:only-one-slot")]
    [InlineData("slack:C123:1234.5")]
    public void Parse_MalformedValue_ThrowsFormatException(string value)
    {
        var act = () => TeamsThreadId.Parse(value);

        act.Should().Throw<FormatException>();
    }
}
