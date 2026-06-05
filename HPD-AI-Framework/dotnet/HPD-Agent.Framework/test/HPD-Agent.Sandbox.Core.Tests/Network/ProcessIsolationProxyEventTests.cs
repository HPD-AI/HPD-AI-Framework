using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.Network;
using Xunit;

namespace HPD.Agent.Sandbox.Tests.Network;

public sealed class ProcessIsolationProxyEventTests
{
    [Fact]
    public void ToProcessIsolationViolation_ForNetworkPolicyDenied_MapsToNetworkAccessViolation()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var proxyEvent = new ProcessIsolationProxyEvent
        {
            Protocol = ProcessIsolationProxyProtocol.Http,
            Kind = ProcessIsolationProxyEventKind.NetworkPolicyDenied,
            Reason = "host is not allowed",
            Timestamp = timestamp,
            Host = "blocked.example",
            Port = 443,
            Method = "CONNECT"
        };

        var violation = proxyEvent.ToProcessIsolationViolation();

        violation.Type.Should().Be(ProcessIsolationViolationType.NetworkAccess);
        violation.Timestamp.Should().Be(timestamp);
        violation.Path.Should().Be("blocked.example:443");
        violation.Message.Should().Contain("Http proxy denied network access");
        violation.Message.Should().Contain("host is not allowed");
    }

    [Fact]
    public void ToProcessIsolationViolation_ForRequestFilterDenied_UsesUriTarget()
    {
        var uri = new Uri("http://example.com/blocked");
        var proxyEvent = new ProcessIsolationProxyEvent
        {
            Protocol = ProcessIsolationProxyProtocol.Http,
            Kind = ProcessIsolationProxyEventKind.RequestFilterDenied,
            Reason = "blocked by filter",
            Timestamp = DateTimeOffset.UtcNow,
            Uri = uri,
            Host = uri.Host,
            Port = 80,
            Method = "GET"
        };

        var violation = proxyEvent.ToProcessIsolationViolation();

        violation.Type.Should().Be(ProcessIsolationViolationType.NetworkAccess);
        violation.Path.Should().Be(uri.ToString());
        violation.Message.Should().Contain("request filter denied");
        violation.Message.Should().Contain("blocked by filter");
    }

    [Fact]
    public void ToProcessIsolationViolation_ForSinkConsumerException_IsNotRequiredByConverter()
    {
        var proxyEvent = new ProcessIsolationProxyEvent
        {
            Protocol = ProcessIsolationProxyProtocol.Socks5,
            Kind = ProcessIsolationProxyEventKind.MalformedRequest,
            Reason = "Unsupported SOCKS5 address type",
            Timestamp = DateTimeOffset.UtcNow
        };

        var violation = proxyEvent.ToProcessIsolationViolation();

        violation.Path.Should().BeNull();
        violation.Message.Should().Contain("Socks5 proxy rejected malformed request");
    }
}
