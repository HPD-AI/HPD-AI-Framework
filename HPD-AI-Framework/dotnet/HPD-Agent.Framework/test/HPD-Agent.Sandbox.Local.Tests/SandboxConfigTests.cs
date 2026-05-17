using FluentAssertions;
using HPD.Agent.Sandbox;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxConfigTests
{
    [Fact]
    public void CreateDefault_ReturnsRestrictiveConfig()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowWrite.Should().BeEquivalentTo([".", "/tmp"]);
        config.DenyRead.Should().BeEquivalentTo(["~/.ssh", "~/.aws", "~/.gnupg"]);
        config.AllowRead.Should().BeEmpty();
        config.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        config.AllowedDomains.Should().BeEmpty();
        config.OnInitializationFailure.Should().Be(SandboxFailureBehavior.Block);
        config.OnViolation.Should().Be(SandboxViolationBehavior.EmitEvent);
    }

    [Fact]
    public void CreatePermissive_AllowsNetworkAndMinimalRestrictions()
    {
        var config = SandboxConfig.CreatePermissive();

        config.DenyRead.Should().BeEmpty();
        config.NetworkMode.Should().Be(SandboxNetworkMode.Unrestricted);
        config.AllowedDomains.Should().BeEmpty();
    }

    [Fact]
    public void CreateForMCP_HasMcpOptimizedDefaults()
    {
        var config = SandboxConfig.CreateForMCP();

        config.AllowedDomains.Should().Contain("*.npmjs.org");
        config.AllowedDomains.Should().Contain("*.pypi.org");
        config.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        config.DenyRead.Should().Contain("~/.ssh");
        config.DenyRead.Should().Contain("~/.config");
        config.EnableViolationMonitoring.Should().BeTrue();
    }

    [Fact]
    public void Validate_ThrowsWhenNoWritablePaths()
    {
        var config = new SandboxConfig { AllowWrite = [] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*writable path*");
    }

    [Fact]
    public void Validate_ThrowsWhenPathIsEmpty()
    {
        var config = new SandboxConfig { AllowWrite = [".", ""] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Validate_ThrowsWhenAllowReadPathIsEmpty()
    {
        var config = new SandboxConfig { AllowRead = [""] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Validate_ThrowsWhenDomainPatternIsEmpty()
    {
        var config = new SandboxConfig { AllowedDomains = ["github.com", ""] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Domain*empty*");
    }

    [Fact]
    public void NetworkMode_WithBlockedMode_ReturnsBlocked()
    {
        var config = new SandboxConfig
        {
            NetworkMode = SandboxNetworkMode.Blocked,
        };

        config.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        config.IsNetworkBlocked.Should().BeTrue();
    }

    [Fact]
    public void NetworkMode_WithUnrestrictedMode_ReturnsUnrestricted()
    {
        var config = new SandboxConfig
        {
            NetworkMode = SandboxNetworkMode.Unrestricted,
            AllowedDomains = ["example.com"]
        };

        config.NetworkMode.Should().Be(SandboxNetworkMode.Unrestricted);
        config.IsNetworkUnrestricted.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithFilteredModeAndNoAllowedDomains_Throws()
    {
        var config = new SandboxConfig
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = []
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*AllowedDomains*Filtered*");
    }

    [Fact]
    public void Validate_WithDeniedDomainsOutsideFilteredMode_Throws()
    {
        var config = new SandboxConfig
        {
            NetworkMode = SandboxNetworkMode.Blocked,
            DeniedDomains = ["example.com"]
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*DeniedDomains*Filtered*");
    }

    [Fact]
    public void Validate_WithValidMachLookupPatterns_Allows()
    {
        var config = new SandboxConfig
        {
            AllowMachLookup = ["com.example.service", "com.example.*", "*"]
        };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("com.*.service")]
    [InlineData("com.example*foo")]
    [InlineData("com.example.**")]
    public void Validate_WithInvalidMachLookupPattern_Throws(string pattern)
    {
        var config = new SandboxConfig
        {
            AllowMachLookup = [pattern]
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Mach lookup*");
    }

    [Fact]
    public void Validate_WithMandatoryDenySearchDepthAboveMax_Throws()
    {
        var config = new SandboxConfig
        {
            MandatoryDenySearchDepth = 11
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MandatoryDenySearchDepth*10*");
    }

    [Fact]
    public void Validate_PassesForValidConfig()
    {
        var config = SandboxConfig.CreateDefault();

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Config_CanBeModifiedWithWith()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["api.github.com"],
            EnableViolationMonitoring = true
        };

        config.AllowedDomains.Should().BeEquivalentTo(["api.github.com"]);
        config.EnableViolationMonitoring.Should().BeTrue();
        // Original defaults preserved
        config.AllowWrite.Should().BeEquivalentTo([".", "/tmp"]);
    }

    [Fact]
    public void AllowedEnvironmentVariables_HasSafeDefaults()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowedEnvironmentVariables.Should().Contain("PATH");
        config.AllowedEnvironmentVariables.Should().Contain("HOME");
        config.AllowedEnvironmentVariables.Should().Contain("TERM");
        config.AllowedEnvironmentVariables.Should().Contain("LANG");
    }

    [Fact]
    public void EnableWeakerNestedSandbox_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();

        config.EnableWeakerNestedSandbox.Should().BeFalse();
    }

    [Fact]
    public void AllowMacOSTrustdLookup_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowMacOSTrustdLookup.Should().BeFalse();
    }

    [Fact]
    public void AllowMacOSTrustdLookup_CanBeEnabled()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowMacOSTrustdLookup = true
        };

        config.AllowMacOSTrustdLookup.Should().BeTrue();
    }

    [Fact]
    public void ExternalHttpProxyPort_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.ExternalHttpProxyPort.Should().BeNull();
    }

    [Fact]
    public void ExternalSocksProxyPort_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.ExternalSocksProxyPort.Should().BeNull();
    }

    [Fact]
    public void ExternalProxyPorts_CanBeSet()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            ExternalHttpProxyPort = 8080,
            ExternalSocksProxyPort = 1080
        };

        config.ExternalHttpProxyPort.Should().Be(8080);
        config.ExternalSocksProxyPort.Should().Be(1080);
    }

    [Fact]
    public void ParentProxy_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.ParentProxy.Should().BeNull();
    }

    [Fact]
    public void TlsTerminationAndMitmProxy_DefaultToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.TlsTermination.Should().BeNull();
        config.MitmProxy.Should().BeNull();
    }

    [Fact]
    public void Validate_WithTlsTerminationEphemeralCa_Allows()
    {
        var config = new SandboxConfig
        {
            TlsTermination = new TlsTerminationConfig()
        };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithTlsTerminationOnlyCaCert_Throws()
    {
        var config = new SandboxConfig
        {
            TlsTermination = new TlsTerminationConfig
            {
                CaCertificatePath = "/tmp/sandbox-ca.pem"
            }
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*CaCertificatePath*CaPrivateKeyPath*");
    }

    [Fact]
    public void Validate_WithTlsTerminationRelativeCaPath_Throws()
    {
        var config = new SandboxConfig
        {
            TlsTermination = new TlsTerminationConfig
            {
                CaCertificatePath = "sandbox-ca.pem",
                CaPrivateKeyPath = "/tmp/sandbox-ca.key"
            }
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*CaCertificatePath*absolute path*");
    }

    [Fact]
    public void Validate_WithTlsTerminationAbsoluteCaPair_Allows()
    {
        var config = new SandboxConfig
        {
            TlsTermination = new TlsTerminationConfig
            {
                CaCertificatePath = "/tmp/sandbox-ca.pem",
                CaPrivateKeyPath = "/tmp/sandbox-ca.key",
                LeafCertificateCacheDirectory = "/tmp/sandbox-leaves"
            }
        };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithMitmProxyRelativeSocketPath_Throws()
    {
        var config = new SandboxConfig
        {
            MitmProxy = new MitmProxyConfig
            {
                UnixSocketPath = "mitm.sock"
            }
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*UnixSocketPath*absolute path*");
    }

    [Fact]
    public void Validate_WithTlsTerminationAndMitmProxy_Throws()
    {
        var config = new SandboxConfig
        {
            TlsTermination = new TlsTerminationConfig(),
            MitmProxy = new MitmProxyConfig
            {
                UnixSocketPath = "/tmp/mitm.sock"
            }
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*TlsTermination*MitmProxy*");
    }

    [Fact]
    public void Validate_WithInvalidParentProxy_Throws()
    {
        var config = new SandboxConfig
        {
            ParentProxy = new ParentProxyConfig
            {
                HttpProxy = "socks5://proxy.corp:1080"
            }
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*HttpProxy*");
    }

    [Fact]
    public void Validate_WithSchemelessParentProxy_Allows()
    {
        var config = new SandboxConfig
        {
            ParentProxy = new ParentProxyConfig
            {
                HttpProxy = "proxy.corp:8080"
            }
        };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void AllowUnixSockets_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowUnixSockets.Should().BeNull();
    }

    [Fact]
    public void AllowUnixSockets_CanBeSet()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowUnixSockets = ["/var/run/docker.sock", "/tmp/ssh-agent.sock"]
        };

        config.AllowUnixSockets.Should().NotBeNull();
        config.AllowUnixSockets.Should().HaveCount(2);
        config.AllowUnixSockets.Should().Contain("/var/run/docker.sock");
        config.AllowUnixSockets.Should().Contain("/tmp/ssh-agent.sock");
    }

    [Fact]
    public void AllowAllUnixSockets_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowAllUnixSockets.Should().BeFalse();
    }

    [Fact]
    public void AllowAllUnixSockets_CanBeEnabled()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowAllUnixSockets = true
        };

        config.AllowAllUnixSockets.Should().BeTrue();
    }

    [Fact]
    public void SeccompRuntimeCompilation_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowSeccompRuntimeCompilation.Should().BeFalse();
    }

    [Fact]
    public void SeccompHelperPath_WhenRelative_ValidationThrows()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            SeccompHelperPath = "apply-seccomp-x64"
        };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*SeccompHelperPath*absolute*");
    }

    [Fact]
    public void SeccompHelperPath_WhenAbsolute_ValidationPasses()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            SeccompHelperPath = Path.Combine(Path.GetTempPath(), "apply-seccomp-x64")
        };

        var act = () => config.Validate();

        act.Should().NotThrow();
    }
}
