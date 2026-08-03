using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Effective;

public static class GatewayEffectiveBounds
{
    public const int MaximumRecords = 100_000;
    public const int MaximumContributionsPerRecord = 64;
    public const int MaximumDiagnosticsPerRecord = 16;
    public const int MaximumDiagnostics = 256;
}

public static class GatewayEffectiveFamilies
{
    public const string Authorization = "hpd.gateway/authorization";
    public const string Cors = "hpd.gateway/cors";
    public const string TrafficAdmission = "hpd.gateway/traffic-admission";
    public const string RequestTimeout = "hpd.gateway/request-timeout";
    public const string OutputCache = "hpd.gateway/output-cache";
    public const string Inspection = "hpd.gateway/inspection";
    public const string CredentialDisposition = "hpd.gateway/credential-disposition";
    public const string RequestHeaderTransforms = "hpd.gateway/request-header-transforms";
    public const string ResponseHeaderTransforms = "hpd.gateway/response-header-transforms";
    public const string ResponseTrailerTransforms = "hpd.gateway/response-trailer-transforms";
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayEffectiveTargetKind>))]
public enum GatewayEffectiveTargetKind : byte { Route = 0 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayEffectiveComposition>))]
public enum GatewayEffectiveComposition : byte { ReplaceMoreSpecific = 0, AdditiveOrdered = 1 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayContributionSourceKind>))]
public enum GatewayContributionSourceKind : byte { RootDefault = 0, Inline = 1, ReusableDefinition = 2, HostProfile = 3 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayContributionDisposition>))]
public enum GatewayContributionDisposition : byte { Selected = 0, Overridden = 1, Correlated = 2 }

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayMaterializationDisposition>))]
public enum GatewayMaterializationDisposition : byte { Materialized = 0 }

public sealed record GatewayNativeProjection(string Owner, string Seam, string PackageIdentity);

public sealed record GatewayEffectiveContribution(
    GatewayContributionSourceKind SourceKind,
    GatewayContributionDisposition Disposition,
    string SourceIdentity,
    DefinitionId? Definition,
    int DeterministicOrder,
    ContentHash ContentHash);

public sealed record GatewayEffectiveDiagnostic(string Code, string SafeMessage);

public sealed record GatewayEffectiveRecord(
    ushort SchemaVersion,
    GatewayEffectiveTargetKind TargetKind,
    string TargetId,
    string Family,
    GatewayEffectiveComposition Composition,
    ImmutableArray<GatewayEffectiveContribution> Contributions,
    GatewayNativeProjection NativeProjection,
    string CompilerPackage,
    string CompilerVersion,
    GatewayMaterializationDisposition Disposition,
    ContentHash EffectiveContentHash,
    ImmutableArray<GatewayEffectiveDiagnostic> Diagnostics);

public sealed record GatewayEffectiveSnapshot(
    ushort SchemaVersion,
    CandidateId CandidateId,
    ContentHash CandidateContentHash,
    ImmutableArray<GatewayEffectiveRecord> Records,
    bool IsTruncated);
