using System.Collections.Immutable;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Hosting;

public readonly record struct GatewayHostSchemaVersion(ushort Major, ushort Minor);

public readonly record struct GatewayHostId(string Value);

public enum GatewayListenerBindingKind : byte
{
    AnyIp = 0,
    Loopback = 1,
    IpAddress = 2
}

[Flags]
public enum GatewayListenerProtocols : byte
{
    Http1 = 1,
    Http2 = 2
}

public enum InboundTlsFallback : byte
{
    RejectUnmatchedOrMissingSni = 0
}

public sealed record GatewayHostConfiguration
{
    public required GatewayHostSchemaVersion SchemaVersion { get; init; }
    public required ushort CanonicalizationVersion { get; init; }
    public required GatewayHostId HostId { get; init; }
    public ImmutableArray<GatewayHttpsListenerDeclaration> DataListeners { get; init; } = [];
}

public sealed record GatewayHttpsListenerDeclaration
{
    public required ListenerId Id { get; init; }
    public required GatewayListenerBindingKind Binding { get; init; }
    public string? IpAddress { get; init; }
    public required ushort Port { get; init; }
    public required GatewayListenerProtocols Protocols { get; init; }
    public required GatewayInboundTlsDeclaration Tls { get; init; }
}

public sealed record GatewayInboundTlsDeclaration
{
    public InboundTlsFallback Fallback { get; init; } = InboundTlsFallback.RejectUnmatchedOrMissingSni;
    public ImmutableArray<GatewaySniTlsDeclaration> Sni { get; init; } = [];
}

public sealed record GatewaySniTlsDeclaration
{
    public required string HostnamePattern { get; init; }
    public required SecretReference Certificate { get; init; }
}

public sealed record GatewayHostValidationError(string Code, string Path, string SafeMessage);

public sealed class GatewayHostCandidate
{
    internal GatewayHostCandidate(GatewayHostConfiguration configuration, ImmutableArray<byte> canonicalUtf8, string sha256)
    {
        Configuration = configuration;
        CanonicalUtf8 = canonicalUtf8;
        Sha256 = sha256;
    }

    public GatewayHostConfiguration Configuration { get; }
    public ImmutableArray<byte> CanonicalUtf8 { get; }
    public string Sha256 { get; }
}

public sealed record GatewayHostCandidateResult
{
    public GatewayHostCandidate? Candidate { get; init; }
    public required ImmutableArray<GatewayHostValidationError> Errors { get; init; }
    public bool IsAccepted => Candidate is not null && Errors.IsEmpty;
}
