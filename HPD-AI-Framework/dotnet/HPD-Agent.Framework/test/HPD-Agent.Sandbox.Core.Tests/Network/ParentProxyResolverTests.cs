using FluentAssertions;
using HPD.Execution.Contracts;
using HPD.Agent.Sandbox.Network;
using Xunit;

namespace HPD.Agent.Sandbox.Tests.Network;

public sealed class ParentProxyResolverTests
{
    [Fact]
    public void Resolve_ExplicitProxyWinsOverEnvironment()
    {
        var config = new ParentProxyPolicy { ProxyUri = new Uri("http://explicit.proxy:8080") };
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "http://env.proxy:3128",
        };

        var result = ParentProxyResolver.Resolve(new Uri("http://example.com"), config, environment);

        result.IsBypassed.Should().BeFalse();
        result.ProxyUri!.Host.Should().Be("explicit.proxy");
        result.ProxyUri.Port.Should().Be(8080);
    }

    [Fact]
    public void Resolve_SchemelessProxyDefaultsToHttp()
    {
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "proxy.corp:8080",
        };

        var result = ParentProxyResolver.Resolve(new Uri("http://example.com"), environment: environment);

        result.ProxyUri!.Scheme.Should().Be("http");
        result.ProxyUri.Host.Should().Be("proxy.corp");
        result.ProxyUri.Port.Should().Be(8080);
    }

    [Fact]
    public void Resolve_UsesHttpsProxyForHttpsDestination()
    {
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "http://http.proxy:8080",
            ["HTTPS_PROXY"] = "https://https.proxy:8443",
        };

        var result = ParentProxyResolver.Resolve(new Uri("https://example.com"), environment: environment);

        result.ProxyUri!.Scheme.Should().Be("https");
        result.ProxyUri.Host.Should().Be("https.proxy");
        result.ProxyUri.Port.Should().Be(8443);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://[::1]")]
    public void Resolve_AlwaysBypassesLoopback(string destination)
    {
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "http://proxy.corp:8080",
        };

        var result = ParentProxyResolver.Resolve(new Uri(destination), environment: environment);

        result.IsBypassed.Should().BeTrue();
        result.Reason.Should().Be("loopback");
    }

    [Theory]
    [InlineData("*", "http://example.com")]
    [InlineData("example.com", "http://api.example.com")]
    [InlineData(".example.com", "http://api.example.com")]
    [InlineData(".example.com", "http://example.com")]
    [InlineData("10.0.0.0/8", "http://10.1.2.3")]
    [InlineData("[2001:db8::1]", "http://[2001:db8::1]")]
    public void Resolve_BypassesNoProxyMatches(string noProxy, string destination)
    {
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "http://proxy.corp:8080",
            ["NO_PROXY"] = noProxy,
        };

        var result = ParentProxyResolver.Resolve(new Uri(destination), environment: environment);

        result.IsBypassed.Should().BeTrue();
        result.Reason.Should().Be("no_proxy");
    }

    [Fact]
    public void Resolve_NoProxyNonMatch_UsesProxy()
    {
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "http://proxy.corp:8080",
            ["NO_PROXY"] = ".internal.example,10.0.0.0/8",
        };

        var result = ParentProxyResolver.Resolve(new Uri("http://api.example.com"), environment: environment);

        result.IsBypassed.Should().BeFalse();
        result.ProxyUri!.Host.Should().Be("proxy.corp");
    }

    [Fact]
    public void Resolve_RedactsProxyUserInfo()
    {
        var environment = new Dictionary<string, string?>
        {
            ["HTTP_PROXY"] = "http://user:secret@proxy.corp:8080",
        };

        var result = ParentProxyResolver.Resolve(new Uri("http://example.com"), environment: environment);

        result.RedactedProxyUri.Should().Contain("REDACTED");
        result.RedactedProxyUri.Should().NotContain("secret");
    }

    [Fact]
    public void ParseProxyUri_RejectsUnsupportedScheme()
    {
        var act = () => ParentProxyResolver.ParseProxyUri("socks5://proxy.corp:1080");

        act.Should().Throw<ArgumentException>();
    }
}
