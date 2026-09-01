using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers;

/// <summary>Identifies a concrete API surface exposed by a provider package.</summary>
public readonly record struct ProviderBackendIdentity
{
    /// <summary>Initializes a provider/backend identity.</summary>
    /// <param name="providerKey">The canonical provider package identity.</param>
    /// <param name="backendKey">The canonical backend identity.</param>
    public ProviderBackendIdentity(string providerKey, string backendKey)
    {
        ProviderKey = providerKey;
        BackendKey = backendKey;
    }

    /// <summary>Gets the canonical provider package identity.</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the canonical backend identity.</summary>
    public string BackendKey { get; }
}

/// <summary>The closed set of provider authentication mechanisms understood by HPD.</summary>
public enum ProviderAuthenticationKind
{
    /// <summary>A static secret used as an API key.</summary>
    ApiKey,
    /// <summary>A renewable provider authorization session.</summary>
    OAuth,
    /// <summary>A host-registered SDK-native identity.</summary>
    ExternalIdentity,
    /// <summary>No credential is supplied.</summary>
    Anonymous
}

/// <summary>
/// Portable provider-authentication configuration. Credential material is never part of this
/// serialized union.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ApiKeyProviderAuthentication), "api-key")]
[JsonDerivedType(typeof(OAuthProviderAuthentication), "oauth")]
[JsonDerivedType(typeof(ExternalIdentityProviderAuthentication), "external-identity")]
[JsonDerivedType(typeof(AnonymousProviderAuthentication), "anonymous")]
public abstract record ProviderAuthentication;

/// <summary>Resolves an API key through the host's static secret infrastructure.</summary>
public sealed record ApiKeyProviderAuthentication : ProviderAuthentication
{
    /// <summary>Gets the canonical key passed to the host secret resolver.</summary>
    public required string SecretKey { get; init; }
}

/// <summary>Selects a durable provider authorization account.</summary>
public sealed record OAuthProviderAuthentication : ProviderAuthentication
{
    /// <summary>Gets the portable host-defined account label.</summary>
    public required string AccountId { get; init; }

    /// <summary>Gets the requested provider scopes, or <see langword="null"/> for provider defaults.</summary>
    public IReadOnlyList<string>? Scopes { get; init; }

    /// <summary>Gets the optional provider authorization-profile reference.</summary>
    public string? AuthorizationProfile { get; init; }

    /// <summary>Gets the optional host authorization-store registration key.</summary>
    public string? StoreKey { get; init; }
}

/// <summary>Selects a host-registered SDK-native credential.</summary>
public sealed record ExternalIdentityProviderAuthentication : ProviderAuthentication
{
    /// <summary>Gets the name of the host-registered external identity.</summary>
    public required string CredentialName { get; init; }
}

/// <summary>Selects provider-declared anonymous access.</summary>
public sealed record AnonymousProviderAuthentication : ProviderAuthentication;

/// <summary>
/// Selects an HPD-owned process-local API-key registration. This type is deliberately absent
/// from the JSON derived-type table and cannot be used as portable configuration.
/// </summary>
public sealed record ExplicitApiKeyProviderAuthentication : ProviderAuthentication
{
    /// <summary>Gets the opaque process-local registration name; it is never serialized.</summary>
    [JsonIgnore]
    public required string RuntimeRegistrationName { get; init; }
}

/// <summary>A portable authoring reference to a provider, backend, and authentication mechanism.</summary>
public sealed record ProviderReference
{
    /// <summary>Gets the provider package identity.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the backend identity, or <see langword="null"/> until default resolution.</summary>
    public string? Backend { get; init; }

    /// <summary>Gets the authentication selection, or <see langword="null"/> until default resolution.</summary>
    public ProviderAuthentication? Authentication { get; init; }
}

/// <summary>A complete immutable authentication selection published after resolution.</summary>
public sealed record EffectiveProviderAuthentication
{
    /// <summary>Gets the owned immutable authentication configuration used for acquisition.</summary>
    public required ProviderAuthentication Configuration { get; init; }

    /// <summary>Gets the resolved authentication mechanism.</summary>
    public required ProviderAuthenticationKind Kind { get; init; }

    /// <summary>Gets the stable, non-secret identity of the selected authentication reference.</summary>
    public required string StableReferenceIdentity { get; init; }

    /// <summary>Gets the canonical immutable scope set.</summary>
    public required ImmutableArray<string> Scopes { get; init; }

    /// <summary>Gets the resolved authorization-profile identity.</summary>
    public string? AuthorizationProfile { get; init; }

    /// <summary>Gets the resolved authorization-store identity.</summary>
    public string? AuthorizationStoreIdentity { get; init; }
}

/// <summary>A complete immutable provider selection published after reference resolution.</summary>
public sealed record ResolvedProviderSelection
{
    /// <summary>Gets the complete canonical provider/backend identity.</summary>
    public required ProviderBackendIdentity Backend { get; init; }

    /// <summary>Gets the complete effective authentication selection.</summary>
    public required EffectiveProviderAuthentication Authentication { get; init; }
}

/// <summary>Identifies the configuration layer that supplied an effective value.</summary>
public enum ProviderConfigurationLayer
{
    /// <summary>A provider/backend profile baseline.</summary>
    Profile,
    /// <summary>The durable agent-family configuration.</summary>
    Agent,
    /// <summary>The portable per-run configuration.</summary>
    Run,
    /// <summary>A process-local builder registration.</summary>
    BuilderRuntime
}

/// <summary>Contains one canonical immutable provider-owned payload.</summary>
public sealed record ProviderPayloadSnapshot
{
    /// <summary>Gets the generated payload contract identity.</summary>
    public required string ContractId { get; init; }

    /// <summary>Gets the canonical serialized payload bytes.</summary>
    public required ImmutableArray<byte> CanonicalPayload { get; init; }

    /// <summary>Gets the stable non-secret payload fingerprint.</summary>
    public required string Fingerprint { get; init; }
}

/// <summary>Records the winning configuration layer for every effective field.</summary>
public sealed record ProviderConfigurationProvenance
{
    /// <summary>Gets the immutable field-to-layer mapping.</summary>
    public required ImmutableDictionary<string, ProviderConfigurationLayer> Fields { get; init; }
}

/// <summary>
/// Deeply immutable provider-client configuration published before credential preparation or
/// cache lookup.
/// </summary>
public sealed record EffectiveProviderClientConfig
{
    /// <summary>Gets the complete provider/backend/authentication selection.</summary>
    public required ResolvedProviderSelection Provider { get; init; }

    /// <summary>Gets the requested provider client family.</summary>
    public required ProviderClientFamily Family { get; init; }

    /// <summary>Gets the backend-specific model identifier.</summary>
    public string? ModelName { get; init; }

    /// <summary>Gets the normalized endpoint override.</summary>
    public Uri? Endpoint { get; init; }

    /// <summary>Gets copied non-credential custom headers.</summary>
    public required ImmutableDictionary<string, string> CustomHeaders { get; init; }

    /// <summary>Gets the immutable provider construction payload.</summary>
    public required ProviderPayloadSnapshot ProviderConfiguration { get; init; }

    /// <summary>Gets the immutable family-operation payload.</summary>
    public required ProviderPayloadSnapshot FamilyOperation { get; init; }

    /// <summary>Gets immutable portable defaults specific to the selected client family.</summary>
    public required ProviderFamilyDefaultsSnapshot FamilyDefaults { get; init; }

    /// <summary>Gets effective-field provenance.</summary>
    public required ProviderConfigurationProvenance Provenance { get; init; }

    /// <summary>Gets the provider manifest revision used during resolution.</summary>
    public required string ProviderManifestRevision { get; init; }

    /// <summary>Gets the complete safe construction fingerprint.</summary>
    public required string ConstructionFingerprint { get; init; }
}

/// <summary>Safe immutable identity of the client that actually executes a provider operation.</summary>
public sealed record ProviderClientExecutionIdentity
{
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the canonical backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the selected client family.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the selected model where the family uses one.</summary>
    public string? ModelName { get; init; }
    /// <summary>Gets the generated or adapter-declared operation identity.</summary>
    public required string OperationAdapterKey { get; init; }
    /// <summary>Gets the adapter identity governing usage semantics.</summary>
    public required string UsageSemanticsKey { get; init; }
    /// <summary>Gets a stable fingerprint containing no credential or authorization material.</summary>
    public required string SafeConfigurationFingerprint { get; init; }

    internal static ProviderClientExecutionIdentity CreateSafe(
        string providerKey,
        string backendKey,
        ProviderClientFamily family,
        string? modelName,
        string operationAdapterKey,
        string usageSemanticsKey)
    {
        var canonical = string.Join('\n',
            providerKey.Trim().ToLowerInvariant(),
            backendKey.Trim().ToLowerInvariant(),
            ((int)family).ToString(System.Globalization.CultureInfo.InvariantCulture),
            modelName?.Trim() ?? string.Empty,
            operationAdapterKey.Trim(),
            usageSemanticsKey.Trim());
        return new ProviderClientExecutionIdentity
        {
            ProviderKey = providerKey,
            BackendKey = backendKey,
            Family = family,
            ModelName = modelName,
            OperationAdapterKey = operationAdapterKey,
            UsageSemanticsKey = usageSemanticsKey,
            SafeConfigurationFingerprint = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
        };
    }
}

/// <summary>Deeply immutable portable defaults shared by the nine provider-family factories.</summary>
public sealed record ProviderFamilyDefaultsSnapshot
{
    /// <summary>Gets chat temperature.</summary>
    public double? Temperature { get; init; }
    /// <summary>Gets chat nucleus-sampling probability.</summary>
    public double? TopP { get; init; }
    /// <summary>Gets chat top-k sampling count.</summary>
    public int? TopK { get; init; }
    /// <summary>Gets chat frequency penalty.</summary>
    public double? FrequencyPenalty { get; init; }
    /// <summary>Gets chat presence penalty.</summary>
    public double? PresencePenalty { get; init; }
    /// <summary>Gets the deterministic chat seed.</summary>
    public long? Seed { get; init; }
    /// <summary>Gets immutable chat stop sequences.</summary>
    public required ImmutableArray<string> StopSequences { get; init; }
    /// <summary>Gets the requested reasoning effort.</summary>
    public ReasoningEffort? ReasoningEffort { get; init; }
    /// <summary>Gets the requested reasoning output projection.</summary>
    public ReasoningOutput? ReasoningOutput { get; init; }
    /// <summary>Gets the voice identifier.</summary>
    public string? VoiceId { get; init; }
    /// <summary>Gets the language or spoken-language identifier.</summary>
    public string? Language { get; init; }
    /// <summary>Gets the speech input language without collapsing it with text output language.</summary>
    public string? SpeechLanguage { get; init; }
    /// <summary>Gets the recognized text output language.</summary>
    public string? TextLanguage { get; init; }
    /// <summary>Gets the output audio or media format.</summary>
    public string? MediaType { get; init; }
    /// <summary>Gets the explicit audio-format identifier.</summary>
    public string? AudioFormat { get; init; }
    /// <summary>Gets speech speed.</summary>
    public float? Speed { get; init; }
    /// <summary>Gets speech pitch.</summary>
    public float? Pitch { get; init; }
    /// <summary>Gets speech volume.</summary>
    public float? Volume { get; init; }
    /// <summary>Gets a sample rate in hertz.</summary>
    public int? SampleRate { get; init; }
    /// <summary>Gets embedding dimensions.</summary>
    public int? Dimensions { get; init; }
    /// <summary>Gets an image or result count.</summary>
    public int? Count { get; init; }
    /// <summary>Gets image width.</summary>
    public int? Width { get; init; }
    /// <summary>Gets image height.</summary>
    public int? Height { get; init; }
    /// <summary>Gets a maximum output-token count.</summary>
    public int? MaxOutputTokens { get; init; }
    /// <summary>Gets immutable output modality names.</summary>
    public required ImmutableArray<string> OutputModalities { get; init; }
    /// <summary>Gets the realtime transcription model.</summary>
    public string? TranscriptionModel { get; init; }
    /// <summary>Gets the realtime transcription language.</summary>
    public string? TranscriptionLanguage { get; init; }
    /// <summary>Gets the realtime transcription prompt.</summary>
    public string? TranscriptionPrompt { get; init; }
    /// <summary>Gets the requested streaming image count.</summary>
    public int? StreamingCount { get; init; }
    /// <summary>Gets hosted-file scope.</summary>
    public string? Scope { get; init; }
    /// <summary>Gets hosted-file purpose.</summary>
    public string? Purpose { get; init; }
    /// <summary>Gets a hosted-file result limit.</summary>
    public int? Limit { get; init; }
}

/// <summary>A provider/backend profile serialized with explicit identity fields.</summary>
public sealed record AgentProviderBackendProfile
{
    /// <summary>Gets the provider package identity owned by this profile.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Gets the provider backend identity owned by this profile.</summary>
    public required string BackendKey { get; init; }

    /// <summary>Gets the family configuration baselines for this provider/backend.</summary>
    public required AgentClientsConfig Clients { get; init; }
}

/// <summary>The explicit provider/backend default for one client family.</summary>
public sealed record AgentProviderFamilyDefault
{
    /// <summary>Gets the client family receiving this default.</summary>
    public required ProviderClientFamily Family { get; init; }

    /// <summary>Gets the default provider package identity.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Gets the default backend identity.</summary>
    public required string BackendKey { get; init; }
}
