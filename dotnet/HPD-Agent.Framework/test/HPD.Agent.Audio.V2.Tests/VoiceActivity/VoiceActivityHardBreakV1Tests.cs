using System.Text.Json;
using HPD.Agent.Audio.Turns;
using HPD.Agent.Audio.Runtime.Scenarios;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityHardBreakV1Tests
{
    private static readonly string[] RemovedTypeNames =
    [
        "IVoiceActivity" + "Detector",
        "IVoiceActivity" + "DetectorProvider",
        "Vad" + "Event",
        "Vad" + "Result",
        "Vad" + "State",
        "VoiceActivityEvidence" + "Detail",
        "ITurn" + "Controller",
        "InputTurn" + "Controller",
        "Turn" + "Evidence",
        "Turn" + "Decision",
        "Turn" + "Commit",
        "Turn" + "Snapshot",
        "Transcript" + "Stage",
    ];

    [Fact]
    public void Legacy_detector_and_summary_evidence_types_are_absent()
    {
        var exported = typeof(AgentBuilder).Assembly.GetExportedTypes()
            .Concat(typeof(EndpointEvidenceProjectionDetailV1).Assembly.GetExportedTypes())
            .Select(static type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(RemovedTypeNames, name => Assert.DoesNotContain(name, exported));
    }

    [Fact]
    public void Agent_composition_no_longer_exposes_legacy_detector_factories_or_middleware()
    {
        Assert.Null(typeof(AgentClientSet).GetProperty("VoiceActivity" + "DetectorFactory"));
        Assert.Null(typeof(AgentBuilder).GetMethod("UseVoiceActivity" + "DetectorMiddleware"));
        Assert.DoesNotContain(typeof(CompositeProvider).GetInterfaces(),
            contract => contract.Name == "IVoiceActivity" + "DetectorProvider");
        Assert.Null(typeof(AudioInteractionRuntimeRequest).GetProperty("Turn" + "Controller"));
    }

    [Fact]
    public void Voice_activity_configuration_uses_the_leaf_owned_hard_break_name()
    {
        var value = new AgentClientsConfig
        {
            VoiceActivity = new VoiceActivityClientConfig
            {
                ProviderKey = "voice-provider",
                ModelName = "model"
            }
        };
        var json = JsonSerializer.Serialize(value, HPDJsonContext.Default.AgentClientsConfig);
        Assert.Contains("\"voiceActivity\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("voiceActivity" + "Detection", json, StringComparison.Ordinal);
        var decoded = JsonSerializer.Deserialize(json, HPDJsonContext.Default.AgentClientsConfig);
        Assert.IsType<VoiceActivityClientConfig>(decoded!.VoiceActivity);
        Assert.Equal("voice-provider", decoded.VoiceActivity.ProviderKey);
    }

    [Fact]
    public void Generic_registry_remains_the_only_voice_activity_provider_resolution_path()
    {
        var registry = new ProviderRegistry();
        registry.Register(new TestVoiceProvider());
        var resolved = registry.ResolveRequiredFamily<TestVoiceProvider>(new VoiceActivityClientConfig
        {
            ProviderKey = "voice-provider"
        }, ProviderClientFamily.VoiceActivityDetection, ProviderFamilyLifetime.StatefulPerAudioSession);
        Assert.Same(registry.GetProvider("voice-provider"), resolved.Provider);
        Assert.Equal(ProviderClientFamily.VoiceActivityDetection, resolved.Family);
    }

    private sealed class TestVoiceProvider : IProvider
    {
        public string ProviderKey => "voice-provider";
        public string DisplayName => "Voice Provider";
        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.VoiceActivityDetection] = new()
                {
                    Family = ProviderClientFamily.VoiceActivityDetection,
                    Lifetime = ProviderFamilyLifetime.StatefulPerAudioSession
                }
            }
        };
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config,
            ProviderClientFamily family) => ProviderValidationResult.Success();
        public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
    }
}
