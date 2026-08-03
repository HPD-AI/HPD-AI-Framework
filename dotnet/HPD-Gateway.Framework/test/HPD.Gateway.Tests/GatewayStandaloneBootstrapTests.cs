using System.Collections.Immutable;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Hosting;
using HPD.Gateway.Standalone;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayStandaloneBootstrapTests
{
    [Fact]
    public void BootstrapReaderUsesStrictGeneratedContractAndReturnsImmutableActivationBytes()
    {
        using var files = StandaloneFiles.Create();
        var inputs = GatewayStandaloneBootstrapReader.Read(files.BootstrapPath);

        inputs.InitialCandidate.CandidateId.Should().Be(new CandidateId("candidate"));
        inputs.InitialCandidate.AuthorityId.Should().Be("authority");
        inputs.InitialCandidate.AuthorityEpoch.Should().Be("epoch");
        inputs.InitialCandidate.AuthorityVersion.Should().Be(1);
        inputs.InitialCandidate.Utf8Configuration.Should().Equal(files.GatewayBytes);
        inputs.Certificates.Should().ContainSingle();
    }

    [Fact]
    public void BootstrapReaderRejectsDuplicatePropertiesAndInvalidEnvironmentNames()
    {
        using var files = StandaloneFiles.Create();
        File.WriteAllText(files.BootstrapPath,
            File.ReadAllText(files.BootstrapPath).Replace(
                "\"schemaVersion\":\"hpd.gateway.standalone/v1\"",
                "\"schemaVersion\":\"hpd.gateway.standalone/v1\",\"schemaVersion\":\"hpd.gateway.standalone/v1\"",
                StringComparison.Ordinal));

        FluentActions.Invoking(() => GatewayStandaloneBootstrapReader.Read(files.BootstrapPath))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*malformed or unsupported*");

        files.WriteBootstrap("BAD-NAME");
        FluentActions.Invoking(() => GatewayStandaloneBootstrapReader.Read(files.BootstrapPath))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*environment-variable name is invalid*");
    }

    private sealed class StandaloneFiles : IDisposable
    {
        private StandaloneFiles(string directory, byte[] gatewayBytes)
        {
            Directory = directory;
            GatewayBytes = gatewayBytes;
            BootstrapPath = Path.Combine(directory, "bootstrap.json");
        }

        internal string Directory { get; }
        internal string BootstrapPath { get; }
        internal byte[] GatewayBytes { get; }

        internal static StandaloneFiles Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"hpd-gateway-standalone-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var gateway = new GatewayConfiguration
            {
                SchemaVersion = new(1, 0),
                CanonicalizationVersion = 1,
                Routes = [],
                Upstreams = [],
                Definitions = new(),
                RootDefaults = new(),
                Metadata = new()
            };
            var gatewayBytes = JsonSerializer.SerializeToUtf8Bytes(
                gateway,
                GatewayJsonSerializerContext.Default.GatewayConfiguration);
            var files = new StandaloneFiles(directory, gatewayBytes);
            File.WriteAllBytes(Path.Combine(directory, "gateway.json"), gatewayBytes);
            var host = GatewayHostCandidateReader.Create(new GatewayHostConfiguration
            {
                SchemaVersion = new(1, 0),
                CanonicalizationVersion = 1,
                HostId = new("standalone"),
                DataListeners =
                [
                    new GatewayHttpsListenerDeclaration
                    {
                        Id = new("https"),
                        Binding = GatewayListenerBindingKind.Loopback,
                        Port = 8443,
                        Protocols = GatewayListenerProtocols.Http1,
                        Tls = new GatewayInboundTlsDeclaration
                        {
                            Sni =
                            [
                                new GatewaySniTlsDeclaration
                                {
                                    HostnamePattern = "localhost",
                                    Certificate = new(new("pfx"), new("localhost"), "v1")
                                }
                            ]
                        }
                    }
                ]
            }).Candidate!;
            File.WriteAllBytes(Path.Combine(directory, "host.json"), host.CanonicalUtf8.ToArray());
            File.WriteAllBytes(Path.Combine(directory, "certificate.pfx"), [1]);
            files.WriteBootstrap(null);
            return files;
        }

        internal void WriteBootstrap(string? passwordEnvironmentVariable)
        {
            var bootstrap = new GatewayStandaloneBootstrap
            {
                SchemaVersion = "hpd.gateway.standalone/v1",
                HostConfigurationPath = Path.Combine(Directory, "host.json"),
                GatewayConfigurationPath = Path.Combine(Directory, "gateway.json"),
                CandidateId = new("candidate"),
                AuthorityId = "authority",
                AuthorityEpoch = "epoch",
                AuthorityVersion = 1,
                Certificates =
                [
                    new GatewayStandaloneCertificateSource
                    {
                        Provider = new("pfx"),
                        Name = new("localhost"),
                        Version = "v1",
                        PfxPath = Path.Combine(Directory, "certificate.pfx"),
                        PasswordEnvironmentVariable = passwordEnvironmentVariable
                    }
                ]
            };
            File.WriteAllBytes(BootstrapPath, JsonSerializer.SerializeToUtf8Bytes(
                bootstrap,
                GatewayStandaloneJsonContext.Default.GatewayStandaloneBootstrap));
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
