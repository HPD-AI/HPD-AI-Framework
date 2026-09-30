using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HPD.Agent;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Audio.VoiceActivity;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Providers.Audio.Silero;

var limits = new VoiceActivityOperationalLimitsV1(
    maximumSources: 2,
    maximumObservationHistory: 32,
    maximumCorrectionHistory: 4,
    maximumWindow: TimeSpan.FromSeconds(1),
    maximumProcessingLatency: TimeSpan.FromMilliseconds(250));
var request = new VoiceActivityRequestV1(
    VoiceActivityProfileV1.Fused,
    ActivityResponsivenessV1.Balanced,
    VoiceActivityNoiseEnvironmentV1.Variable,
    VoiceActivitySpeechContinuityV1.Natural,
    TimeSpan.FromMilliseconds(200),
    [
        new ActivitySourceRequestV1("local", ActivitySourceKindV1.LocalDetector,
            ActivitySourceRoleV1.Authoritative, required: true),
        new ActivitySourceRequestV1("provider", ActivitySourceKindV1.ProviderNative,
            ActivitySourceRoleV1.Corroborating, required: false),
    ],
    ActivityDegradationPolicyV1.AllowOptionalSources,
    limits);

var encoded = JsonSerializer.SerializeToUtf8Bytes(request, VoiceActivityJsonContextV1.Default.VoiceActivityRequestV1);
var decoded = JsonSerializer.Deserialize(encoded, VoiceActivityJsonContextV1.Default.VoiceActivityRequestV1)
    ?? throw new InvalidOperationException("The request did not roundtrip.");

if (decoded.Profile != request.Profile || decoded.Sources.Count != 2 || decoded.Sources[0].SourceKey != "local")
    throw new InvalidOperationException("The generated metadata changed immutable voice-activity intent.");
if (encoded.Length == 0)
    throw new InvalidOperationException("The generated payload is empty.");

var provider = new SmokeProvider();
var product = await CreateProductAsync(provider,
    new ProviderClientConfig { Provider = new ProviderReference { Key = provider.ProviderKey }, ModelName = "native-vad" },
    new ProviderComponentLifetimeContext(AudioSessionId: "native-audio",
        Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession));
if (product is not VoiceActivitySourceProductV1.BorrowedSynchronous)
    throw new InvalidOperationException("The typed provider product did not survive native binding.");

var sileroModel = System.Environment.GetEnvironmentVariable("HPD_SILERO_VAD_MODEL_PATH");
if (!string.IsNullOrWhiteSpace(sileroModel))
{
    using var silero = new SileroAudioProvider();
    var sileroProduct = await CreateProductAsync(silero,
        new ProviderClientConfig
        {
            Provider = new ProviderReference { Key = SileroAudioProvider.Key },
            ModelName = "silero-vad-6.2",
            ProviderConfig = new SileroVadOptions { ModelPath = sileroModel }
        },
        new ProviderComponentLifetimeContext(AudioSessionId: "native-silero",
            Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession));
    var source = ((VoiceActivitySourceProductV1.BorrowedSynchronous)sileroProduct).Source;
    var bytes = new byte[1_024];
    var graph = HPD.Agent.Authority.GraphGenerationId.Create();
    var clock = HPD.Agent.Authority.ClockDomainId.Create();
    var boot = HPD.Agent.Authority.BootId.Create();
    var soakText = System.Environment.GetEnvironmentVariable("HPD_SILERO_SOAK_WINDOWS");
    var soakWindows = string.IsNullOrWhiteSpace(soakText) ? 1 : int.Parse(soakText, System.Globalization.CultureInfo.InvariantCulture);
    if (soakWindows is < 1 or > 1_000_000)
        throw new InvalidOperationException("HPD_SILERO_SOAK_WINDOWS must be between 1 and 1000000.");
    var soakStarted = System.Diagnostics.Stopwatch.GetTimestamp();
    for (var index = 1; index <= soakWindows; index++)
    {
        var outcome = source.Observe(new VoiceActivityBorrowedWindowV1(bytes,
            new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1),
            new VoiceActivityMediaExtentV1(graph, (long)(index - 1) * 512, (long)index * 512, true),
            new HPD.Agent.Authority.MonotonicStampV1(clock, boot, (ulong)index)));
        if (outcome is not VoiceActivitySourceOutcomeV1.Observed)
            throw new InvalidOperationException("The real Silero ONNX source did not execute under NativeAOT.");
    }
    var soakElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(soakStarted);
    if (soakWindows > 1)
        Console.WriteLine($"silero-soak-windows={soakWindows} elapsed-ms={soakElapsed.TotalMilliseconds:F0} " +
            $"windows-per-second={soakWindows / soakElapsed.TotalSeconds:F0}");
    (source as IDisposable)?.Dispose();
}

Console.WriteLine("voice-activity-aot=pass");

// Binds one voice-activity provider the way ProviderFamilyClientRuntime does, without the
// provider-composition and credential plumbing this NativeAOT smoke does not exercise.
static async Task<VoiceActivitySourceProductV1> CreateProductAsync<TProvider>(
    TProvider provider,
    ProviderClientConfig configuration,
    ProviderComponentLifetimeContext lifetime)
    where TProvider : IProviderClientFactory<VoiceActivitySourceProductV1>
{
    var emptyPayload = new ProviderPayloadSnapshot
    {
        ContractId = "hpd.provider.smoke.empty.v1",
        CanonicalPayload = ImmutableArray<byte>.Empty,
        Fingerprint = "empty"
    };
    var effectiveConfig = new EffectiveProviderClientConfig
    {
        Provider = new ResolvedProviderSelection
        {
            Backend = new ProviderBackendIdentity(configuration.Provider?.Key ?? "aot-smoke", "local"),
            Authentication = new EffectiveProviderAuthentication
            {
                Configuration = new AnonymousProviderAuthentication(),
                Kind = ProviderAuthenticationKind.Anonymous,
                StableReferenceIdentity = "anonymous",
                Scopes = ImmutableArray<string>.Empty
            }
        },
        Family = ProviderClientFamily.VoiceActivityDetection,
        ModelName = configuration.ModelName,
        Endpoint = null,
        CustomHeaders = ImmutableDictionary<string, string>.Empty,
        ProviderConfiguration = configuration.ProviderConfig is SileroVadOptions sileroOptions
            ? new ProviderPayloadSnapshot
            {
                ContractId = "hpd.provider.silero.vadoptions.v1",
                CanonicalPayload = ImmutableArray.Create(
                    JsonSerializer.SerializeToUtf8Bytes(sileroOptions, SileroJsonContext.Default.SileroVadOptions)),
                Fingerprint = "silero-vad-options"
            }
            : emptyPayload,
        FamilyOperation = emptyPayload,
        FamilyDefaults = new ProviderFamilyDefaultsSnapshot { StopSequences = [], OutputModalities = [] },
        Provenance = new ProviderConfigurationProvenance
        {
            Fields = ImmutableDictionary<string, ProviderConfigurationLayer>.Empty
        },
        ProviderManifestRevision = "aot-smoke",
        ConstructionFingerprint = "aot-smoke"
    };

    var authorizationScope = new ProviderAuthorizationScopeSnapshot { TrustDomainId = "aot-smoke" };
    var grant = new ProviderAuthorizationGrantSnapshot
    {
        GrantIdentity = "anonymous",
        RequestedScopes = [],
        RequestedScopeSetIdentity = "none"
    };
    var plan = new ProviderCredentialPlan
    {
        Backend = effectiveConfig.Provider.Backend,
        Family = effectiveConfig.Family,
        AuthorizationScope = authorizationScope,
        Identity = new ProviderCredentialIdentity
        {
            ProviderKey = effectiveConfig.Provider.Backend.ProviderKey,
            BackendKey = effectiveConfig.Provider.Backend.BackendKey,
            Subject = "aot-smoke",
            TrustDomainId = "aot-smoke"
        },
        Grant = grant,
        StableCredentialIdentity = "anonymous",
        AuthorizationScopeIdentity = "aot-smoke"
    };

    var binding = provider.ResolveCredentialBinding(new ProviderClientBindingDescriptor
    {
        EffectiveConfig = effectiveConfig
    });
    ProviderCredentialBindingContext credentialBinding = binding == ProviderClientCredentialBinding.RequestTime
        ? new ProviderCredentialBindingContext.RequestTime(new UnusedCredentialSource(), plan)
        : new ProviderCredentialBindingContext.ConstructionTime(plan, new SmokeCredentialLease());

    var construction = await provider.CreateAsync(new ProviderClientConstructionContext
    {
        EffectiveConfig = effectiveConfig,
        AuthorizationScope = authorizationScope,
        Grant = grant,
        CredentialBinding = credentialBinding,
        Lifetime = lifetime,
        Services = new SmokeRuntimeServices()
    });

    // Both providers construct the product from a host they own themselves and hand back an
    // empty owner, so releasing it here cannot outlive the returned source.
    await construction.Owner.DisposeAsync();
    return construction.Client;
}

sealed class SmokeProvider : IVoiceActivitySourceProviderV1
{
    public string ProviderKey => "native-smoke";
    public string DisplayName => "Native smoke";
    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = ProviderKey,
        DisplayName = DisplayName,
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.VoiceActivityDetection] = new()
            {
                Family = ProviderClientFamily.VoiceActivityDetection,
                Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession,
            },
        },
    };
    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config) =>
        ProviderValidationResult.Success();

    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor) =>
        ProviderClientCredentialBinding.RequestTime;

    public ValueTask<ProviderClientConstruction<VoiceActivitySourceProductV1>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Lifetime.Lifetime != ProviderFamilyLifetime.StatefulPerAudioSession)
            throw new ArgumentException("The smoke source requires one isolated source per audio session.", nameof(context));

        return ValueTask.FromResult(new ProviderClientConstruction<VoiceActivitySourceProductV1>
        {
            Client = new VoiceActivitySourceProductV1.BorrowedSynchronous(new SmokeSource()),
            Owner = ProviderClientConstructionUtilities.Own()
        });
    }

    public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
}

sealed class SmokeSource : IBorrowedSynchronousVoiceActivitySourceV1
{
    public VoiceActivitySourceCapabilitiesV1 Capabilities { get; } = new(
        VoiceActivityInputOwnershipV1.BorrowedSynchronous,
        [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.BinaryDecision,
            new HPD.Agent.Authority.BoundedAscii("decision"), 0, 1, null),
        VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
        true, false, 1);

    public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window) =>
        new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Gap);
}

// The smoke binds anonymous, locally-owned providers, so credential preparation and acquisition
// are never reached. These exist only to satisfy the uniform construction contract.
sealed class UnusedCredentialSource : IProviderCredentialSource
{
    public ValueTask<ProviderCredentialPlan> PrepareAsync(
        ProviderCredentialRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The voice-activity AOT smoke does not prepare provider credentials.");

    public ValueTask<IProviderCredentialLease> AcquireAsync(
        ProviderCredentialPlan plan,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The voice-activity AOT smoke does not acquire provider credentials.");
}

sealed class SmokeCredentialLease : IProviderCredentialLease
{
    public ProviderCredential Credential { get; } = new ProviderCredential.Anonymous();

    public ProviderCredentialIdentity Identity { get; } = new()
    {
        ProviderKey = "aot-smoke",
        BackendKey = "local",
        Subject = "aot-smoke",
        TrustDomainId = "aot-smoke"
    };

    public ProviderCredentialGeneration Generation => new("aot-smoke");
    public DateTimeOffset? ExpiresAt => null;
    public CancellationToken RotationToken => CancellationToken.None;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

sealed class SmokeRuntimeServices : IProviderRuntimeServices
{
    public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;
    public IHttpClientFactory HttpClientFactory { get; } = new SmokeHttpClientFactory();
    public TimeProvider TimeProvider { get; } = TimeProvider.System;
    public IProviderTelemetry Telemetry { get; } = new SmokeTelemetry();
}

sealed class SmokeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

sealed class SmokeTelemetry : IProviderTelemetry
{
}
