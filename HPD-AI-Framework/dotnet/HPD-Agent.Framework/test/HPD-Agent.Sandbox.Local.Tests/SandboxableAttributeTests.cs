using FluentAssertions;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxableAttributeTests
{
    [Fact]
    public void DefaultValues_AreRestrictive()
    {
        var attr = new SandboxableAttribute();

        attr.Profile.Should().BeEmpty();
        attr.AllowedDomains.Should().BeEmpty();
        attr.DeniedDomains.Should().BeEmpty();
        attr.AllowWrite.Should().Be(".,/tmp");
        attr.DenyRead.Should().Be("~/.ssh,~/.aws,~/.gnupg");
    }

    [Fact]
    public void GetAllowedDomains_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            AllowedDomains = "api.github.com,*.npmjs.org,pypi.org"
        };

        var domains = attr.GetAllowedDomains();

        domains.Should().HaveCount(3);
        domains.Should().Contain("api.github.com");
        domains.Should().Contain("*.npmjs.org");
        domains.Should().Contain("pypi.org");
    }

    [Fact]
    public void GetAllowedDomains_ReturnsEmptyArrayWhenEmpty()
    {
        var attr = new SandboxableAttribute { AllowedDomains = "" };

        var domains = attr.GetAllowedDomains();

        domains.Should().BeEmpty();
    }

    [Fact]
    public void GetAllowedDomains_TrimsWhitespace()
    {
        var attr = new SandboxableAttribute
        {
            AllowedDomains = "  api.github.com  ,  *.npmjs.org  "
        };

        var domains = attr.GetAllowedDomains();

        domains.Should().Contain("api.github.com");
        domains.Should().Contain("*.npmjs.org");
    }

    [Fact]
    public void GetDeniedDomains_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            DeniedDomains = "malicious.com,evil.org"
        };

        var domains = attr.GetDeniedDomains();

        domains.Should().HaveCount(2);
        domains.Should().Contain("malicious.com");
        domains.Should().Contain("evil.org");
    }

    [Fact]
    public void GetDeniedDomains_ReturnsEmptyArrayWhenEmpty()
    {
        var attr = new SandboxableAttribute { DeniedDomains = "" };

        var domains = attr.GetDeniedDomains();

        domains.Should().BeEmpty();
    }

    [Fact]
    public void GetAllowWrite_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            AllowWrite = "./workspace,./output,/tmp"
        };

        var paths = attr.GetAllowWrite();

        paths.Should().HaveCount(3);
        paths.Should().Contain("./workspace");
        paths.Should().Contain("./output");
        paths.Should().Contain("/tmp");
    }

    [Fact]
    public void GetAllowWrite_ReturnsDefaultsWhenEmpty()
    {
        var attr = new SandboxableAttribute { AllowWrite = "" };

        var paths = attr.GetAllowWrite();

        paths.Should().Contain(".");
        paths.Should().Contain("/tmp");
    }

    [Fact]
    public void GetDenyRead_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            DenyRead = "~/.ssh,~/.aws,~/.gnupg,~/.config/secrets"
        };

        var paths = attr.GetDenyRead();

        paths.Should().HaveCount(4);
        paths.Should().Contain("~/.ssh");
        paths.Should().Contain("~/.aws");
        paths.Should().Contain("~/.gnupg");
        paths.Should().Contain("~/.config/secrets");
    }

    [Fact]
    public void GetDenyRead_ReturnsDefaultsWhenEmpty()
    {
        var attr = new SandboxableAttribute { DenyRead = "" };

        var paths = attr.GetDenyRead();

        paths.Should().Contain("~/.ssh");
        paths.Should().Contain("~/.aws");
        paths.Should().Contain("~/.gnupg");
    }

    [Fact]
    public void Profile_CanBeSet()
    {
        var attr = new SandboxableAttribute { Profile = "network-only" };

        attr.Profile.Should().Be("network-only");
    }

    [Theory]
    [InlineData("restrictive")]
    [InlineData("permissive")]
    [InlineData("network-only")]
    [InlineData("filesystem-only")]
    public void Profile_AcceptsValidValues(string profile)
    {
        var attr = new SandboxableAttribute { Profile = profile };

        attr.Profile.Should().Be(profile);
    }

    [Fact]
    public void Attribute_CanBeAppliedToMethods()
    {
        var attrType = typeof(SandboxableAttribute);

        var usage = attrType.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .FirstOrDefault();

        usage.Should().NotBeNull();
        usage!.ValidOn.Should().HaveFlag(AttributeTargets.Method);
    }

    [Fact]
    public void Attribute_DisallowsMultiple()
    {
        var attrType = typeof(SandboxableAttribute);

        var usage = attrType.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .FirstOrDefault();

        usage.Should().NotBeNull();
        usage!.AllowMultiple.Should().BeFalse();
    }
}

public class SandboxableAttributeUsageTests
{
    // Test that the attribute can be used on methods
    [Sandboxable]
    public void BasicSandboxedMethod() { }

    [Sandboxable(AllowedDomains = "api.github.com")]
    public void MethodWithNetwork() { }

    [Sandboxable(Profile = "restrictive", DenyRead = "~/.ssh,~/.aws")]
    public void MethodWithProfile() { }

    [Fact]
    public void Attribute_CanBeRetrievedFromMethod()
    {
        var method = GetType().GetMethod(nameof(BasicSandboxedMethod));
        var attr = method!.GetCustomAttributes(typeof(SandboxableAttribute), false)
            .Cast<SandboxableAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull();
    }

    [Fact]
    public void Attribute_PreservesValues()
    {
        var method = GetType().GetMethod(nameof(MethodWithNetwork));
        var attr = method!.GetCustomAttributes(typeof(SandboxableAttribute), false)
            .Cast<SandboxableAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull();
        attr!.GetAllowedDomains().Should().Contain("api.github.com");
    }

    [Fact]
    public void Attribute_PreservesProfile()
    {
        var method = GetType().GetMethod(nameof(MethodWithProfile));
        var attr = method!.GetCustomAttributes(typeof(SandboxableAttribute), false)
            .Cast<SandboxableAttribute>()
            .FirstOrDefault();

        attr.Should().NotBeNull();
        attr!.Profile.Should().Be("restrictive");
        attr.GetDenyRead().Should().Contain("~/.ssh");
        attr.GetDenyRead().Should().Contain("~/.aws");
    }
}
