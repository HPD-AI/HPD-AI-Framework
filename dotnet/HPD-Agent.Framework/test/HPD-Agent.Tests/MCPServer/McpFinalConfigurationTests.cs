using HPD.Agent.MCP;

namespace HPD.Agent.Tests.MCPServer;

public sealed class McpFinalConfigurationTests
{
    [Fact]
    public void DefaultProtocolUsesSdkNegotiation()
    {
        var options = new McpOptions();

        Assert.Null(options.Protocol.ExactVersion);
        Assert.Equal(TimeSpan.FromSeconds(5), options.Protocol.DiscoveryTimeout);
        Assert.False(options.Invocation.EnableRemoteTasks);
        Assert.Null(options.Invocation.RemoteTaskAdapter);
    }

    [Theory]
    [InlineData("MCP-Protocol-Version")]
    [InlineData("mcp-session-id")]
    [InlineData("Mcp-Method")]
    [InlineData("mcp-name")]
    public void ReservedHeadersAreRejectedCaseInsensitively(string header)
    {
        var server = HttpServer();
        server.Headers = new() { [header] = "forbidden" };

        Assert.Throws<ArgumentException>(server.Validate);
    }

    [Fact]
    public void DynamicRegistrationRequiresExplicitAuthority()
    {
        var server = HttpServer();
        server.OAuth = new McpOAuthOptions
        {
            RedirectUri = new Uri("https://client.example/callback"),
            RegistrationMode = McpOAuthClientRegistrationMode.DynamicRegistration
        };

        Assert.Throws<ArgumentException>(server.Validate);
        server.OAuth.AllowDynamicRegistration = true;
        server.Validate();
    }

    [Fact]
    public void ManifestRejectsDuplicateRegistrationNames()
    {
        var manifest = new McpManifest { Servers = [HttpServer(), HttpServer()] };

        Assert.Throws<ArgumentException>(manifest.Validate);
    }

    private static McpServerConfig HttpServer() => new()
    {
        Name = "documents",
        Transport = "http",
        Endpoint = new Uri("https://mcp.example")
    };
}
