using FluentAssertions;
using HPD.Execution.Local.Network;
using Xunit;

namespace HPD.Execution.Local.Tests.Network;

public sealed class TlsTrustEnvironmentTests
{
    [Fact]
    public void Build_WithNullConfig_ReturnsEmpty()
    {
        var environment = TlsTrustEnvironment.Build(null);

        environment.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithEphemeralConfigWithoutResolvedCaPath_ReturnsEmptyForNow()
    {
        var environment = TlsTrustEnvironment.Build(new TlsTerminationConfig());

        environment.Should().BeEmpty();
    }

    [Fact]
    public void Build_WithCaPath_AddsTrustVariables()
    {
        var environment = TlsTrustEnvironment.Build(new TlsTerminationConfig
        {
            CaCertificatePath = "/tmp/hpd-sandbox-ca.pem",
            CaPrivateKeyPath = "/tmp/hpd-sandbox-ca.key"
        });

        environment.Should().ContainKey("NODE_EXTRA_CA_CERTS")
            .WhoseValue.Should().Be("/tmp/hpd-sandbox-ca.pem");
        environment.Should().ContainKey("SSL_CERT_FILE")
            .WhoseValue.Should().Be("/tmp/hpd-sandbox-ca.pem");
        environment.Should().ContainKey("GIT_SSL_CAINFO")
            .WhoseValue.Should().Be("/tmp/hpd-sandbox-ca.pem");
        environment.Should().ContainKey("DENO_CERT")
            .WhoseValue.Should().Be("/tmp/hpd-sandbox-ca.pem");
    }

    [Fact]
    public void Build_WhenInjectionDisabled_ReturnsEmpty()
    {
        var environment = TlsTrustEnvironment.Build(new TlsTerminationConfig
        {
            CaCertificatePath = "/tmp/hpd-sandbox-ca.pem",
            CaPrivateKeyPath = "/tmp/hpd-sandbox-ca.key",
            InjectTrustEnvironmentVariables = false
        });

        environment.Should().BeEmpty();
    }

    [Fact]
    public void ApplyCaCertificatePath_OverwritesExistingTrustVariablesOnly()
    {
        var environment = new Dictionary<string, string>
        {
            ["SSL_CERT_FILE"] = "/tmp/old.pem",
            ["OTHER"] = "kept"
        };

        TlsTrustEnvironment.ApplyCaCertificatePath(environment, "/tmp/new.pem");

        environment["SSL_CERT_FILE"].Should().Be("/tmp/new.pem");
        environment["OTHER"].Should().Be("kept");
    }
}
