using FluentAssertions;
using HPD.Agent.Providers.OpenRouter;

namespace HPD.Agent.Tests.Providers;

public sealed class OpenRouterChatClientTests
{
    [Fact]
    public void SerializeFunctionArguments_WithNullArguments_ReturnsEmptyJsonObject()
    {
        var json = OpenRouterChatClient.SerializeFunctionArguments(null);

        json.Should().Be("{}");
    }

    [Fact]
    public void SerializeFunctionArguments_WithArguments_ReturnsJsonObject()
    {
        var json = OpenRouterChatClient.SerializeFunctionArguments(
            new Dictionary<string, object?>
            {
                ["command"] = "curl wttr.in/NewYork"
            });

        json.Should().Be("""{"command":"curl wttr.in/NewYork"}""");
    }
}
