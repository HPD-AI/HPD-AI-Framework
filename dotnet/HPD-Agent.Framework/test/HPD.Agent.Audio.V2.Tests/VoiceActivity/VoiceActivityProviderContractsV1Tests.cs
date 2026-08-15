using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;

namespace HPD.Agent.Audio.V2.Tests.VoiceActivity;

public sealed class VoiceActivityProviderContractsV1Tests
{
    [Fact]
    public void Resolved_family_creates_the_typed_product_without_another_lookup()
    {
        var provider = new Provider(new Source(Capabilities()));
        var resolved = new ResolvedProviderFamily<IVoiceActivitySourceProviderV1>(provider,
            ProviderClientFamily.VoiceActivityDetection,
            new ProviderClientConfig { ProviderKey = "scripted", ModelName = "vad" },
            ProviderFamilyLifetime.StatefulPerAudioSession);
        var context = new ProviderComponentLifetimeContext(AudioSessionId: "audio-1",
            Lifetime: ProviderFamilyLifetime.StatefulPerAudioSession);

        var product = Assert.IsType<VoiceActivitySourceProductV1.BorrowedSynchronous>(
            VoiceActivitySourceProviderBindingV1.Create(resolved, context));

        Assert.Same(provider.Source, product.Source);
        Assert.Equal("vad", provider.ReceivedConfiguration!.ModelName);
        Assert.Same(context, provider.ReceivedContext);
    }

    [Fact]
    public void Product_and_lifecycle_contradictions_fail_before_runtime_use()
    {
        var provider = new Provider(new Source(Capabilities()));
        var resolved = new ResolvedProviderFamily<IVoiceActivitySourceProviderV1>(provider,
            ProviderClientFamily.VoiceActivityDetection,
            new ProviderClientConfig { ProviderKey = "scripted", ModelName = "vad" },
            ProviderFamilyLifetime.StatefulPerAudioSession);

        Assert.Throws<ArgumentException>(() => VoiceActivitySourceProviderBindingV1.Create(resolved,
            new ProviderComponentLifetimeContext(Lifetime: ProviderFamilyLifetime.StatefulPerRun)));
        Assert.Throws<ArgumentException>(() => new VoiceActivitySourceProductV1.Transferred(provider.Source));
        Assert.Null(provider.ReceivedConfiguration);
    }

    [Fact]
    public void Provider_contract_namespace_has_an_exact_exported_surface()
    {
        var names = typeof(IVoiceActivitySourceProviderV1).Assembly.ExportedTypes
            .Where(static type => type.Namespace == "HPD.Agent.Audio.ProviderContracts.VoiceActivity" &&
                                  type.DeclaringType is null)
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[]
        {
            "IBorrowedSynchronousVoiceActivitySourceV1", "ITransferredVoiceActivitySourceV1",
            "IVoiceActivitySourceMiddlewareV1", "IVoiceActivitySourceProviderV1", "VoiceActivityBorrowedWindowV1", "VoiceActivityInputFormatV1",
            "VoiceActivityInputInvalidReasonV1", "VoiceActivityInputOwnershipV1", "VoiceActivityMeasurementDescriptorV1",
            "VoiceActivityMeasurementKindV1", "VoiceActivityMeasurementV1", "VoiceActivityMediaExtentV1",
            "VoiceActivityNoObservationReasonV1", "VoiceActivityOwnedWindowV1", "VoiceActivityRetryabilityV1",
            "VoiceActivitySampleEncodingV1", "VoiceActivitySettlementResultV1", "VoiceActivitySourceCapabilitiesV1",
            "VoiceActivitySourceConcurrencyV1", "VoiceActivitySourceControlV1", "VoiceActivitySourceFaultClassV1",
            "VoiceActivitySourceMiddlewareContextV1", "VoiceActivitySourceMiddlewarePipelineV1",
            "VoiceActivitySourceMiddlewareRegistrationV1", "VoiceActivitySourceOutcomeV1", "VoiceActivitySourceProductV1",
            "VoiceActivitySourceProviderBindingV1",
            "VoiceActivitySourceStateModelV1", "VoiceActivitySourceUnavailableReasonV1", "VoiceActivityStateValidityV1",
            "VoiceActivityTransferResultV1", "VoiceActivityWindowCapabilityV1",
        }, names);
    }

    [Fact]
    public void Middleware_composition_is_explicit_stable_and_duplicate_closed()
    {
        var calls = new List<string>();
        var product = new VoiceActivitySourceProductV1.BorrowedSynchronous(new Source(Capabilities()));
        var context = new VoiceActivitySourceMiddlewareContextV1("scripted",
            ProviderFamilyLifetime.StatefulPerAudioSession);
        var registrations = new[]
        {
            new VoiceActivitySourceMiddlewareRegistrationV1("z-last", 20, new Middleware("z-last", calls)),
            new VoiceActivitySourceMiddlewareRegistrationV1("b-tie", 10, new Middleware("b-tie", calls)),
            new VoiceActivitySourceMiddlewareRegistrationV1("a-tie", 10, new Middleware("a-tie", calls)),
        };

        Assert.Same(product, VoiceActivitySourceMiddlewarePipelineV1.Apply(product, context, registrations));
        Assert.Equal(new[] { "a-tie", "b-tie", "z-last" }, calls);
        Assert.Throws<ArgumentException>(() => VoiceActivitySourceMiddlewarePipelineV1.Apply(product, context,
        [
            registrations[0],
            new VoiceActivitySourceMiddlewareRegistrationV1("z-last", 30, new Middleware("duplicate", calls)),
        ]));
    }

    private sealed class Provider(Source source) : IVoiceActivitySourceProviderV1
    {
        internal Source Source => source;
        internal ProviderClientConfig? ReceivedConfiguration { get; private set; }
        internal ProviderComponentLifetimeContext? ReceivedContext { get; private set; }
        public string ProviderKey => "scripted";
        public string DisplayName => "Scripted";
        public VoiceActivitySourceProductV1 CreateVoiceActivitySource(ProviderClientConfig configuration,
            ProviderComponentLifetimeContext context, IServiceProvider? services = null)
        {
            ReceivedConfiguration = configuration;
            ReceivedContext = context;
            return new VoiceActivitySourceProductV1.BorrowedSynchronous(source);
        }
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
        public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family) =>
            ProviderValidationResult.Success();
        public IProviderErrorHandler CreateErrorHandler() => throw new NotSupportedException();
    }

    private sealed class Source(VoiceActivitySourceCapabilitiesV1 capabilities) :
        IBorrowedSynchronousVoiceActivitySourceV1, ITransferredVoiceActivitySourceV1
    {
        public VoiceActivitySourceCapabilitiesV1 Capabilities => capabilities;
        public VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window) =>
            new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.Gap);
        public ValueTask<VoiceActivityTransferResultV1> TransferAsync(
            VoiceActivityOwnedWindowV1 window, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
            OperationId operationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Middleware(string key, List<string> calls) : IVoiceActivitySourceMiddlewareV1
    {
        public VoiceActivitySourceProductV1 Wrap(
            VoiceActivitySourceProductV1 current, VoiceActivitySourceMiddlewareContextV1 context)
        {
            calls.Add(key);
            return current;
        }
    }

    private static VoiceActivitySourceCapabilitiesV1 Capabilities() => new(
        VoiceActivityInputOwnershipV1.BorrowedSynchronous,
        [new VoiceActivityInputFormatV1(VoiceActivitySampleEncodingV1.SignedPcm16, 16_000, 1)],
        new VoiceActivityWindowCapabilityV1(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(10), 1),
        new VoiceActivityMeasurementDescriptorV1(VoiceActivityMeasurementKindV1.BinaryDecision,
            new BoundedAscii("decision"), 0, 1, null),
        VoiceActivitySourceStateModelV1.Stateless, VoiceActivitySourceConcurrencyV1.Serial,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.Unsupported,
        VoiceActivitySourceControlV1.Unsupported, VoiceActivitySourceControlV1.ReplacementRequired,
        true, false, 1);
}
