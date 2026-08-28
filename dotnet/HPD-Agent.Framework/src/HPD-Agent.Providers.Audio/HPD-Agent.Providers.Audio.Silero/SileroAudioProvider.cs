// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using System.Text.Json;

namespace HPD.Agent.Providers.Audio.Silero;

/// <summary>Creates isolated stream-local Silero VAD sources over one validated ONNX model host.</summary>
[HpdProvider(Key, "Silero VAD")]
[HpdProviderBackend("local", ProviderAuthenticationKind.Anonymous, IsDefaultBackend = true, IsDefaultAuthentication = true)]
[HpdProviderFamily(ProviderClientFamily.VoiceActivityDetection,
    Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession)]
[HpdProviderPayload(ProviderClientFamily.VoiceActivityDetection, ProviderPayloadKind.Configuration,
    typeof(SileroVadOptions), typeof(SileroJsonContext))]
public sealed class SileroAudioProvider : IVoiceActivitySourceProviderV1, IDisposable
{
    public const string Key = "silero";
    public const string DefaultModel = "silero-vad-6.2";
    private readonly object _gate = new();
    private SileroModelHostV1? _host;
    private string? _hostIdentity;
    private bool _disposed;

    public string ProviderKey => Key;
    public string DisplayName => "Silero VAD";

    /// <inheritdoc />
    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    /// <inheritdoc />
    public ValueTask<ProviderClientConstruction<VoiceActivitySourceProductV1>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.EffectiveConfig.Family != ProviderClientFamily.VoiceActivityDetection)
            throw new ArgumentException("The effective provider family is not voice activity detection.", nameof(context));
        if (context.Lifetime.Lifetime != ProviderFamilyLifetime.StatefulPerAudioSession)
            throw new ArgumentException("Silero requires one isolated source per audio session.", nameof(context));
        var options = ReadOptions(context.EffectiveConfig);
        var validation = ValidateConfiguration(context.EffectiveConfig);
        if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors), nameof(context));
        var identity = $"{Path.GetFullPath(options.ModelPath!)}|{options.ModelSha256}|{options.IntraOpThreads}";
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_host is null)
            {
                _host = new SileroModelHostV1(options.ModelPath!, options.ModelSha256, options.IntraOpThreads);
                _hostIdentity = identity;
            }
            else if (!StringComparer.Ordinal.Equals(_hostIdentity, identity))
            {
                throw new InvalidOperationException("A Silero provider instance cannot change its model host identity.");
            }
            VoiceActivitySourceProductV1 product = new VoiceActivitySourceProductV1.BorrowedSynchronous(_host.CreateSource());
            var construction = new ProviderClientConstruction<VoiceActivitySourceProductV1>
            {
                Client = product,
                Owner = ProviderClientConstructionUtilities.Own()
            };
            return ValueTask.FromResult(construction);
        }
    }

    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = Key,
        DisplayName = DisplayName,
        DocumentationUri = new Uri("https://github.com/snakers4/silero-vad"),
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.VoiceActivityDetection] = new()
            {
                Family = ProviderClientFamily.VoiceActivityDetection,
                Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession,
                DefaultModelId = DefaultModel,
                SupportedModels = [DefaultModel],
                Capabilities = new Dictionary<string, object?>
                {
                    ["InputSampleRates"] = new[] { 8_000, 16_000 },
                    ["InputChannels"] = 1,
                    ["WindowMilliseconds"] = 32,
                    ["MeasurementKind"] = "EngineScore",
                    ["StreamStateIsolation"] = true,
                    ["ImplicitModelDownload"] = false,
                    ["MaximumConcurrentInferences"] = 1,
                    ["MaximumPendingInferences"] = 0,
                    ["OnnxRuntimeVersion"] = SileroModelArtifactV1.OnnxRuntimeVersion,
                    ["SupportedRuntimeIdentifiers"] = SileroModelArtifactV1.NativeRuntimeAssets.Keys.ToArray(),
                }
            }
        }
    };

    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();
        if (config.Family != ProviderClientFamily.VoiceActivityDetection)
            errors.Add($"Silero does not support provider family '{config.Family}'.");
        SileroVadOptions? options = null;
        try
        {
            options = ReadOptions(config);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            errors.Add("SileroVadOptions are required in provider configuration.");
        }
        if (options is not null)
        {
            if (string.IsNullOrWhiteSpace(options.ModelPath)) errors.Add("An explicit local Silero ONNX model path is required.");
            else if (!File.Exists(options.ModelPath)) errors.Add("The configured Silero ONNX model does not exist.");
            if (!IsSha256(options.ModelSha256)) errors.Add("ModelSha256 must be exactly 64 lowercase hexadecimal characters.");
            if (options.IntraOpThreads is < 1 or > 64) errors.Add("IntraOpThreads must be between 1 and 64.");
        }
        return errors.Count == 0 ? ProviderValidationResult.Success() : ProviderValidationResult.Failure(errors.ToArray());
    }

    public IProviderErrorHandler CreateErrorHandler() => SileroErrorHandlerV1.Instance;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _host?.Dispose();
            _host = null;
        }
    }

    private static SileroVadOptions ReadOptions(EffectiveProviderClientConfig configuration) =>
        configuration.ProviderConfiguration.CanonicalPayload.IsEmpty
            ? throw new ArgumentException("SileroVadOptions are required in provider configuration.", nameof(configuration))
            : JsonSerializer.Deserialize(
                configuration.ProviderConfiguration.CanonicalPayload.AsSpan(),
                SileroJsonContext.Default.SileroVadOptions)
                ?? throw new ArgumentException("SileroVadOptions are required in provider configuration.", nameof(configuration));

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
