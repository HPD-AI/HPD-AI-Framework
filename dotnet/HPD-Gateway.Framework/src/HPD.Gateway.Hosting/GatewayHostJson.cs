using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Hosting;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GatewayHostConfiguration))]
internal partial class GatewayHostJsonContext : JsonSerializerContext;

public static class GatewayHostCandidateReader
{
    public const int MaximumDocumentBytes = 256 * 1024;

    public static GatewayHostCandidateResult Read(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || utf8Json.Length > MaximumDocumentBytes)
            return Failure("host.document-bound", "$", "Host document is empty or exceeds its byte bound.");
        try
        {
            var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions { MaxDepth = 32 });
            var tokens = 0;
            string? propertyName = null;
            var arrayFrames = new bool[33];
            var itemCounts = new int[33];
            var propertyNames = new HashSet<string>?[33];
            var frameDepth = 0;
            while (reader.Read())
            {
                if (++tokens > 32_768) return Failure("host.token-bound", "$", "Host document exceeds its token bound.");
                var length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && length > 4_096)
                    return Failure("host.string-bound", "$", "Host document contains an oversized string.");
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    propertyName = reader.GetString();
                    if (frameDepth == 0 || propertyName is null || !propertyNames[frameDepth - 1]!.Add(propertyName))
                        return Failure("host.duplicate-property", "$", "Host document contains a duplicate JSON property.");
                    if (++itemCounts[frameDepth - 1] > 64)
                        return Failure("host.property-bound", "$", "Host object exceeds its property bound.");
                }
                else if (reader.TokenType == JsonTokenType.Number && propertyName is "binding" or "protocols" or "fallback")
                    return Failure("host.numeric-enum", "$", "Host enum values must use their supported string names.");
                if (frameDepth > 0 && arrayFrames[frameDepth - 1] && reader.TokenType != JsonTokenType.EndArray &&
                    ++itemCounts[frameDepth - 1] > 128)
                    return Failure("host.array-bound", "$", "Host array exceeds its item bound.");
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        arrayFrames[frameDepth] = false;
                        itemCounts[frameDepth] = 0;
                        propertyNames[frameDepth] = new(StringComparer.Ordinal);
                        frameDepth++;
                        propertyName = null;
                        break;
                    case JsonTokenType.StartArray:
                        arrayFrames[frameDepth] = true;
                        itemCounts[frameDepth] = 0;
                        propertyNames[frameDepth] = null;
                        frameDepth++;
                        propertyName = null;
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        frameDepth--;
                        propertyName = null;
                        break;
                    case not JsonTokenType.PropertyName:
                        propertyName = null;
                        break;
                }
            }
            var configuration = JsonSerializer.Deserialize(utf8Json, GatewayHostJsonContext.Default.GatewayHostConfiguration);
            return configuration is null ? Failure("host.missing", "$", "Host document produced no value.") : Create(configuration);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return Failure("host.invalid-json", "$", "Host JSON is malformed, unknown, or outside the supported contract.");
        }
    }

    public static GatewayHostCandidateResult Create(GatewayHostConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = ImmutableArray.CreateBuilder<GatewayHostValidationError>();
        if (configuration.SchemaVersion != new GatewayHostSchemaVersion(1, 0)) Add(errors, "host.unsupported-schema", "schemaVersion", "Only host schema 1.0 is supported.");
        if (configuration.CanonicalizationVersion != 1) Add(errors, "host.unsupported-canonicalization", "canonicalizationVersion", "Only host canonicalization version 1 is supported.");
        if (!GatewayIdentifier.IsCanonical(configuration.HostId.Value)) Add(errors, "host.invalid-id", "hostId", "Host ID is not canonical.");
        if (configuration.DataListeners.IsDefault || configuration.DataListeners.Length is < 1 or > 16)
            Add(errors, "host.listener-bound", "dataListeners", "One to sixteen data listeners are required.");

        var listeners = ImmutableArray.CreateBuilder<GatewayHttpsListenerDeclaration>();
        var listenerIds = new HashSet<string>(StringComparer.Ordinal);
        var listenerPorts = new HashSet<ushort>();
        foreach (var (listener, index) in configuration.DataListeners.IsDefault ? [] : configuration.DataListeners.Select((value, index) => (value, index)))
        {
            var path = $"dataListeners[{index}]";
            if (listener is null) { Add(errors, "host.listener-required", path, "Listener is required."); continue; }
            if (!GatewayIdentifier.IsCanonical(listener.Id.Value) || !listenerIds.Add(listener.Id.Value)) Add(errors, "host.invalid-listener-id", $"{path}.id", "Listener ID must be canonical and unique.");
            if (!Enum.IsDefined(listener.Binding)) Add(errors, "host.invalid-binding", $"{path}.binding", "Listener binding is unsupported.");
            if (listener.Port == 0) Add(errors, "host.invalid-port", $"{path}.port", "Listener port must be nonzero.");
            else if (!listenerPorts.Add(listener.Port)) Add(errors, "host.listener-conflict", $"{path}.port", "Listener ports must be unique in this host profile.");
            if (listener.Protocols is GatewayListenerProtocols.Http1 or GatewayListenerProtocols.Http2 or (GatewayListenerProtocols.Http1 | GatewayListenerProtocols.Http2)) { }
            else Add(errors, "host.invalid-protocols", $"{path}.protocols", "Only HTTP/1, HTTP/2, or HTTP/1+2 is supported.");
            string? normalizedAddress = null;
            if (listener.Binding == GatewayListenerBindingKind.IpAddress)
            {
                if (!IPAddress.TryParse(listener.IpAddress, out var address)) Add(errors, "host.invalid-address", $"{path}.ipAddress", "Explicit IP binding requires a valid IP literal.");
                else normalizedAddress = address.ToString();
            }
            else if (listener.IpAddress is not null) Add(errors, "host.unexpected-address", $"{path}.ipAddress", "Only explicit-IP binding accepts an address.");
            if (listener.Tls is null) { Add(errors, "host.tls-required", $"{path}.tls", "No-fallback TLS configuration is required."); continue; }
            if (!Enum.IsDefined(listener.Tls.Fallback) || listener.Tls.Fallback != InboundTlsFallback.RejectUnmatchedOrMissingSni)
                Add(errors, "host.invalid-fallback", $"{path}.tls.fallback", "Only reject-unmatched-or-missing-SNI mode is supported.");
            if (listener.Tls.Sni.IsDefault || listener.Tls.Sni.Length is < 1 or > 64)
                Add(errors, "host.sni-bound", $"{path}.tls.sni", "One to sixty-four SNI entries are required.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            var sni = ImmutableArray.CreateBuilder<GatewaySniTlsDeclaration>();
            foreach (var (entry, sniIndex) in listener.Tls.Sni.IsDefault ? [] : listener.Tls.Sni.Select((value, itemIndex) => (value, itemIndex)))
            {
                var sniPath = $"{path}.tls.sni[{sniIndex}]";
                if (entry is null) { Add(errors, "host.sni-required", sniPath, "SNI entry is required."); continue; }
                var normalized = NormalizeSni(entry.HostnamePattern);
                if (normalized is null || !names.Add(normalized)) Add(errors, "host.invalid-sni", $"{sniPath}.hostnamePattern", "SNI pattern must be valid, normalized, and unique.");
                if (!ValidSecret(entry.Certificate)) Add(errors, "host.invalid-certificate-reference", $"{sniPath}.certificate", "Certificate reference is invalid.");
                if (normalized is not null) sni.Add(entry with { HostnamePattern = normalized });
            }
            listeners.Add(listener with
            {
                IpAddress = normalizedAddress,
                Tls = listener.Tls with { Sni = sni.OrderBy(static item => item.HostnamePattern, StringComparer.Ordinal).ToImmutableArray() }
            });
        }
        if (errors.Count > 0) return new GatewayHostCandidateResult { Errors = errors.ToImmutable() };
        var normalizedConfiguration = configuration with { DataListeners = listeners.OrderBy(static item => item.Id.Value, StringComparer.Ordinal).ToImmutableArray() };
        var canonical = JsonSerializer.SerializeToUtf8Bytes(normalizedConfiguration, GatewayHostJsonContext.Default.GatewayHostConfiguration);
        return new GatewayHostCandidateResult
        {
            Candidate = new GatewayHostCandidate(normalizedConfiguration, ImmutableArray.Create(canonical), Convert.ToHexStringLower(SHA256.HashData(canonical))),
            Errors = []
        };
    }

    private static string? NormalizeSni(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "*" || value.Any(char.IsControl)) return null;
        var wildcard = value.StartsWith("*.", StringComparison.Ordinal);
        var name = wildcard ? value[2..] : value;
        if (name.EndsWith(".", StringComparison.Ordinal)) name = name[..^1];
        if (name.Length == 0 || name.Contains('*') || IPAddress.TryParse(name, out _)) return null;
        try { name = new IdnMapping().GetAscii(name).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
        if (name.Length > 253 || name.Split('.').Any(static label => label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-' || label.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))) return null;
        return wildcard ? $"*.{name}" : name;
    }

    private static bool ValidSecret(SecretReference? value) => value is not null && GatewayIdentifier.IsCanonical(value.Provider.Value) && GatewayIdentifier.IsCanonical(value.Name.Value) && (value.Version is null || GatewayIdentifier.IsCanonical(value.Version));
    private static void Add(ImmutableArray<GatewayHostValidationError>.Builder errors, string code, string path, string message) { if (errors.Count < 128) errors.Add(new(code, path, message)); }
    private static GatewayHostCandidateResult Failure(string code, string path, string message) => new() { Errors = [new(code, path, message)] };
}
