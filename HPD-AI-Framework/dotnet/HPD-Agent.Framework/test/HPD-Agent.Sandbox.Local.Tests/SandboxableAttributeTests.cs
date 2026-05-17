using FluentAssertions;
using HPD.Agent.Sandbox;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxableAttributeTests
{
    [Fact]
    public void DefaultValues_AreSparseInherit()
    {
        var attr = new SandboxableAttribute();

        attr.Profile.Should().BeEmpty();
        attr.NetworkMode.Should().Be(SandboxNetworkPolicy.Inherit);
        attr.AllowedDomains.Should().BeEmpty();
        attr.DeniedDomains.Should().BeEmpty();
        attr.AllowWrite.Should().BeEmpty();
        attr.DenyRead.Should().BeEmpty();
        attr.AllowRead.Should().BeEmpty();
        attr.DenyWrite.Should().BeEmpty();
        attr.AllowUnixSockets.Should().BeEmpty();
        attr.AllowMachLookup.Should().BeEmpty();
        attr.AllowPty.Should().Be(SandboxToggle.Inherit);
        attr.AllowLocalBinding.Should().Be(SandboxToggle.Inherit);
        attr.AllowAllUnixSockets.Should().Be(SandboxToggle.Inherit);
        attr.AllowMacOSTrustdLookup.Should().Be(SandboxToggle.Inherit);
        attr.AllowGitConfig.Should().Be(SandboxToggle.Inherit);
        attr.EnableWeakerNestedSandbox.Should().Be(SandboxToggle.Inherit);
        attr.IgnoreViolationPatterns.Should().BeEmpty();
        attr.AllowedEnvironmentVariables.Should().BeEmpty();
        attr.MandatoryDenySearchDepth.Should().Be(-1);
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
    public void GetAllowWrite_ReturnsEmptyArrayWhenEmpty()
    {
        var attr = new SandboxableAttribute { AllowWrite = "" };

        var paths = attr.GetAllowWrite();

        paths.Should().BeEmpty();
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
    public void GetDenyRead_ReturnsEmptyArrayWhenEmpty()
    {
        var attr = new SandboxableAttribute { DenyRead = "" };

        var paths = attr.GetDenyRead();

        paths.Should().BeEmpty();
    }

    [Fact]
    public void GetAllowRead_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            AllowRead = "./workspace,./docs"
        };

        attr.GetAllowRead().Should().BeEquivalentTo(["./workspace", "./docs"]);
    }

    [Fact]
    public void GetDenyWrite_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            DenyWrite = ".git/hooks,.npmrc"
        };

        attr.GetDenyWrite().Should().BeEquivalentTo([".git/hooks", ".npmrc"]);
    }

    [Fact]
    public void GetAllowUnixSockets_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            AllowUnixSockets = "/var/run/docker.sock,~/.ssh/agent.sock"
        };

        attr.GetAllowUnixSockets().Should().BeEquivalentTo(["/var/run/docker.sock", "~/.ssh/agent.sock"]);
    }

    [Fact]
    public void GetAllowMachLookup_ParsesCommaSeparatedString()
    {
        var attr = new SandboxableAttribute
        {
            AllowMachLookup = "com.example.service,com.example.*"
        };

        attr.GetAllowMachLookup().Should().BeEquivalentTo(["com.example.service", "com.example.*"]);
    }

    [Fact]
    public void ToSandboxConfigOverride_MapsAttributeDeclaration()
    {
        var attr = new SandboxableAttribute
        {
            NetworkMode = SandboxNetworkPolicy.Filtered,
            AllowedDomains = "api.github.com,registry.npmjs.org",
            DeniedDomains = "evil.github.com",
            AllowWrite = "./workspace,/tmp",
            DenyRead = "~/.ssh,~/.aws",
            AllowRead = "./workspace/public",
            DenyWrite = ".git/hooks,.npmrc",
            AllowUnixSockets = "/var/run/docker.sock",
            AllowMachLookup = "com.example.*",
            AllowPty = SandboxToggle.Enabled,
            AllowLocalBinding = SandboxToggle.Enabled,
            AllowAllUnixSockets = SandboxToggle.Enabled,
            AllowMacOSTrustdLookup = SandboxToggle.Enabled,
            AllowGitConfig = SandboxToggle.Enabled,
            EnableWeakerNestedSandbox = SandboxToggle.Disabled,
            IgnoreViolationPatterns = "cache,expected",
            AllowedEnvironmentVariables = "PATH,HOME",
            MandatoryDenySearchDepth = 5
        };

        var config = attr.ToSandboxConfigOverride();

        config.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        config.AllowedDomains.Should().BeEquivalentTo(["api.github.com", "registry.npmjs.org"]);
        config.DeniedDomains.Should().BeEquivalentTo(["evil.github.com"]);
        config.AllowWrite.Should().BeEquivalentTo(["./workspace", "/tmp"]);
        config.DenyRead.Should().BeEquivalentTo(["~/.ssh", "~/.aws"]);
        config.AllowRead.Should().BeEquivalentTo(["./workspace/public"]);
        config.DenyWrite.Should().BeEquivalentTo([".git/hooks", ".npmrc"]);
        config.AllowUnixSockets.Should().BeEquivalentTo(["/var/run/docker.sock"]);
        config.AllowMachLookup.Should().BeEquivalentTo(["com.example.*"]);
        config.AllowPty.Should().BeTrue();
        config.AllowLocalBinding.Should().BeTrue();
        config.AllowAllUnixSockets.Should().BeTrue();
        config.AllowMacOSTrustdLookup.Should().BeTrue();
        config.AllowGitConfig.Should().BeTrue();
        config.EnableWeakerNestedSandbox.Should().BeFalse();
        config.IgnoreViolationPatterns.Should().BeEquivalentTo(["cache", "expected"]);
        config.AllowedEnvironmentVariables.Should().BeEquivalentTo(["PATH", "HOME"]);
        config.MandatoryDenySearchDepth.Should().Be(5);
    }

    [Fact]
    public void ToSandboxConfigOverride_BareAttributeHasNoOverrides()
    {
        var attr = new SandboxableAttribute();

        var config = attr.ToSandboxConfigOverride();

        config.NetworkMode.Should().BeNull();
        config.AllowWrite.Should().BeNull();
        config.DenyRead.Should().BeNull();
        config.AllowPty.Should().BeNull();
        config.MandatoryDenySearchDepth.Should().BeNull();
    }

    [Fact]
    public void Profile_CanBeSet()
    {
        var attr = new SandboxableAttribute { Profile = "network-only" };

        attr.Profile.Should().Be("network-only");
    }

    [Fact]
    public void NetworkMode_CanBeSet()
    {
        var attr = new SandboxableAttribute { NetworkMode = SandboxNetworkPolicy.Filtered };

        attr.NetworkMode.Should().Be(SandboxNetworkPolicy.Filtered);
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

    [Sandboxable(NetworkMode = SandboxNetworkPolicy.Filtered, AllowedDomains = "api.github.com")]
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
        attr!.NetworkMode.Should().Be(SandboxNetworkPolicy.Filtered);
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
