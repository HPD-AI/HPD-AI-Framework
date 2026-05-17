using FluentAssertions;
using HPD.Sandbox.Local.Policy;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Policy;

public sealed class NetworkPolicyParityTests
{
    [Theory]
    [InlineData("example.com")]
    [InlineData("api.example.com")]
    [InlineData("*.example.com")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    public void DomainPattern_Parse_AcceptsValidPatterns(string pattern)
    {
        var parsed = DomainPattern.Parse(pattern);

        parsed.Raw.Should().Be(pattern);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("*")]
    [InlineData("*.")]
    [InlineData("*.com")]
    [InlineData("https://example.com")]
    [InlineData("example.com/path")]
    [InlineData("example.com:443")]
    [InlineData("evil*.example.com")]
    [InlineData("example..com")]
    [InlineData("bad\nexample.com")]
    public void DomainPattern_Parse_RejectsUnsafePatterns(string pattern)
    {
        var act = () => DomainPattern.Parse(pattern);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WildcardDomain_MatchesSubdomain_NotApexOrSuffixSibling()
    {
        var policy = NetworkPolicy.Filtered([DomainPattern.Parse("*.example.com")]);
        var evaluator = new NetworkPolicyEvaluator(policy);

        evaluator.Evaluate("api.example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Allow);
        evaluator.Evaluate("deep.api.example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Allow);
        evaluator.Evaluate("example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
        evaluator.Evaluate("notexample.com").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
    }

    [Fact]
    public void DeniedDomain_TakesPrecedenceOverAllowedWildcard()
    {
        var policy = NetworkPolicy.Filtered(
            [DomainPattern.Parse("*.example.com")],
            [DomainPattern.Parse("api.example.com")]);
        var evaluator = new NetworkPolicyEvaluator(policy);

        evaluator.Evaluate("api.example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
        evaluator.Evaluate("cdn.example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Allow);
    }

    [Theory]
    [InlineData("API.EXAMPLE.COM.")]
    [InlineData("api.example.com")]
    public void HostCanonicalization_NormalizesCaseAndTrailingDot(string host)
    {
        var policy = NetworkPolicy.Filtered([DomainPattern.Parse("api.example.com")]);
        var evaluator = new NetworkPolicyEvaluator(policy);

        evaluator.Evaluate(host).Kind.Should().Be(NetworkPolicyDecisionKind.Allow);
    }

    [Theory]
    [InlineData("evil.com\0.example.com")]
    [InlineData("evil.com\r\nHost: example.com")]
    [InlineData("::ffff:1.2.3.4%25.example.com")]
    [InlineData("2130706433")]
    [InlineData("127.1")]
    [InlineData("0x7f.0.0.1")]
    public void MalformedOrAmbiguousHosts_AreDeniedBeforePolicyMatching(string host)
    {
        var policy = NetworkPolicy.Filtered([DomainPattern.Parse("*.example.com")]);
        var evaluator = new NetworkPolicyEvaluator(policy);

        var decision = evaluator.Evaluate(host);

        decision.Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
        decision.Reason.Should().NotBe("no matching allow rule");
    }

    [Fact]
    public void IpLiteralPattern_MatchesOnlyEquivalentIpLiteral()
    {
        var policy = NetworkPolicy.Filtered([DomainPattern.Parse("127.0.0.1")]);
        var evaluator = new NetworkPolicyEvaluator(policy);

        evaluator.Evaluate("127.0.0.1").Kind.Should().Be(NetworkPolicyDecisionKind.Allow);
        evaluator.Evaluate("localhost").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
    }

    [Fact]
    public void UnrestrictedPolicy_AllowsMalformedHostsBecauseProxyPolicyIsDisabled()
    {
        var evaluator = new NetworkPolicyEvaluator(NetworkPolicy.Unrestricted);

        evaluator.Evaluate("evil.com\0.example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Allow);
    }

    [Fact]
    public void BlockedPolicy_DeniesEvenAllowedLookingHosts()
    {
        var evaluator = new NetworkPolicyEvaluator(NetworkPolicy.Blocked);

        evaluator.Evaluate("example.com").Kind.Should().Be(NetworkPolicyDecisionKind.Deny);
    }
}
