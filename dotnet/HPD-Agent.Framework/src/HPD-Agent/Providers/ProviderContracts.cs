using System;
using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers;

/// <summary>
/// Base contract for provider packages. Providers describe a vendor, platform,
/// or capability package and opt into individual client families through
/// provider-family interfaces.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Unique identifier for this provider (for example, "openai", "anthropic", "silero").
    /// Must be lowercase and URL-safe (used in JSON config).
    /// </summary>
    string ProviderKey { get; }

    /// <summary>
    /// Display name for UI purposes (e.g., "OpenAI", "Anthropic Claude").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Create an error handler for this provider.
    /// </summary>
    IProviderErrorHandler CreateErrorHandler();

    /// <summary>
    /// Get metadata about this provider's capabilities.
    /// </summary>
    ProviderMetadata GetMetadata();

    /// <summary>
    /// Validate provider-specific configuration for a specific client family.
    /// </summary>
    ProviderValidationResult ValidateConfiguration(
        EffectiveProviderClientConfig config);

    /// <summary>
    /// Validate provider-specific configuration asynchronously with live API testing.
    /// Providers that don't support async validation should return null.
    /// </summary>
    Task<ProviderValidationResult>? ValidateConfigurationAsync(
        EffectiveProviderClientConfig config,
        CancellationToken cancellationToken = default)
        => null;
}

/// <summary>Determines how a provider client receives renewable credentials.</summary>
public enum ProviderClientCredentialBinding
{
    /// <summary>The client acquires the current credential for each request.</summary>
    RequestTime,
    /// <summary>The client captures one credential generation during construction.</summary>
    ConstructionTime
}

/// <summary>Describes the immutable inputs used to select credential binding.</summary>
public sealed record ProviderClientBindingDescriptor
{
    /// <summary>Gets the effective provider-client configuration.</summary>
    public required EffectiveProviderClientConfig EffectiveConfig { get; init; }
}

/// <summary>Contains a provider-created client and its single authoritative lifetime owner.</summary>
public sealed record ProviderClientConstruction<TClient> where TClient : class
{
    /// <summary>Gets the provider client.</summary>
    public required TClient Client { get; init; }
    /// <summary>Gets the owner of the client, transport, handlers, and retained credentials.</summary>
    public required IAsyncDisposable Owner { get; init; }
}

/// <summary>Exposes the credential binding selected before cache lookup.</summary>
public abstract record ProviderCredentialBindingContext
{
    private ProviderCredentialBindingContext() { }

    /// <summary>Supplies a stable plan and source for request-time acquisition.</summary>
    public sealed record RequestTime(
        IProviderCredentialSource Source,
        ProviderCredentialPlan Plan) : ProviderCredentialBindingContext;

    /// <summary>Supplies the exact lease retained by a construction-bound client.</summary>
    public sealed record ConstructionTime(
        ProviderCredentialPlan Plan,
        IProviderCredentialLease Lease) : ProviderCredentialBindingContext;
}

/// <summary>A capability-shaped service facade available during provider construction.</summary>
public interface IProviderRuntimeServices
{
    /// <summary>Gets the logger factory.</summary>
    Microsoft.Extensions.Logging.ILoggerFactory LoggerFactory { get; }
    /// <summary>Gets the HTTP client factory.</summary>
    System.Net.Http.IHttpClientFactory HttpClientFactory { get; }
    /// <summary>Gets the injected time provider.</summary>
    TimeProvider TimeProvider { get; }
    /// <summary>Gets the provider telemetry sink.</summary>
    IProviderTelemetry Telemetry { get; }
}

/// <summary>Receives non-secret provider authentication and construction telemetry.</summary>
public interface IProviderTelemetry;

/// <summary>Contains all immutable inputs for one provider-client construction.</summary>
public sealed record ProviderClientConstructionContext
{
    /// <summary>Gets the effective configuration.</summary>
    public required EffectiveProviderClientConfig EffectiveConfig { get; init; }
    /// <summary>Gets the immutable authorization scope.</summary>
    public required ProviderAuthorizationScopeSnapshot AuthorizationScope { get; init; }
    /// <summary>Gets the normalized grant.</summary>
    public required ProviderAuthorizationGrantSnapshot Grant { get; init; }
    /// <summary>Gets the resolved credential binding.</summary>
    public required ProviderCredentialBindingContext CredentialBinding { get; init; }
    /// <summary>Gets the component lifetime context.</summary>
    public required ProviderComponentLifetimeContext Lifetime { get; init; }
    /// <summary>Gets the narrow runtime-services facade.</summary>
    public required IProviderRuntimeServices Services { get; init; }
}

/// <summary>Creates one provider client family using the uniform asynchronous contract.</summary>
public interface IProviderClientFactory<TClient> where TClient : class
{
    /// <summary>Resolves the protected credential audience for this backend and client family.</summary>
    /// <remarks>
    /// The default maps the configured endpoint and authentication scopes. A backend whose
    /// credential resource is protocol-owned rather than user-configured must override it.
    /// </remarks>
    ProviderCredentialAudience ResolveCredentialAudience(
        ProviderClientBindingDescriptor descriptor) => new()
        {
            Resource = descriptor.EffectiveConfig.Endpoint,
            Scopes = descriptor.EffectiveConfig.Provider.Authentication.Scopes
        };

    /// <summary>Resolves credential binding without side effects before cache lookup.</summary>
    ProviderClientCredentialBinding ResolveCredentialBinding(
        ProviderClientBindingDescriptor descriptor);

    /// <summary>Creates the client and its authoritative owner.</summary>
    ValueTask<ProviderClientConstruction<TClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies a provider-created client or operation family.</summary>
public enum ProviderClientFamily
{
    /// <summary>Chat completion or response generation.</summary>
    Chat,
    /// <summary>Text-to-speech synthesis.</summary>
    TextToSpeech,
    /// <summary>Speech-to-text transcription.</summary>
    SpeechToText,
    /// <summary>Bidirectional realtime interaction.</summary>
    Realtime,
    /// <summary>Image generation.</summary>
    ImageGeneration,
    /// <summary>Embedding generation.</summary>
    Embeddings,
    /// <summary>Provider-hosted file operations.</summary>
    HostedFiles,
    /// <summary>Voice-activity detection.</summary>
    VoiceActivityDetection,
    /// <summary>Semantic end-of-turn detection.</summary>
    EndOfTurnDetection
}

/// <summary>Defines the ownership scope required by a provider client family.</summary>
public enum ProviderFamilyLifetime
{
    /// <summary>One client can be safely reused across independent runs.</summary>
    ReusableClient,
    /// <summary>One client is owned by a single audio session.</summary>
    StatefulPerAudioSession,
    /// <summary>One client is owned by a single agent run.</summary>
    StatefulPerRun,
    /// <summary>One client is owned by a single model turn.</summary>
    StatefulPerTurn
}

/// <summary>Identifies the concrete runtime scope that owns a provider component.</summary>
/// <param name="AgentId">The owning agent identity, when applicable.</param>
/// <param name="SessionId">The owning session identity, when applicable.</param>
/// <param name="ThreadId">The owning thread identity, when applicable.</param>
/// <param name="ThreadExecutionId">The owning thread-execution identity, when applicable.</param>
/// <param name="AudioSessionId">The owning audio-session identity, when applicable.</param>
/// <param name="Lifetime">The required provider-family lifetime.</param>
public sealed record ProviderComponentLifetimeContext(
    string? AgentId = null,
    string? SessionId = null,
    string? ThreadId = null,
    string? ThreadExecutionId = null,
    string? AudioSessionId = null,
    ProviderFamilyLifetime Lifetime = ProviderFamilyLifetime.ReusableClient);

/// <summary>
/// Metadata about a provider's capabilities.
/// </summary>
public sealed class ProviderFamilyDescriptor
{
    /// <summary>Gets the provider client family.</summary>
    public ProviderClientFamily Family { get; init; }
    /// <summary>Gets the runtime ownership scope required by the family.</summary>
    public ProviderFamilyLifetime Lifetime { get; init; } = ProviderFamilyLifetime.ReusableClient;
    /// <summary>Gets whether the provider binds the model while constructing this client family.</summary>
    public bool BindsModelToClient { get; init; } = true;
    /// <summary>Gets the closed model identifiers advertised by the provider, when available.</summary>
    public IReadOnlyList<string>? SupportedModels { get; init; }
    /// <summary>Gets the provider's default model identifier, when available.</summary>
    public string? DefaultModelId { get; init; }
    /// <summary>Gets provider-specific redacted capability metadata.</summary>
    public IReadOnlyDictionary<string, object?>? Capabilities { get; init; }
}

/// <summary>Describes one authentication mechanism supported by a provider backend.</summary>
public sealed record ProviderAuthenticationDescriptor
{
    /// <summary>Gets the authentication kind.</summary>
    public required ProviderAuthenticationKind Kind { get; init; }

    /// <summary>Gets whether this is the unambiguous backend default.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Gets whether acquisition can require host interaction.</summary>
    public bool IsInteractive { get; init; }

    /// <summary>Gets whether renewable credentials are supported.</summary>
    public bool SupportsRefresh { get; init; }

    /// <summary>Gets the default resolver key for API-key authentication.</summary>
    public string? DefaultSecretKey { get; init; }

    /// <summary>Gets the default normalized scopes.</summary>
    public IReadOnlyList<string>? DefaultScopes { get; init; }

    /// <summary>Gets the supported client families.</summary>
    public required IReadOnlySet<ProviderClientFamily> SupportedFamilies { get; init; }
}

/// <summary>Describes one concrete API surface exposed by a provider.</summary>
public sealed record ProviderBackendDescriptor
{
    /// <summary>Gets the canonical backend key.</summary>
    public required string BackendKey { get; init; }

    /// <summary>Gets whether this is the provider's unambiguous default backend.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Gets the family descriptors supported by this backend.</summary>
    public required IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; init; }

    /// <summary>Gets the supported authentication mechanisms.</summary>
    public required IReadOnlyList<ProviderAuthenticationDescriptor> Authentication { get; init; }
}

public class ProviderMetadata
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Uri? DocumentationUri { get; init; }
    public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; init; }
        = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();
}

/// <summary>
/// Result of provider configuration validation.
/// </summary>
public class ProviderValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public static ProviderValidationResult Success() => new() { IsValid = true };

    public static ProviderValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = new List<string>(errors) };
}
