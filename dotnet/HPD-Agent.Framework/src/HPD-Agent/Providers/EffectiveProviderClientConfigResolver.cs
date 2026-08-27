using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Agent.Providers;

/// <summary>Resolves profile, agent, and run layers into one immutable provider-client snapshot.</summary>
public sealed class EffectiveProviderClientConfigResolver
{
    private readonly ProviderComposition _composition;

    /// <summary>Initializes the authoritative provider-client resolver.</summary>
    /// <param name="composition">The immutable generated provider composition.</param>
    public EffectiveProviderClientConfigResolver(ProviderComposition composition) =>
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));

    /// <summary>Resolves one family without acquiring credentials or performing network work.</summary>
    public EffectiveProviderClientConfig Resolve(
        AgentConfig agent,
        ProviderClientFamily family,
        AgentClientsConfig? runClients = null,
        AgentProviderProfileIndex? profileIndex = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var agentFamily = agent.Clients.GetFamilyConfig(family);
        var runFamily = runClients?.GetFamilyConfig(family);
        var explicitDraft = runFamily?.Provider ?? agentFamily?.Provider;
        AgentProviderBackendProfile? profile;
        ProviderReference draft;

        if (explicitDraft is not null)
        {
            draft = explicitDraft;
            var provider = _composition.Descriptors.Canonicalize(draft.Key);
            var backend = CompleteBackend(provider, draft.Backend, family);
            profile = (profileIndex ??= AgentProviderProfileIndex.Create(agent, _composition))
                .FindProfile(new ProviderBackendIdentity(provider, backend));
        }
        else
        {
            profileIndex ??= AgentProviderProfileIndex.Create(agent, _composition);
            if (!profileIndex.TryGetDefault(family, out var selectedDefault))
                throw Error("ProviderDefaultRequired", $"providerDefaults.{family}",
                    $"Client family '{family}' requires one explicit provider/backend default.");
            var provider = selectedDefault.ProviderKey;
            var backend = CompleteBackend(provider, selectedDefault.BackendKey, family);
            profile = profileIndex.FindProfile(new ProviderBackendIdentity(provider, backend))
                ?? throw Error("ProviderProfileRequired", $"providerProfiles.{provider}.{backend}",
                    $"Default provider/backend '{provider}/{backend}' requires a matching profile.", provider);
            draft = profile.Clients.GetFamilyConfig(family)?.Provider ?? new ProviderReference
            {
                Key = provider,
                Backend = backend
            };
        }

        var canonicalProvider = _composition.Descriptors.Canonicalize(draft.Key);
        var canonicalBackend = CompleteBackend(canonicalProvider, draft.Backend, family);
        var authentication = CompleteAuthentication(canonicalProvider, canonicalBackend, family, draft.Authentication);
        var selectedIdentity = new ProviderBackendIdentity(canonicalProvider, canonicalBackend);
        var provenance = ImmutableDictionary.CreateBuilder<string, ProviderConfigurationLayer>(StringComparer.Ordinal);

        ProviderClientConfig? effective = null;
        ApplyCompatible(profile?.Clients.GetFamilyConfig(family), ProviderConfigurationLayer.Profile);
        ApplyCompatible(agentFamily, ProviderConfigurationLayer.Agent);
        ApplyCompatible(runFamily, ProviderConfigurationLayer.Run);
        effective ??= CreateFamilyConfig(family);

        var headers = effective.CustomHeaders is null
            ? ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase)
            : effective.CustomHeaders.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        RejectCredentialHeaders(headers, canonicalProvider, family);
        var providerPayload = SnapshotPayload(
            canonicalProvider, family, ProviderPayloadKind.Configuration, effective.ProviderConfig);
        var operationPayload = SnapshotPayload(
            canonicalProvider, family, ProviderPayloadKind.OperationOptions, GetOperationPayload(effective));
        var familyDefaults = SnapshotFamilyDefaults(effective);
        var manifestRevision = ComputeHash(string.Join('|',
            canonicalProvider,
            canonicalBackend,
            string.Join(',', _composition.Fragments.Select(fragment => fragment.OwnerAssembly ?? "dynamic"))));
        var stableReference = GetStableAuthenticationIdentity(authentication);
        var scopes = authentication is OAuthProviderAuthentication oauth
            ? (oauth.Scopes ?? []).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToImmutableArray()
            : ImmutableArray<string>.Empty;
        var effectiveAuthentication = new EffectiveProviderAuthentication
        {
            Configuration = ProviderClientConfigSnapshot.CloneAuthentication(authentication),
            Kind = GetKind(authentication),
            StableReferenceIdentity = stableReference,
            Scopes = scopes,
            AuthorizationProfile = (authentication as OAuthProviderAuthentication)?.AuthorizationProfile,
            AuthorizationStoreIdentity = (authentication as OAuthProviderAuthentication)?.StoreKey
        };
        var endpoint = string.IsNullOrWhiteSpace(effective.Endpoint) ? null : new Uri(effective.Endpoint, UriKind.Absolute);
        var fingerprint = ComputeHash(string.Join('|', canonicalProvider, canonicalBackend, family,
            effective.ModelName, endpoint?.AbsoluteUri, stableReference,
            providerPayload.Fingerprint, operationPayload.Fingerprint,
            JsonSerializer.Serialize(familyDefaults),
            string.Join(';', headers.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}"))));

        return new EffectiveProviderClientConfig
        {
            Provider = new ResolvedProviderSelection
            {
                Backend = selectedIdentity,
                Authentication = effectiveAuthentication
            },
            Family = family,
            ModelName = effective.ModelName,
            Endpoint = endpoint,
            CustomHeaders = headers,
            ProviderConfiguration = providerPayload,
            FamilyOperation = operationPayload,
            FamilyDefaults = familyDefaults,
            Provenance = new ProviderConfigurationProvenance { Fields = provenance.ToImmutable() },
            ProviderManifestRevision = manifestRevision,
            ConstructionFingerprint = fingerprint
        };

        void ApplyCompatible(ProviderClientConfig? layer, ProviderConfigurationLayer source)
        {
            if (layer is null || !IsCompatible(layer.Provider, selectedIdentity))
                return;
            effective ??= CreateFamilyConfig(family);
            if (layer.Provider is not null)
            {
                effective.Provider = layer.Provider;
                provenance[nameof(ProviderClientConfig.Provider)] = source;
            }
            Set(layer.ModelName, value => effective.ModelName = value, nameof(ProviderClientConfig.ModelName));
            Set(layer.Endpoint, value => effective.Endpoint = value, nameof(ProviderClientConfig.Endpoint));
            if (layer.CustomHeaders is not null)
            {
                effective.CustomHeaders = new Dictionary<string, string>(layer.CustomHeaders, StringComparer.OrdinalIgnoreCase);
                provenance[nameof(ProviderClientConfig.CustomHeaders)] = source;
            }
            if (layer.ProviderConfig is not null)
            {
                effective.ProviderConfig = layer.ProviderConfig;
                provenance[nameof(ProviderClientConfig.ProviderConfig)] = source;
            }
            ApplyFamilyFields(layer, effective, provenance, source);

            void Set(string? value, Action<string> assign, string name)
            {
                if (value is null) return;
                assign(value);
                provenance[name] = source;
            }
        }
    }

    internal static ProviderClientConfig OverlayResolvedInheritance(
        ProviderClientConfig resolvedParent,
        ProviderClientConfig childOverlay)
    {
        ArgumentNullException.ThrowIfNull(resolvedParent);
        ArgumentNullException.ThrowIfNull(childOverlay);
        if (childOverlay.Provider is not null)
            return ProviderClientConfigSnapshot.Clone(childOverlay);

        var result = ProviderClientConfigSnapshot.Clone(resolvedParent);
        if (childOverlay.ModelName is not null) result.ModelName = childOverlay.ModelName;
        if (childOverlay.Endpoint is not null) result.Endpoint = childOverlay.Endpoint;
        if (childOverlay.CustomHeaders is not null)
            result.CustomHeaders = new Dictionary<string, string>(childOverlay.CustomHeaders, StringComparer.OrdinalIgnoreCase);
        if (childOverlay.ProviderConfig is not null) result.ProviderConfig = childOverlay.ProviderConfig;
        var provenance = ImmutableDictionary.CreateBuilder<string, ProviderConfigurationLayer>(StringComparer.Ordinal);
        ApplyFamilyFields(childOverlay, result, provenance, ProviderConfigurationLayer.Run);
        return result;
    }

    private static void ApplyFamilyFields(
        ProviderClientConfig layer,
        ProviderClientConfig effective,
        ImmutableDictionary<string, ProviderConfigurationLayer>.Builder provenance,
        ProviderConfigurationLayer source)
    {
        void SetStruct<T>(T? value, Action<T> assign, string name) where T : struct
        { if (value.HasValue) { assign(value.Value); provenance[name] = source; } }
        void SetText(string? value, Action<string> assign, string name)
        { if (value is not null) { assign(value); provenance[name] = source; } }

        switch (layer, effective)
        {
            case (TextToSpeechClientConfig from, TextToSpeechClientConfig to):
                SetText(from.VoiceId, value => to.VoiceId = value, nameof(from.VoiceId));
                SetText(from.Language, value => to.Language = value, nameof(from.Language));
                SetText(from.AudioFormat, value => to.AudioFormat = value, nameof(from.AudioFormat));
                SetStruct(from.Speed, value => to.Speed = value, nameof(from.Speed));
                SetStruct(from.Pitch, value => to.Pitch = value, nameof(from.Pitch));
                SetStruct(from.Volume, value => to.Volume = value, nameof(from.Volume));
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
            case (SpeechToTextClientConfig from, SpeechToTextClientConfig to):
                SetText(from.SpeechLanguage, value => to.SpeechLanguage = value, nameof(from.SpeechLanguage));
                SetStruct(from.SpeechSampleRate, value => to.SpeechSampleRate = value, nameof(from.SpeechSampleRate));
                SetText(from.TextLanguage, value => to.TextLanguage = value, nameof(from.TextLanguage));
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
            case (RealtimeClientConfig from, RealtimeClientConfig to):
                SetText(from.Voice, value => to.Voice = value, nameof(from.Voice));
                SetStruct(from.MaxOutputTokens, value => to.MaxOutputTokens = value, nameof(from.MaxOutputTokens));
                if (from.OutputModalities is not null) to.OutputModalities = from.OutputModalities.ToArray();
                if (from.OutputAudioFormat is not null) to.OutputAudioFormat = new RealtimeAudioFormatRunConfig
                    { MediaType = from.OutputAudioFormat.MediaType, SampleRate = from.OutputAudioFormat.SampleRate };
                if (from.Transcription is not null) to.Transcription = new RealtimeTranscriptionRunConfig
                    { ModelName = from.Transcription.ModelName, SpeechLanguage = from.Transcription.SpeechLanguage, Prompt = from.Transcription.Prompt };
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
            case (ImageGenerationClientConfig from, ImageGenerationClientConfig to):
                SetStruct(from.Count, value => to.Count = value, nameof(from.Count));
                SetText(from.MediaType, value => to.MediaType = value, nameof(from.MediaType));
                SetStruct(from.StreamingCount, value => to.StreamingCount = value, nameof(from.StreamingCount));
                if (from.ImageSize is not null) to.ImageSize = new ImageSizeRunConfig { Width = from.ImageSize.Width, Height = from.ImageSize.Height };
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
            case (EmbeddingsClientConfig from, EmbeddingsClientConfig to):
                SetStruct(from.Dimensions, value => to.Dimensions = value, nameof(from.Dimensions));
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
            case (HostedFilesClientConfig from, HostedFilesClientConfig to):
                SetText(from.Scope, value => to.Scope = value, nameof(from.Scope));
                SetText(from.Purpose, value => to.Purpose = value, nameof(from.Purpose));
                SetStruct(from.Limit, value => to.Limit = value, nameof(from.Limit));
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
            case (ChatClientConfig from, ChatClientConfig to):
                SetStruct(from.Temperature, value => to.Temperature = value, nameof(from.Temperature));
                SetStruct(from.TopP, value => to.TopP = value, nameof(from.TopP));
                SetStruct(from.TopK, value => to.TopK = value, nameof(from.TopK));
                SetStruct(from.MaxOutputTokens, value => to.MaxOutputTokens = value, nameof(from.MaxOutputTokens));
                SetStruct(from.FrequencyPenalty, value => to.FrequencyPenalty = value, nameof(from.FrequencyPenalty));
                SetStruct(from.PresencePenalty, value => to.PresencePenalty = value, nameof(from.PresencePenalty));
                SetStruct(from.Seed, value => to.Seed = value, nameof(from.Seed));
                if (from.StopSequences is not null)
                {
                    to.StopSequences = from.StopSequences.ToArray();
                    provenance[nameof(from.StopSequences)] = source;
                }
                if (from.Reasoning is not null)
                {
                    to.Reasoning = from.Reasoning.Clone();
                    provenance[nameof(from.Reasoning)] = source;
                }
                if (from.RuntimeResponseFormat is not null)
                {
                    to.RuntimeResponseFormat = from.RuntimeResponseFormat;
                    provenance[nameof(from.RuntimeResponseFormat)] = source;
                }
                if (from.ProviderOptions is not null) to.ProviderOptions = from.ProviderOptions;
                break;
        }
    }

    private static ProviderFamilyDefaultsSnapshot SnapshotFamilyDefaults(ProviderClientConfig config) => config switch
    {
        ChatClientConfig value => EmptyDefaults() with
        {
            Temperature = value.Temperature, TopP = value.TopP, TopK = value.TopK,
            MaxOutputTokens = value.MaxOutputTokens, FrequencyPenalty = value.FrequencyPenalty,
            PresencePenalty = value.PresencePenalty, Seed = value.Seed,
            StopSequences = (value.StopSequences ?? []).ToImmutableArray(),
            ReasoningEffort = value.Reasoning?.Effort, ReasoningOutput = value.Reasoning?.Output
        },
        TextToSpeechClientConfig value => EmptyDefaults() with
        { VoiceId = value.VoiceId, Language = value.Language, MediaType = value.AudioFormat,
          AudioFormat = value.AudioFormat, Speed = value.Speed, Pitch = value.Pitch, Volume = value.Volume },
        SpeechToTextClientConfig value => EmptyDefaults() with
        { Language = value.SpeechLanguage, SpeechLanguage = value.SpeechLanguage,
          TextLanguage = value.TextLanguage, SampleRate = value.SpeechSampleRate },
        RealtimeClientConfig value => EmptyDefaults() with
        { VoiceId = value.Voice, MediaType = value.OutputAudioFormat?.MediaType, SampleRate = value.OutputAudioFormat?.SampleRate,
          MaxOutputTokens = value.MaxOutputTokens, OutputModalities = (value.OutputModalities ?? []).ToImmutableArray(),
          TranscriptionModel = value.Transcription?.ModelName,
          TranscriptionLanguage = value.Transcription?.SpeechLanguage,
          TranscriptionPrompt = value.Transcription?.Prompt },
        ImageGenerationClientConfig value => EmptyDefaults() with
        { Count = value.Count, MediaType = value.MediaType, Width = value.ImageSize?.Width,
          Height = value.ImageSize?.Height, StreamingCount = value.StreamingCount },
        EmbeddingsClientConfig value => EmptyDefaults() with { Dimensions = value.Dimensions },
        HostedFilesClientConfig value => EmptyDefaults() with { Scope = value.Scope, Purpose = value.Purpose, Limit = value.Limit },
        _ => EmptyDefaults()
    };

    private static ProviderFamilyDefaultsSnapshot EmptyDefaults() => new()
    { OutputModalities = ImmutableArray<string>.Empty, StopSequences = ImmutableArray<string>.Empty };

    private string CompleteBackend(string providerKey, string? backendKey, ProviderClientFamily family)
    {
        _composition.Descriptors.TryGet(providerKey, out var descriptor);
        var candidates = descriptor!.Backends.Values
            .Where(backend => backend.Families.ContainsKey(family)).ToArray();
        var backend = string.IsNullOrWhiteSpace(backendKey)
            ? candidates.SingleOrDefault(static item => item.IsDefault)
                ?? throw Error("BackendSelectionRequired", $"clients.{family}.provider.backend",
                    $"Provider '{providerKey}' has no unique default backend for '{family}'.", providerKey)
            : candidates.SingleOrDefault(item => string.Equals(item.BackendKey, backendKey, StringComparison.Ordinal))
                ?? throw Error("BackendUnsupported", $"clients.{family}.provider.backend",
                    $"Provider '{providerKey}' does not support backend '{backendKey}' for '{family}'.", providerKey);
        return backend.BackendKey;
    }

    private ProviderAuthentication CompleteAuthentication(
        string providerKey,
        string backendKey,
        ProviderClientFamily family,
        ProviderAuthentication? authentication)
    {
        _composition.Descriptors.TryGet(providerKey, out var descriptor);
        var backend = descriptor!.Backends[backendKey];
        var supported = backend.Authentication.Where(item => item.SupportedFamilies.Contains(family)).ToArray();
        if (authentication is not null)
        {
            if (!supported.Any(item => item.Kind == GetKind(authentication)))
                throw Error("UnsupportedAuthentication", $"clients.{family}.provider.authentication",
                    $"Authentication '{GetKind(authentication)}' is not supported by '{providerKey}/{backendKey}' for '{family}'.", providerKey);
            return authentication;
        }

        var selected = supported.SingleOrDefault(static item => item.IsDefault)
            ?? throw Error("AuthenticationSelectionRequired", $"clients.{family}.provider.authentication",
                $"Provider/backend '{providerKey}/{backendKey}' has no unique default authentication for '{family}'.", providerKey);
        return selected.Kind switch
        {
            ProviderAuthenticationKind.ApiKey when !string.IsNullOrWhiteSpace(selected.DefaultSecretKey) =>
                new ApiKeyProviderAuthentication { SecretKey = selected.DefaultSecretKey },
            ProviderAuthenticationKind.Anonymous => new AnonymousProviderAuthentication(),
            _ => throw Error("AuthenticationConfigurationRequired", $"clients.{family}.provider.authentication",
                $"Authentication '{selected.Kind}' requires an explicit portable reference.", providerKey)
        };
    }

    private ProviderPayloadSnapshot SnapshotPayload(
        string providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        object? payload)
    {
        if (payload is null)
            return new ProviderPayloadSnapshot
            {
                ContractId = "none",
                CanonicalPayload = ImmutableArray<byte>.Empty,
                Fingerprint = ComputeHash(string.Empty)
            };
        if (!_composition.Serialization.TryGet(providerKey, family, kind, out var contract) || contract is null)
            throw Error("ProviderPayloadNotRegistered", $"clients.{family}.{kind}",
                $"Provider '{providerKey}' does not declare a '{kind}' payload contract.", providerKey);
        _composition.ValidatePayload(providerKey, family, kind, payload, $"clients.{family}.{kind}");
        var snapshot = contract.Snapshot(payload);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, contract.JsonTypeInfo);
        return new ProviderPayloadSnapshot
        {
            ContractId = contract.RuntimeType.FullName ?? contract.RuntimeType.Name,
            CanonicalPayload = ImmutableArray.Create(bytes),
            Fingerprint = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private bool IsCompatible(ProviderReference? selection, ProviderBackendIdentity selected) =>
        selection is null ||
        (string.Equals(_composition.Descriptors.Canonicalize(selection.Key), selected.ProviderKey, StringComparison.Ordinal) &&
         (string.IsNullOrWhiteSpace(selection.Backend) ||
          string.Equals(selection.Backend, selected.BackendKey, StringComparison.Ordinal)));

    private static ProviderAuthenticationKind GetKind(ProviderAuthentication authentication) => authentication switch
    {
        ApiKeyProviderAuthentication or ExplicitApiKeyProviderAuthentication => ProviderAuthenticationKind.ApiKey,
        OAuthProviderAuthentication => ProviderAuthenticationKind.OAuth,
        ExternalIdentityProviderAuthentication => ProviderAuthenticationKind.ExternalIdentity,
        AnonymousProviderAuthentication => ProviderAuthenticationKind.Anonymous,
        _ => throw new ArgumentOutOfRangeException(nameof(authentication))
    };

    private static string GetStableAuthenticationIdentity(ProviderAuthentication authentication) => authentication switch
    {
        ApiKeyProviderAuthentication value => $"api-key:{value.SecretKey}",
        ExplicitApiKeyProviderAuthentication value => $"explicit:{value.RuntimeRegistrationName}",
        OAuthProviderAuthentication value => $"oauth:{value.StoreKey ?? "default"}:{value.AuthorizationProfile ?? "default"}:{value.AccountId}",
        ExternalIdentityProviderAuthentication value => $"external:{value.CredentialName}",
        AnonymousProviderAuthentication => "anonymous",
        _ => throw new ArgumentOutOfRangeException(nameof(authentication))
    };

    private static object? GetOperationPayload(ProviderClientConfig config) => config switch
    {
        ChatClientConfig value => value.ProviderOptions,
        RealtimeClientConfig value => value.ProviderOptions,
        ImageGenerationClientConfig value => value.ProviderOptions,
        EmbeddingsClientConfig value => value.ProviderOptions,
        TextToSpeechClientConfig value => value.ProviderOptions,
        SpeechToTextClientConfig value => value.ProviderOptions,
        HostedFilesClientConfig value => value.ProviderOptions,
        _ => null
    };

    private static ProviderClientConfig CreateFamilyConfig(ProviderClientFamily family) => family switch
    {
        ProviderClientFamily.Chat => new ChatClientConfig(),
        ProviderClientFamily.Realtime => new RealtimeClientConfig(),
        ProviderClientFamily.ImageGeneration => new ImageGenerationClientConfig(),
        ProviderClientFamily.Embeddings => new EmbeddingsClientConfig(),
        ProviderClientFamily.TextToSpeech => new TextToSpeechClientConfig(),
        ProviderClientFamily.SpeechToText => new SpeechToTextClientConfig(),
        ProviderClientFamily.HostedFiles => new HostedFilesClientConfig(),
        ProviderClientFamily.VoiceActivityDetection => new VoiceActivityClientConfig(),
        ProviderClientFamily.EndOfTurnDetection => new EndOfTurnClientConfig(),
        _ => new ProviderClientConfig()
    };

    private static void RejectCredentialHeaders(
        ImmutableDictionary<string, string> headers,
        string providerKey,
        ProviderClientFamily family)
    {
        foreach (var header in headers.Keys)
            if (header.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("api-key", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("x-api-key", StringComparison.OrdinalIgnoreCase) ||
                header.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                throw Error("AuthenticationHeaderNotAllowed", $"clients.{family}.customHeaders.{header}",
                    $"Header '{header}' cannot carry provider credentials.", providerKey);
    }

    private static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static AgentRunConfigurationException Error(
        string code,
        string path,
        string message,
        string? providerKey = null) => new(code, path, message, providerKey);
}
