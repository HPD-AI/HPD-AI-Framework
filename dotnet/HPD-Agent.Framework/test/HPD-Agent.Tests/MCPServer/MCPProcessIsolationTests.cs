using FluentAssertions;
using HPD.Agent.MCP;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HPD.Agent.Tests.MCPServer;

public sealed class MCPProcessIsolationTests
{
    [Fact]
    public async Task IsolatedStdioServer_WithoutProcessProvider_FailsClosed()
    {
        const string manifest = """
        {
          "servers": [
            {
              "name": "isolated",
              "transport": "stdio",
              "command": "node",
              "processIsolation": {
                "mode": "Isolated",
                "profile": "filesystem-only"
              }
            }
          ]
        }
        """;

        var manager = new MCPClientManager(
            NullLogger.Instance,
            new MCPOptions { FailOnServerError = true });

        var act = async () => await manager.LoadToolsFromManifestContentAsync(manifest);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().NotBeNull();
        exception.Which.InnerException!.Message.Should().Contain("ProcessProvider was not configured");
    }

    [Fact]
    public async Task DisabledProcessIsolation_UsesNormalStdioPath()
    {
        const string manifest = """
        {
          "servers": [
            {
              "name": "disabled",
              "transport": "stdio",
              "command": "__definitely_missing_mcp_server__",
              "processIsolation": {
                "mode": "Disabled"
              }
            }
          ]
        }
        """;

        var manager = new MCPClientManager(
            NullLogger.Instance,
            new MCPOptions { FailOnServerError = true });

        var act = async () => await manager.LoadToolsFromManifestContentAsync(manifest);

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.InnerException.Should().NotBeNull();
        exception.Which.InnerException!.Message.Should().NotContain("ProcessProvider was not configured");
    }
}
