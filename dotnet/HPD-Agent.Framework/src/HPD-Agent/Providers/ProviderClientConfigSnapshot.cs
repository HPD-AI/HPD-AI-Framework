namespace HPD.Agent.Providers;

/// <summary>Creates defensive copies of mutable provider-client authoring configuration.</summary>
public static class ProviderClientConfigSnapshot
{
    /// <summary>Creates a defensive copy of one mutable family configuration.</summary>
    /// <param name="source">The authoring configuration to copy.</param>
    /// <returns>An independent configuration snapshot.</returns>
    public static ProviderClientConfig Clone(ProviderClientConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var target = Create(source);
        target.Provider = Clone(source.Provider);
        target.ModelName = source.ModelName;
        target.Endpoint = source.Endpoint;
        target.CustomHeaders = source.CustomHeaders is null
            ? null
            : new Dictionary<string, string>(source.CustomHeaders, StringComparer.OrdinalIgnoreCase);
        target.ProviderConfig = source.ProviderConfig;
        CopyFamily(source, target);
        return target;
    }

    /// <summary>
    /// Creates a defensive copy and snapshots provider-owned payloads through the
    /// source-generated serialization contracts for the selected provider family.
    /// </summary>
    /// <param name="source">The authoring configuration to copy.</param>
    /// <param name="providerKey">The canonical provider that owns the payloads.</param>
    /// <param name="family">The client family represented by <paramref name="source"/>.</param>
    /// <param name="composition">The immutable provider composition.</param>
    /// <returns>An independent configuration snapshot, including provider-owned payloads.</returns>
    public static ProviderClientConfig Clone(
        ProviderClientConfig source,
        string providerKey,
        ProviderClientFamily family,
        ProviderComposition composition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(composition);

        var target = Clone(source);
        target.ProviderConfig = (IProviderConfig?)SnapshotPayload(
            source.ProviderConfig,
            providerKey,
            family,
            ProviderPayloadKind.Configuration,
            composition);
        SetProviderOptions(target, SnapshotPayload(
            GetProviderOptions(source),
            providerKey,
            family,
            ProviderPayloadKind.OperationOptions,
            composition));
        return target;
    }

    private static ProviderReference? Clone(ProviderReference? source) =>
        source is null ? null : CloneProviderReference(source);

    /// <summary>Creates a defensive copy of one complete portable provider reference.</summary>
    /// <param name="source">The provider reference to copy.</param>
    /// <returns>An independent provider reference including its authentication selection.</returns>
    public static ProviderReference CloneProviderReference(ProviderReference source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ProviderReference
        {
            Key = source.Key,
            Backend = source.Backend,
            Authentication = CloneAuthenticationOrNull(source.Authentication)
        };
    }

    /// <summary>Creates a defensive copy of a portable or process-local authentication selection.</summary>
    /// <param name="source">The authentication selection to copy.</param>
    /// <returns>An independent authentication selection.</returns>
    public static ProviderAuthentication CloneAuthentication(ProviderAuthentication source) => source switch
    {
        ApiKeyProviderAuthentication value => new ApiKeyProviderAuthentication { SecretKey = value.SecretKey },
        ExplicitApiKeyProviderAuthentication value => new ExplicitApiKeyProviderAuthentication { RuntimeRegistrationName = value.RuntimeRegistrationName },
        OAuthProviderAuthentication value => new OAuthProviderAuthentication
        {
            AccountId = value.AccountId,
            Scopes = value.Scopes?.ToArray(),
            AuthorizationProfile = value.AuthorizationProfile,
            StoreKey = value.StoreKey
        },
        ExternalIdentityProviderAuthentication value => new ExternalIdentityProviderAuthentication { CredentialName = value.CredentialName },
        AnonymousProviderAuthentication => new AnonymousProviderAuthentication(),
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private static ProviderAuthentication? CloneAuthenticationOrNull(ProviderAuthentication? source) =>
        source is null ? null : CloneAuthentication(source);

    private static ProviderClientConfig Create(ProviderClientConfig source) => source switch
    {
        ChatClientConfig => new ChatClientConfig(),
        RealtimeClientConfig => new RealtimeClientConfig(),
        ImageGenerationClientConfig => new ImageGenerationClientConfig(),
        EmbeddingsClientConfig => new EmbeddingsClientConfig(),
        TextToSpeechClientConfig => new TextToSpeechClientConfig(),
        SpeechToTextClientConfig => new SpeechToTextClientConfig(),
        HostedFilesClientConfig => new HostedFilesClientConfig(),
        VoiceActivityClientConfig => new VoiceActivityClientConfig(),
        EndOfTurnClientConfig => new EndOfTurnClientConfig(),
        _ => new ProviderClientConfig()
    };

    private static void CopyFamily(ProviderClientConfig source, ProviderClientConfig target)
    {
        switch (source, target)
        {
            case (ChatClientConfig value, ChatClientConfig copy):
                copy.Temperature = value.Temperature;
                copy.TopP = value.TopP;
                copy.TopK = value.TopK;
                copy.MaxOutputTokens = value.MaxOutputTokens;
                copy.FrequencyPenalty = value.FrequencyPenalty;
                copy.PresencePenalty = value.PresencePenalty;
                copy.Seed = value.Seed;
                copy.StopSequences = value.StopSequences?.ToArray();
                copy.Reasoning = value.Reasoning?.Clone();
                copy.RuntimeResponseFormat = value.RuntimeResponseFormat;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
            case (RealtimeClientConfig value, RealtimeClientConfig copy):
                copy.OutputAudioFormat = value.OutputAudioFormat;
                copy.Voice = value.Voice;
                copy.MaxOutputTokens = value.MaxOutputTokens;
                copy.OutputModalities = value.OutputModalities;
                copy.Transcription = value.Transcription;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
            case (ImageGenerationClientConfig value, ImageGenerationClientConfig copy):
                copy.Count = value.Count;
                copy.ImageSize = value.ImageSize;
                copy.MediaType = value.MediaType;
                copy.StreamingCount = value.StreamingCount;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
            case (EmbeddingsClientConfig value, EmbeddingsClientConfig copy):
                copy.Dimensions = value.Dimensions;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
            case (TextToSpeechClientConfig value, TextToSpeechClientConfig copy):
                copy.VoiceId = value.VoiceId;
                copy.Language = value.Language;
                copy.AudioFormat = value.AudioFormat;
                copy.Speed = value.Speed;
                copy.Pitch = value.Pitch;
                copy.Volume = value.Volume;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
            case (SpeechToTextClientConfig value, SpeechToTextClientConfig copy):
                copy.SpeechLanguage = value.SpeechLanguage;
                copy.SpeechSampleRate = value.SpeechSampleRate;
                copy.TextLanguage = value.TextLanguage;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
            case (HostedFilesClientConfig value, HostedFilesClientConfig copy):
                copy.Scope = value.Scope;
                copy.Purpose = value.Purpose;
                copy.Limit = value.Limit;
                copy.ProviderOptions = value.ProviderOptions;
                copy.Override = value.Override;
                break;
        }
    }

    private static object? SnapshotPayload(
        object? value,
        string providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        ProviderComposition composition)
    {
        if (value is null)
            return null;
        if (!composition.Serialization.TryGet(providerKey, family, kind, out var contract) || contract is null)
            throw new AgentRunConfigurationException(
                kind == ProviderPayloadKind.Configuration ? "ProviderConfigTypeMismatch" : "ProviderOptionsTypeMismatch",
                $"providerProfiles.{providerKey}.clients.{family}",
                $"Provider '{providerKey}' does not declare a {kind} payload for family '{family}'.",
                providerKey);
        if (!contract.RuntimeType.IsInstanceOfType(value))
            throw new AgentRunConfigurationException(
                kind == ProviderPayloadKind.Configuration ? "ProviderConfigTypeMismatch" : "ProviderOptionsTypeMismatch",
                $"providerProfiles.{providerKey}.clients.{family}",
                $"The provider-owned payload is not compatible with provider '{providerKey}' and family '{family}'.",
                providerKey,
                contract.RuntimeType,
                value.GetType());
        return contract.Snapshot(value);
    }

    private static object? GetProviderOptions(ProviderClientConfig value) => value switch
    {
        ChatClientConfig typed => typed.ProviderOptions,
        RealtimeClientConfig typed => typed.ProviderOptions,
        ImageGenerationClientConfig typed => typed.ProviderOptions,
        EmbeddingsClientConfig typed => typed.ProviderOptions,
        TextToSpeechClientConfig typed => typed.ProviderOptions,
        SpeechToTextClientConfig typed => typed.ProviderOptions,
        HostedFilesClientConfig typed => typed.ProviderOptions,
        _ => null
    };

    private static void SetProviderOptions(ProviderClientConfig value, object? options)
    {
        switch (value)
        {
            case ChatClientConfig typed: typed.ProviderOptions = (IChatRequestOptions?)options; break;
            case RealtimeClientConfig typed: typed.ProviderOptions = (IRealtimeSessionProviderOptions?)options; break;
            case ImageGenerationClientConfig typed: typed.ProviderOptions = (IImageGenerationProviderOptions?)options; break;
            case EmbeddingsClientConfig typed: typed.ProviderOptions = (IEmbeddingGenerationProviderOptions?)options; break;
            case TextToSpeechClientConfig typed: typed.ProviderOptions = (ITextToSpeechProviderOptions?)options; break;
            case SpeechToTextClientConfig typed: typed.ProviderOptions = (ISpeechToTextProviderOptions?)options; break;
            case HostedFilesClientConfig typed: typed.ProviderOptions = (IHostedFileProviderOptions?)options; break;
        }
    }
}
