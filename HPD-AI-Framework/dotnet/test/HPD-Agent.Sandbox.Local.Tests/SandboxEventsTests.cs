using FluentAssertions;
using HPD.Sandbox.Local.Events;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxEventsTests
{
    [Fact]
    public void SandboxViolationEvent_HasCorrectSourceName()
    {
        var evt = new SandboxViolationEvent();

        evt.SourceName.Should().Be("SandboxMiddleware");
    }

    [Fact]
    public void SandboxViolationEvent_CanBeConstructedWithParameters()
    {
        var evt = new SandboxViolationEvent(
            functionName: "dangerous_function",
            violationType: ViolationType.FilesystemWrite,
            message: "Attempted write to /etc/passwd",
            path: "/etc/passwd");

        evt.FunctionName.Should().Be("dangerous_function");
        evt.ViolationType.Should().Be(ViolationType.FilesystemWrite);
        evt.Message.Should().Contain("/etc/passwd");
        evt.Path.Should().Be("/etc/passwd");
    }

    [Fact]
    public void SandboxBlockedEvent_HasCorrectSourceName()
    {
        var evt = new SandboxBlockedEvent();

        evt.SourceName.Should().Be("SandboxMiddleware");
    }

    [Fact]
    public void SandboxBlockedEvent_CanBeConstructedWithParameters()
    {
        var evt = new SandboxBlockedEvent("mcp_function", "Previous violation detected");

        evt.FunctionName.Should().Be("mcp_function");
        evt.Reason.Should().Be("Previous violation detected");
    }

    [Fact]
    public void SandboxErrorEvent_HasCorrectSourceName()
    {
        var evt = new SandboxErrorEvent();

        evt.SourceName.Should().Be("SandboxMiddleware");
    }

    [Fact]
    public void SandboxErrorEvent_CanBeConstructedWithMessage()
    {
        var evt = new SandboxErrorEvent("Bubblewrap not found");

        evt.Message.Should().Be("Bubblewrap not found");
    }

    [Fact]
    public void SandboxWarningEvent_HasCorrectSourceName()
    {
        var evt = new SandboxWarningEvent();

        evt.SourceName.Should().Be("SandboxMiddleware");
    }

    [Fact]
    public void SandboxWarningEvent_CanBeConstructedWithMessage()
    {
        var evt = new SandboxWarningEvent("Running without network isolation");

        evt.Message.Should().Be("Running without network isolation");
    }

    [Fact]
    public void SandboxInitializedEvent_HasTimestamp()
    {
        var evt = new SandboxInitializedEvent
        {
            Tier = HPD.Agent.Sandbox.SandboxTier.Local,
            Platform = "Linux"
        };

        evt.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void SandboxServerStartedEvent_HasAllProperties()
    {
        var evt = new SandboxServerStartedEvent
        {
            ServerName = "weather-mcp",
            Tier = HPD.Agent.Sandbox.SandboxTier.Local,
            AllowedDomains = ["api.weather.com"]
        };

        evt.ServerName.Should().Be("weather-mcp");
        evt.Tier.Should().Be(HPD.Agent.Sandbox.SandboxTier.Local);
        evt.AllowedDomains.Should().Contain("api.weather.com");
    }

    [Fact]
    public void SandboxEventTypes_HasExpectedConstants()
    {
        SandboxEventTypes.SANDBOX_VIOLATION.Should().Be("SANDBOX_VIOLATION");
        SandboxEventTypes.SANDBOX_BLOCKED.Should().Be("SANDBOX_BLOCKED");
        SandboxEventTypes.SANDBOX_ERROR.Should().Be("SANDBOX_ERROR");
        SandboxEventTypes.SANDBOX_WARNING.Should().Be("SANDBOX_WARNING");
        SandboxEventTypes.SANDBOX_INITIALIZED.Should().Be("SANDBOX_INITIALIZED");
        SandboxEventTypes.SANDBOX_SERVER_STARTED.Should().Be("SANDBOX_SERVER_STARTED");
    }
}
