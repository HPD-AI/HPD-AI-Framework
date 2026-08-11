using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Gateway;

namespace HPD.Gateway.Standalone;

internal sealed record GatewayStandaloneBootstrap
{
    public required string SchemaVersion { get; init; }
    public required string HostConfigurationPath { get; init; }
    public required string GatewayConfigurationPath { get; init; }
    public required string NamespaceId { get; init; }
    public required string TargetNodeId { get; init; }
    public required CandidateId CandidateId { get; init; }
    public required string AuthorityId { get; init; }
    public required string AuthorityEpoch { get; init; }
    public required ulong AuthorityVersion { get; init; }
    public required GatewayStandaloneManagement Management { get; init; }
    public GatewayStandaloneRedisAdmission? RedisAdmission { get; init; }
    public ImmutableArray<GatewayStandaloneCertificateSource> Certificates { get; init; } = [];
}

internal sealed record GatewayStandaloneRedisAdmission
{
    public required string AuthorityId { get; init; }
    public required string ConfigurationEnvironmentVariable { get; init; }
    public string KeyPrefix { get; init; } = "hpd:gateway:admission";
    public int Database { get; init; } = -1;
    public int OperationTimeoutMilliseconds { get; init; } = 75;
    public int MaximumConcurrentInvocations { get; init; } = 1_024;
}

internal sealed record GatewayStandaloneManagement
{
    public required string DatabasePath { get; init; }
    public required string ManagementAuthorityId { get; init; }
    public required string PlanProtectionKeyHex { get; init; }
    public required string TokenProtectionKeyHex { get; init; }
    public required DateTimeOffset TokenProtectionIssueNotBeforeUtc { get; init; }
    public required string DesiredStateTokenKeyHex { get; init; }
    public required string EpochReservationKeyHex { get; init; }
    public required string JwtAuthority { get; init; }
    public required string JwtAudience { get; init; }
    public required string JwtSigningKeyHex { get; init; }
}

internal sealed record GatewayStandaloneCertificateSource
{
    public required ProviderId Provider { get; init; }
    public required ProviderObjectId Name { get; init; }
    public string? Version { get; init; }
    public required string PfxPath { get; init; }
    public string? PasswordEnvironmentVariable { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GatewayStandaloneBootstrap))]
[JsonSerializable(typeof(GatewayStandaloneManagement))]
[JsonSerializable(typeof(GatewayStandaloneRedisAdmission))]
internal partial class GatewayStandaloneJsonContext : JsonSerializerContext;

internal sealed record GatewayStandaloneInputs(
    GatewayHostCandidate Host,
    GatewayNodeActivationRequest InitialCandidate,
    ImmutableArray<(SecretReference Reference, GatewayPfxCertificateSource Source)> Certificates,
    GatewayStandaloneManagement Management,
    GatewayStandaloneRedisAdmissionInputs? RedisAdmission);

internal sealed record GatewayStandaloneRedisAdmissionInputs(
    string AuthorityId,
    string Configuration,
    string KeyPrefix,
    int Database,
    TimeSpan OperationTimeout,
    int MaximumConcurrentInvocations);

internal static class GatewayStandaloneBootstrapReader
{
    private const int MaximumBootstrapBytes = 256 * 1024;
    private const int MaximumPathUtf8Bytes = 4_096;
    private const int MaximumIdentityUtf8Bytes = 256;

    internal static GatewayStandaloneInputs Read(string bootstrapPath)
    {
        if (!Path.IsPathFullyQualified(bootstrapPath))
            throw new InvalidOperationException("The standalone bootstrap path must be absolute.");
        var bootstrapBytes = ReadBounded(bootstrapPath, MaximumBootstrapBytes, "bootstrap");
        GatewayStandaloneBootstrap bootstrap;
        try
        {
            RejectDuplicateProperties(bootstrapBytes.AsSpan());
            bootstrap = JsonSerializer.Deserialize(
                bootstrapBytes.AsSpan(),
                GatewayStandaloneJsonContext.Default.GatewayStandaloneBootstrap)
                ?? throw new InvalidOperationException("The standalone bootstrap document is empty.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new InvalidOperationException("The standalone bootstrap document is malformed or unsupported.");
        }

        Validate(bootstrap);
        var hostBytes = ReadBounded(
            bootstrap.HostConfigurationPath,
            GatewayHostCandidateReader.MaximumDocumentBytes,
            "host");
        var host = GatewayHostCandidateReader.Read(hostBytes.AsSpan());
        if (!host.IsAccepted)
            throw new InvalidOperationException("The standalone host configuration was rejected.");
        var gatewayBytes = ReadBounded(
            bootstrap.GatewayConfigurationPath,
            GatewayJson.MaximumDocumentBytes,
            "gateway");
        var certificates = bootstrap.Certificates.Select(static certificate =>
        {
            var password = certificate.PasswordEnvironmentVariable is null
                ? null
                : Environment.GetEnvironmentVariable(certificate.PasswordEnvironmentVariable)
                    ?? throw new InvalidOperationException("A certificate password environment variable is unavailable.");
            return (
                new SecretReference(certificate.Provider, certificate.Name, certificate.Version),
                new GatewayPfxCertificateSource { Path = certificate.PfxPath, Password = password });
        }).ToImmutableArray();
        GatewayStandaloneRedisAdmissionInputs? redisAdmission = bootstrap.RedisAdmission is null
            ? null
            : new(
                bootstrap.RedisAdmission.AuthorityId,
                Environment.GetEnvironmentVariable(bootstrap.RedisAdmission.ConfigurationEnvironmentVariable)
                    ?? throw new InvalidOperationException("The standalone Redis admission configuration environment variable is unavailable."),
                bootstrap.RedisAdmission.KeyPrefix,
                bootstrap.RedisAdmission.Database,
                TimeSpan.FromMilliseconds(bootstrap.RedisAdmission.OperationTimeoutMilliseconds),
                bootstrap.RedisAdmission.MaximumConcurrentInvocations);
        return new GatewayStandaloneInputs(
            host.Candidate!,
            new GatewayNodeActivationRequest(
                bootstrap.NamespaceId,
                bootstrap.TargetNodeId,
                bootstrap.CandidateId,
                bootstrap.AuthorityId,
                bootstrap.AuthorityEpoch,
                bootstrap.AuthorityVersion,
                gatewayBytes),
            certificates,
            bootstrap.Management,
            redisAdmission);
    }

    private static ImmutableArray<byte> ReadBounded(string path, int maximumBytes, string kind)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
            throw new InvalidOperationException($"The standalone {kind} path is invalid.");
        var length = new FileInfo(path).Length;
        if (length is <= 0 || length > maximumBytes)
            throw new InvalidOperationException($"The standalone {kind} document exceeds its byte bound.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length is <= 0 || bytes.Length > maximumBytes)
            throw new InvalidOperationException($"The standalone {kind} document exceeds its byte bound.");
        return ImmutableArray.CreateRange(bytes);
    }

    private static void Validate(GatewayStandaloneBootstrap bootstrap)
    {
        if (!StringComparer.Ordinal.Equals(bootstrap.SchemaVersion, "hpd.gateway.standalone/v2"))
            throw new InvalidOperationException("The standalone bootstrap schema is unsupported.");
        ValidatePath(bootstrap.HostConfigurationPath);
        ValidatePath(bootstrap.GatewayConfigurationPath);
        if (!GatewayIdentifier.IsCanonical(bootstrap.CandidateId.Value) ||
            !BoundedIdentity(bootstrap.NamespaceId) ||
            !BoundedIdentity(bootstrap.TargetNodeId) ||
            !BoundedIdentity(bootstrap.AuthorityId) ||
            !BoundedIdentity(bootstrap.AuthorityEpoch) ||
            bootstrap.AuthorityVersion == 0)
            throw new InvalidOperationException("The standalone activation identity is invalid.");
        if (bootstrap.Certificates.IsDefault || bootstrap.Certificates.Length is < 1 or > 1_024)
            throw new InvalidOperationException("The standalone certificate catalog is uninitialized or outside its bound.");
        ValidateManagement(bootstrap.Management);
        ValidateRedisAdmission(bootstrap.RedisAdmission);
        var references = new HashSet<SecretReference>();
        foreach (var certificate in bootstrap.Certificates)
        {
            if (certificate is null || !GatewayIdentifier.IsCanonical(certificate.Provider.Value) ||
                !GatewayIdentifier.IsCanonical(certificate.Name.Value) ||
                certificate.Version is not null && !GatewayIdentifier.IsCanonical(certificate.Version) ||
                !references.Add(new(certificate.Provider, certificate.Name, certificate.Version)))
                throw new InvalidOperationException("The standalone certificate references are invalid or duplicated.");
            ValidatePath(certificate.PfxPath);
            if (certificate.PasswordEnvironmentVariable is not null &&
                !IsEnvironmentVariableName(certificate.PasswordEnvironmentVariable))
                throw new InvalidOperationException("A certificate password environment-variable name is invalid.");
        }
    }

    private static void ValidateRedisAdmission(GatewayStandaloneRedisAdmission? admission)
    {
        if (admission is null) return;
        if (!GatewayIdentifier.IsCanonical(admission.AuthorityId) ||
            !IsEnvironmentVariableName(admission.ConfigurationEnvironmentVariable) ||
            string.IsNullOrWhiteSpace(admission.KeyPrefix) || admission.KeyPrefix.Length > 128 ||
            admission.KeyPrefix.Any(static value => value is < '!' or > '~' or '{' or '}') ||
            admission.Database is < -1 or > 15 ||
            admission.OperationTimeoutMilliseconds is < 1 or > 30_000 ||
            admission.MaximumConcurrentInvocations is < 1 or > 4_096)
            throw new InvalidOperationException("The standalone Redis admission configuration is invalid or outside its bound.");
    }

    private static void ValidateManagement(GatewayStandaloneManagement? management)
    {
        if (management is null)
            throw new InvalidOperationException("The standalone management configuration is required.");
        ValidatePath(management.DatabasePath);
        if (!GatewayIdentifier.IsCanonical(management.ManagementAuthorityId) ||
            !Uri.TryCreate(management.JwtAuthority, UriKind.Absolute, out Uri? authority) || authority.Scheme != Uri.UriSchemeHttps ||
            !BoundedIdentity(management.JwtAudience))
            throw new InvalidOperationException("The standalone management identity is invalid.");
        ValidateKey(management.PlanProtectionKeyHex);
        ValidateKey(management.TokenProtectionKeyHex);
        if (management.TokenProtectionIssueNotBeforeUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("The standalone token-protection issue time must be UTC.");
        ValidateKey(management.DesiredStateTokenKeyHex);
        ValidateKey(management.EpochReservationKeyHex);
        ValidateKey(management.JwtSigningKeyHex);
    }

    private static void ValidateKey(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Standalone management protection keys must be 32-byte hexadecimal values.");
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = GatewayJson.MaximumDepth,
        });
        var propertySets = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                propertySets.Push(new(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.EndObject)
                propertySets.Pop();
            else if (reader.TokenType == JsonTokenType.PropertyName &&
                     !propertySets.Peek().Add(reader.GetString()!))
                throw new JsonException("Duplicate JSON properties are not permitted.");
        }
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || Encoding.UTF8.GetByteCount(path) > MaximumPathUtf8Bytes ||
            path.Any(char.IsControl))
            throw new InvalidOperationException("A standalone path is invalid or exceeds its bound.");
    }

    private static bool BoundedIdentity(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Any(char.IsControl) &&
        Encoding.UTF8.GetByteCount(value) <= MaximumIdentityUtf8Bytes;

    private static bool IsEnvironmentVariableName(string value)
    {
        if (value.Length is < 1 or > 128 ||
            value[0] is not ('_' or >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
            return false;
        return value.Skip(1).All(static character =>
            character is '_' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9');
    }
}
