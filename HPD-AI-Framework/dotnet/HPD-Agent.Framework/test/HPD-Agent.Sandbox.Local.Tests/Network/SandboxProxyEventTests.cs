using FluentAssertions;
using HPD.Sandbox.Local;
using HPD.Sandbox.Local.Network;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Network;

public sealed class SandboxProxyEventTests
{
    [Fact]
    public void ToSandboxViolation_ForNetworkPolicyDenied_MapsToNetworkAccessViolation()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var proxyEvent = new SandboxProxyEvent
        {
            Protocol = SandboxProxyProtocol.Http,
            Kind = SandboxProxyEventKind.NetworkPolicyDenied,
            Reason = "host is not allowed",
            Timestamp = timestamp,
            Host = "blocked.example",
            Port = 443,
            Method = "CONNECT"
        };

        var violation = proxyEvent.ToSandboxViolation();

        violation.Type.Should().Be(ViolationType.NetworkAccess);
        violation.Timestamp.Should().Be(timestamp);
        violation.Path.Should().Be("blocked.example:443");
        violation.Message.Should().Contain("Http proxy denied network access");
        violation.Message.Should().Contain("host is not allowed");
    }

    [Fact]
    public void ToSandboxViolation_ForRequestFilterDenied_UsesUriTarget()
    {
        var uri = new Uri("http://example.com/blocked");
        var proxyEvent = new SandboxProxyEvent
        {
            Protocol = SandboxProxyProtocol.Http,
            Kind = SandboxProxyEventKind.RequestFilterDenied,
            Reason = "blocked by filter",
            Timestamp = DateTimeOffset.UtcNow,
            Uri = uri,
            Host = uri.Host,
            Port = 80,
            Method = "GET"
        };

        var violation = proxyEvent.ToSandboxViolation();

        violation.Type.Should().Be(ViolationType.NetworkAccess);
        violation.Path.Should().Be(uri.ToString());
        violation.Message.Should().Contain("request filter denied");
        violation.Message.Should().Contain("blocked by filter");
    }

    [Fact]
    public void ToSandboxViolation_ForSinkConsumerException_IsNotRequiredByConverter()
    {
        var proxyEvent = new SandboxProxyEvent
        {
            Protocol = SandboxProxyProtocol.Socks5,
            Kind = SandboxProxyEventKind.MalformedRequest,
            Reason = "Unsupported SOCKS5 address type",
            Timestamp = DateTimeOffset.UtcNow
        };

        var violation = proxyEvent.ToSandboxViolation();

        violation.Path.Should().BeNull();
        violation.Message.Should().Contain("Socks5 proxy rejected malformed request");
    }
}
