using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayHostingTests
{
    [Fact]
    public void HostCandidateNormalizesOrderingIdnaAndProducesDeterministicIdentity()
    {
        var first = Configuration([
            Sni("BÜCHER.example.", "one"),
            Sni("*.Sub.Example", "two")
        ]);
        var second = Configuration([
            Sni("*.sub.example", "two"),
            Sni("xn--bcher-kva.example", "one")
        ]);

        var a = GatewayHostCandidateReader.Create(first);
        var b = GatewayHostCandidateReader.Create(second);

        a.IsAccepted.Should().BeTrue();
        b.IsAccepted.Should().BeTrue();
        a.Candidate!.Sha256.Should().Be(b.Candidate!.Sha256);
        a.Candidate.Configuration.DataListeners[0].Tls.Sni.Select(static value => value.HostnamePattern)
            .Should().Equal("*.sub.example", "xn--bcher-kva.example");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("127.0.0.1")]
    [InlineData("bad/path")]
    [InlineData("bad:443")]
    [InlineData("a.*.example")]
    public void HostCandidateRejectsUnsupportedSniPatterns(string pattern)
    {
        var result = GatewayHostCandidateReader.Create(Configuration([Sni(pattern, "one")]));
        result.IsAccepted.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Code == "host.invalid-sni");
    }

    [Fact]
    public void StrictReaderRejectsUnknownMembersNumericEnumsAndVersions()
    {
        var unknown = GatewayHostCandidateReader.Read(Encoding.UTF8.GetBytes("""
            {"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"hostId":{"value":"host"},"dataListeners":[],"unknown":true}
            """));
        var numeric = GatewayHostCandidateReader.Read(Encoding.UTF8.GetBytes("""
            {"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"hostId":{"value":"host"},"dataListeners":[{"id":{"value":"https"},"binding":0,"port":443,"protocols":"Http1","tls":{"fallback":"RejectUnmatchedOrMissingSni","sni":[{"hostnamePattern":"exact.example","certificate":{"provider":{"value":"test"},"name":{"value":"one"},"version":"v1"}}]}}]}
            """));
        var duplicate = GatewayHostCandidateReader.Read(Encoding.UTF8.GetBytes("""
            {"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"canonicalizationVersion":1,"hostId":{"value":"host"},"dataListeners":[]}
            """));
        var version = GatewayHostCandidateReader.Create(Configuration([Sni("exact.example", "one")]) with { SchemaVersion = new(2, 0) });

        unknown.IsAccepted.Should().BeFalse();
        numeric.IsAccepted.Should().BeFalse();
        duplicate.Errors.Should().ContainSingle(error => error.Code == "host.duplicate-property");
        version.IsAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task KestrelSelectsExactAndLongestWildcardAndRejectsUnknownOrMissingSniBeforeHttp()
    {
        var directory = Directory.CreateTempSubdirectory("hpd-gateway-sni-");
        try
        {
            var exact = CreateCertificate("exact.example", Path.Combine(directory.FullName, "exact.pfx"));
            var wildcard = CreateCertificate("*.example", Path.Combine(directory.FullName, "wildcard.pfx"));
            var nested = CreateCertificate("*.sub.example", Path.Combine(directory.FullName, "nested.pfx"));
            var port = AvailablePort();
            var configuration = ConfigurationWithPort([
                Sni("exact.example", "exact"),
                Sni("*.example", "wildcard"),
                Sni("*.sub.example", "nested")
            ], port);
            var accepted = GatewayHostCandidateReader.Create(configuration);
            accepted.IsAccepted.Should().BeTrue(string.Join(", ", accepted.Errors.Select(static error => error.SafeMessage)));
            var executions = 0;
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseHpdGatewayHost(accepted.Candidate!, certificates =>
            {
                certificates.Add(Reference("exact"), new GatewayPfxCertificateSource { Path = exact.Path, Password = exact.Password });
                certificates.Add(Reference("wildcard"), new GatewayPfxCertificateSource { Path = wildcard.Path, Password = wildcard.Password });
                certificates.Add(Reference("nested"), new GatewayPfxCertificateSource { Path = nested.Path, Password = nested.Password });
            });
            await using var application = builder.Build();
            application.Run(context => { Interlocked.Increment(ref executions); return context.Response.WriteAsync("ok"); });
            await application.StartHpdGatewayAsync();

            (await Send(port, "exact.example")).Thumbprint.Should().Be(exact.Thumbprint);
            (await Send(port, "a.example")).Thumbprint.Should().Be(wildcard.Thumbprint);
            (await Send(port, "b.sub.example")).Thumbprint.Should().Be(nested.Thumbprint);
            var http2 = await Send(port, "exact.example", HttpVersion.Version20);
            http2.Thumbprint.Should().Be(exact.Thumbprint);
            http2.Version.Should().Be(HttpVersion.Version20);
            await FluentActions.Awaiting(() => Send(port, "unknown.test")).Should().ThrowAsync<HttpRequestException>();
            await FluentActions.Awaiting(() => SendWithoutSni(port)).Should().ThrowAsync<IOException>();
            Volatile.Read(ref executions).Should().Be(4);
            var status = application.Services.GetRequiredService<GatewayHostRuntimeStatus>();
            status.GetSnapshot().State.Should().Be(GatewayHostRealizationState.Ready);
            var desired = GatewayHostCandidateReader.Create(ConfigurationWithPort([Sni("exact.example", "exact")], AvailablePort()));
            status.EvaluateDesired(desired.Candidate!).State.Should().Be(GatewayHostRealizationState.RestartRequired);
            status.GetSnapshot().State.Should().Be(GatewayHostRealizationState.RestartRequired);
            await application.StopHpdGatewayAsync();
            status.GetSnapshot().State.Should().Be(GatewayHostRealizationState.Stopped);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LifecycleReportsBindFailureAndRestartReplacement()
    {
        var directory = Directory.CreateTempSubdirectory("hpd-gateway-lifecycle-");
        try
        {
            var certificate = CreateCertificate("exact.example", Path.Combine(directory.FullName, "exact.pfx"));
            var occupiedPort = AvailablePort();
            using var occupied = new TcpListener(IPAddress.Loopback, occupiedPort);
            occupied.Start();
            await using (var failed = BuildHost(occupiedPort, certificate))
            {
                await FluentActions.Awaiting(() => failed.StartHpdGatewayAsync()).Should().ThrowAsync<IOException>();
                failed.Services.GetRequiredService<GatewayHostRuntimeStatus>().GetSnapshot().State
                    .Should().Be(GatewayHostRealizationState.Failed);
            }

            occupied.Stop();
            await using (var canceled = BuildHost(AvailablePort(), certificate))
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await FluentActions.Awaiting(() => canceled.StartHpdGatewayAsync(cancellation.Token))
                    .Should().ThrowAsync<OperationCanceledException>();
                canceled.Services.GetRequiredService<GatewayHostRuntimeStatus>().GetSnapshot().State
                    .Should().Be(GatewayHostRealizationState.Failed);
            }

            var replacementPort = AvailablePort();
            await using (var first = BuildHost(replacementPort, certificate))
            {
                await first.StartHpdGatewayAsync();
                (await Send(replacementPort, "exact.example")).Thumbprint.Should().Be(certificate.Thumbprint);
                await first.StopHpdGatewayAsync();
            }

            await using (var replacement = BuildHost(replacementPort, certificate))
            {
                await replacement.StartHpdGatewayAsync();
                (await Send(replacementPort, "exact.example")).Thumbprint.Should().Be(certificate.Thumbprint);
                await replacement.StopHpdGatewayAsync();
                replacement.Services.GetRequiredService<GatewayHostRuntimeStatus>().GetSnapshot().State
                    .Should().Be(GatewayHostRealizationState.Stopped);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void CertificateSourcesRejectWrongSanExpiredMissingPrivateKeyAndClientAuthOnlyBeforeBind()
    {
        var directory = Directory.CreateTempSubdirectory("hpd-gateway-cert-invalid-");
        try
        {
            var wrong = CreateCertificate("wrong.example", Path.Combine(directory.FullName, "wrong.pfx"));
            var expired = CreateCertificate("exact.example", Path.Combine(directory.FullName, "expired.pfx"),
                DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(-1));
            var valid = CreateCertificate("exact.example", Path.Combine(directory.FullName, "valid.pfx"));
            var clientOnly = CreateCertificate("exact.example", Path.Combine(directory.FullName, "client-only.pfx"),
                enhancedKeyUsageOid: "1.3.6.1.5.5.7.3.2");
            using (var certificate = X509CertificateLoader.LoadPkcs12FromFile(valid.Path, valid.Password))
                File.WriteAllBytes(Path.Combine(directory.FullName, "public-only.pfx"), certificate.Export(X509ContentType.Cert));
            var candidate = GatewayHostCandidateReader.Create(ConfigurationWithPort([Sni("exact.example", "one")], AvailablePort())).Candidate!;

            Action wrongSan = () => WebApplication.CreateSlimBuilder().WebHost.UseHpdGatewayHost(candidate,
                sources => sources.Add(Reference("one"), new GatewayPfxCertificateSource { Path = wrong.Path, Password = wrong.Password }));
            Action expiredSource = () => WebApplication.CreateSlimBuilder().WebHost.UseHpdGatewayHost(candidate,
                sources => sources.Add(Reference("one"), new GatewayPfxCertificateSource { Path = expired.Path, Password = expired.Password }));
            Action noPrivateKey = () => WebApplication.CreateSlimBuilder().WebHost.UseHpdGatewayHost(candidate,
                sources => sources.Add(Reference("one"), new GatewayPfxCertificateSource { Path = Path.Combine(directory.FullName, "public-only.pfx") }));
            Action clientAuthOnly = () => WebApplication.CreateSlimBuilder().WebHost.UseHpdGatewayHost(candidate,
                sources => sources.Add(Reference("one"), new GatewayPfxCertificateSource { Path = clientOnly.Path, Password = clientOnly.Password }));

            wrongSan.Should().Throw<InvalidOperationException>();
            expiredSource.Should().Throw<InvalidOperationException>();
            noPrivateKey.Should().Throw<InvalidOperationException>();
            clientAuthOnly.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static GatewayHostConfiguration Configuration(ImmutableArray<GatewaySniTlsDeclaration> sni) => new()
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        HostId = new("host"),
        DataListeners =
        [
            new GatewayHttpsListenerDeclaration
            {
                Id = new ListenerId("https"),
                Binding = GatewayListenerBindingKind.Loopback,
                Port = 443,
                Protocols = GatewayListenerProtocols.Http1 | GatewayListenerProtocols.Http2,
                Tls = new GatewayInboundTlsDeclaration { Sni = sni }
            }
        ]
    };

    private static GatewayHostConfiguration ConfigurationWithPort(ImmutableArray<GatewaySniTlsDeclaration> sni, int port)
    {
        var configuration = Configuration(sni);
        return configuration with { DataListeners = [configuration.DataListeners[0] with { Port = checked((ushort)port) }] };
    }

    private static GatewaySniTlsDeclaration Sni(string pattern, string name) => new() { HostnamePattern = pattern, Certificate = Reference(name) };
    private static SecretReference Reference(string name) => new(new ProviderId("test"), new ProviderObjectId(name), "v1");

    private static (string Path, string Password, string Thumbprint) CreateCertificate(
        string dnsName,
        string path,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        string enhancedKeyUsageOid = "1.3.6.1.5.5.7.3.1")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(enhancedKeyUsageOid)], true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        using var certificate = request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-1), notAfter ?? DateTimeOffset.UtcNow.AddDays(1));
        const string password = "test-password";
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return (path, password, certificate.Thumbprint);
    }

    private static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<(string Thumbprint, Version Version)> Send(int port, string serverName, Version? version = null)
    {
        string? thumbprint = null;
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) => { thumbprint = certificate?.GetCertHashString(); return true; }
            }
        };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{serverName}:{port}/")
        {
            Version = version ?? HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (thumbprint!, response.Version);
    }

    private static async Task SendWithoutSni(int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var stream = new SslStream(client.GetStream(), false, static (_, _, _, _) => true);
        await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = string.Empty });
    }

    private static WebApplication BuildHost(
        int port,
        (string Path, string Password, string Thumbprint) certificate)
    {
        var candidate = GatewayHostCandidateReader.Create(
            ConfigurationWithPort([Sni("exact.example", "exact")], port)).Candidate!;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseHpdGatewayHost(candidate, sources =>
            sources.Add(Reference("exact"), new GatewayPfxCertificateSource
            {
                Path = certificate.Path,
                Password = certificate.Password
            }));
        var application = builder.Build();
        application.Run(static context => context.Response.WriteAsync("ok"));
        return application;
    }
}
