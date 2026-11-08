using System.Text.Json;

namespace HPD.Agent.Client.Tests;

public sealed class HpdAgentChatContentTests
{
    [Fact]
    public void BuildsUserMessageWithExplicitTextContent()
    {
        var message = HpdAgentChatContent.UserMessage("hello");

        Assert.Equal("user", message.GetProperty("role").GetString());
        var content = message.GetProperty("contents")[0];
        Assert.Equal("text", content.GetProperty("$type").GetString());
        Assert.Equal("hello", content.GetProperty("text").GetString());
    }

    [Fact]
    public void ProjectsStoredContentReferenceToTypedTransportShape()
    {
        var content = HpdAgentChatContent.StoredContentReference(
            "session-one",
            "content-one",
            "v1",
            "image/png",
            "screen.png");

        Assert.Equal("hpd:stored-content-reference", content.GetProperty("$type").GetString());
        var address = content.GetProperty("address");
        Assert.Equal("session-one", address.GetProperty("scope").GetProperty("value").GetString());
        Assert.Equal("content-one", address.GetProperty("contentId").GetString());
        Assert.Equal("v1", address.GetProperty("version").GetString());
        Assert.Equal("image/png", content.GetProperty("mediaType").GetString());
        Assert.Equal("screen.png", content.GetProperty("name").GetString());
    }
}
