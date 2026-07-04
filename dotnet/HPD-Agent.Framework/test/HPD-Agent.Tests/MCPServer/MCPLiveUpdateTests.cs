using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Serialization;
using HPD.Events.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Agent.Tests.MCPServer;

public sealed class MCPLiveUpdateTests
{
    [Fact]
    public void Validate_RejectsResourceSubscriptionsWhenLiveUpdatesAreDisabled()
    {
        var config = new MCPServerConfig
        {
            Name = "fixture",
            Transport = "stdio",
            Command = "server",
            ResourceSubscriptions = ["fixture://hello"]
        };

        var act = config.Validate;

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Resource subscriptions require enableLiveUpdates*");
    }

    [Fact]
    public void McpLiveUpdateEvents_RoundTripThroughAgentEventSerializer()
    {
        var evt = new McpServerChangedEvent
        {
            ServerName = "fixture",
            ChangeKind = McpLiveUpdateKind.ResourceUpdated,
            Uri = "fixture://hello",
            ObservedAt = DateTimeOffset.Parse("2026-06-28T12:00:00Z")
        };

        var json = AgentEventSerializer.ToJson(evt);
        var roundTrip = AgentEventSerializer.FromJson(json);

        json.Should().Contain("\"type\":\"MCP_SERVER_CHANGED\"");
        roundTrip.Should().BeOfType<McpServerChangedEvent>()
            .Which.Should().Match<McpServerChangedEvent>(e =>
                e.ServerName == "fixture" &&
                e.ChangeKind == McpLiveUpdateKind.ResourceUpdated &&
                e.Uri == "fixture://hello");
    }

    [Fact]
    public void AttachLiveUpdates_WithNoLoadedServers_ReturnsDisposable()
    {
        using var manager = new MCPClientManager(NullLogger.Instance);
        var coordinator = new EventCoordinator();

        using var subscription = manager.AttachLiveUpdates(coordinator);

        subscription.Should().NotBeNull();
    }
}
