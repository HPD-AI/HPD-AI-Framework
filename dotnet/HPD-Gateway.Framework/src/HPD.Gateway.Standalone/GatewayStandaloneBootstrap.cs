using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Hosting;

namespace HPD.Gateway.Standalone;

internal sealed record GatewayStandaloneBootstrap
{
    public required string SchemaVersion { get; init; }
    public required string HostConfigurationPath { get; init; }
    public required string GatewayConfigurationPath { get; init; }
    public required CandidateId CandidateId { get; init; }
    public required string AuthorityId { get; init; }
    public required string AuthorityEpoch { get; init; }
    public required ulong AuthorityVersion { get; init; }
    public ImmutableArray<GatewayStandaloneCertificateSource> Certificates { get; init; } = [];
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
internal partial class GatewayStandaloneJsonContext : JsonSerializerContext;

internal sealed record GatewayStandaloneInputs(
    GatewayHostCandidate Host,
    GatewayNodeActivationRequest InitialCandidate,
    ImmutableArray<(SecretReference Reference, GatewayPfxCertificateSource Source)> Certificates);

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
        return new GatewayStandaloneInputs(
            host.Candidate!,
            new GatewayNodeActivationRequest(
                bootstrap.CandidateId,
                bootstrap.AuthorityId,
                bootstrap.AuthorityEpoch,
                bootstrap.AuthorityVersion,
                gatewayBytes),
            certificates);
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
        if (!StringComparer.Ordinal.Equals(bootstrap.SchemaVersion, "hpd.gateway.standalone/v1"))
            throw new InvalidOperationException("The standalone bootstrap schema is unsupported.");
        ValidatePath(bootstrap.HostConfigurationPath);
        ValidatePath(bootstrap.GatewayConfigurationPath);
        if (!GatewayIdentifier.IsCanonical(bootstrap.CandidateId.Value) ||
            !BoundedIdentity(bootstrap.AuthorityId) ||
            !BoundedIdentity(bootstrap.AuthorityEpoch) ||
            bootstrap.AuthorityVersion == 0)
            throw new InvalidOperationException("The standalone activation identity is invalid.");
        if (bootstrap.Certificates.IsDefault || bootstrap.Certificates.Length is < 1 or > 1_024)
            throw new InvalidOperationException("The standalone certificate catalog is uninitialized or outside its bound.");
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
