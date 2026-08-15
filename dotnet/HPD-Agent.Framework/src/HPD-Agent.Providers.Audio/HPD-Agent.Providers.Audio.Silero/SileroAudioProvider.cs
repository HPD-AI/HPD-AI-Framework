// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Audio.Silero;

/// <summary>Creates isolated stream-local Silero VAD sources over one validated ONNX model host.</summary>
[HpdProvider(Key, "Silero VAD")]
[HpdProviderFamily(ProviderClientFamily.VoiceActivityDetection,
    Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession)]
[HpdProviderPayload(ProviderClientFamily.VoiceActivityDetection, ProviderPayloadKind.Configuration,
    typeof(SileroVadOptions), typeof(SileroJsonContext))]
public sealed class SileroAudioProvider : IVoiceActivitySourceProviderV1, IDisposable
{
    public const string Key = "silero";
    private readonly object _gate = new();
    private SileroModelHostV1? _host;
    private string? _hostIdentity;
    private bool _disposed;

    public string ProviderKey => Key;
    public string DisplayName => "Silero VAD";

    public VoiceActivitySourceProductV1 CreateVoiceActivitySource(
        ProviderClientConfig configuration,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Lifetime != ProviderFamilyLifetime.StatefulPerAudioSession)
            throw new ArgumentException("Silero requires one isolated source per audio session.", nameof(context));
        var options = RequireOptions(configuration);
        var validation = ValidateConfiguration(configuration, ProviderClientFamily.VoiceActivityDetection);
        if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors), nameof(configuration));
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
            return new VoiceActivitySourceProductV1.BorrowedSynchronous(_host.CreateSource());
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
                DefaultModelId = "silero-vad-6.2",
                SupportedModels = ["silero-vad-6.2"],
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

    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errors = new List<string>();
        if (family != ProviderClientFamily.VoiceActivityDetection)
            errors.Add($"Silero does not support provider family '{family}'.");
        if (config.ProviderConfig is not SileroVadOptions options)
            errors.Add("SileroVadOptions are required in ProviderConfig.");
        else
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

    private static SileroVadOptions RequireOptions(ProviderClientConfig configuration) =>
        configuration.ProviderConfig as SileroVadOptions
        ?? throw new ArgumentException("SileroVadOptions are required in ProviderConfig.", nameof(configuration));

    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
