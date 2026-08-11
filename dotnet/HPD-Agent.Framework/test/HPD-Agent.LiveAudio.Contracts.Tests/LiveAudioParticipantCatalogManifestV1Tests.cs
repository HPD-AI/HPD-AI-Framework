using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioParticipantCatalogManifestV1Tests
{
    [Fact]
    public void Manifest_is_order_independent_and_binds_exact_descriptor_bytes()
    {
        var media = new MediaFactory(Descriptor("media", OwnerSliceId.S2));
        var provider = new ProviderFactory(Descriptor("provider", OwnerSliceId.S5, "media"));

        var first = LiveAudioParticipantCatalogManifestV1.Create([Registration(provider), Registration(media)]);
        var second = LiveAudioParticipantCatalogManifestV1.Create([Registration(media), Registration(provider)]);

        Assert.Equal(["media", "provider"], first.Descriptors.Select(static value => value.FactoryKey.ToString()));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(default, first.Fingerprint);
        Assert.Equal("bf4b033ce8905f243ad577b2e9026ce76e26e49959c071e858ab9db317bafa0e",
            first.Fingerprint.ToString());
        var changed = LiveAudioParticipantCatalogManifestV1.Create([Registration(media),
            Registration(new ProviderFactory(new LiveAudioParticipantDescriptorV1(new BoundedAscii("provider"), OwnerSliceId.S5,
                AuthorityAxisId.Provider, [new BoundedAscii("media")], [CapacityDimensionsV1.ProviderInflight],
                new DurationNs(11), new DurationNs(20), new DurationNs(30))))]);
        Assert.NotEqual(first.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void Catalog_rejects_missing_extra_or_changed_factory_descriptors()
    {
        var media = new MediaFactory(Descriptor("media", OwnerSliceId.S2));
        var provider = new ProviderFactory(Descriptor("provider", OwnerSliceId.S5, "media"));
        var registrations = new[] { Registration(media), Registration(provider) };

        var catalog = GeneratedCatalog.Create(registrations, [provider, media]);
        Assert.Equal(2, catalog.Count);
        Assert.Throws<ArgumentException>(() => GeneratedCatalog.Create(registrations, [media]));
        Assert.Throws<ArgumentException>(() => GeneratedCatalog.Create(registrations,
            [media, provider, new OutputFactory(Descriptor("output", OwnerSliceId.S6))]));
        Assert.Throws<ArgumentException>(() => GeneratedCatalog.Create(registrations,
            [media, new ProviderFactory(new LiveAudioParticipantDescriptorV1(new BoundedAscii("provider"), OwnerSliceId.S5,
                AuthorityAxisId.Provider, [new BoundedAscii("media")], [CapacityDimensionsV1.ProviderInflight],
                new DurationNs(11), new DurationNs(20), new DurationNs(30)))]));
        Assert.Throws<ArgumentException>(() => GeneratedCatalog.Create(registrations,
            [media, new AlternateProviderFactory(provider.Descriptor)]));
    }

    [Fact]
    public void Manifest_rejects_duplicate_or_unclosed_dependency_sets()
    {
        var media = new MediaFactory(Descriptor("media", OwnerSliceId.S2));
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantCatalogManifestV1.Create([
            Registration(media), Registration(new AlternateMediaFactory(media.Descriptor))]));
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantCatalogManifestV1.Create([
            Registration(new ProviderFactory(Descriptor("provider", OwnerSliceId.S5, "missing")))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveAudioParticipantCatalogManifestV1.Create([]));
    }

    private static LiveAudioParticipantDescriptorV1 Descriptor(string key, OwnerSliceId owner, params string[] dependencies) =>
        new(new BoundedAscii(key), owner, Axis(owner), dependencies.Select(static value => new BoundedAscii(value)),
            [Dimension(owner)], new DurationNs(10), new DurationNs(20), new DurationNs(30));

    private static AuthorityAxisId Axis(OwnerSliceId owner) => owner switch
    {
        OwnerSliceId.S2 => AuthorityAxisId.Graph,
        OwnerSliceId.S5 => AuthorityAxisId.Provider,
        OwnerSliceId.S6 => AuthorityAxisId.Output,
        _ => throw new ArgumentOutOfRangeException(nameof(owner)),
    };

    private static CapacityDimensionId Dimension(OwnerSliceId owner) => owner switch
    {
        OwnerSliceId.S2 => CapacityDimensionsV1.QueueItems,
        OwnerSliceId.S5 => CapacityDimensionsV1.ProviderInflight,
        OwnerSliceId.S6 => CapacityDimensionsV1.OutputInflight,
        _ => throw new ArgumentOutOfRangeException(nameof(owner)),
    };

    private abstract class FactoryBase(LiveAudioParticipantDescriptorV1 descriptor) : ILiveAudioParticipantFactoryV1
    {
        public LiveAudioParticipantDescriptorV1 Descriptor { get; } = descriptor;
        public ValueTask<LiveAudioParticipantFactoryResultV1> PrepareAsync(
            LiveAudioParticipantPreparationContextV1 context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<LiveAudioParticipantFactoryResultV1>(
                new LiveAudioParticipantFactoryResultV1.Refused(new BoundedAscii("not-used")));
    }

    private sealed class MediaFactory(LiveAudioParticipantDescriptorV1 descriptor) : FactoryBase(descriptor);
    private sealed class AlternateMediaFactory(LiveAudioParticipantDescriptorV1 descriptor) : FactoryBase(descriptor);
    private sealed class ProviderFactory(LiveAudioParticipantDescriptorV1 descriptor) : FactoryBase(descriptor);
    private sealed class AlternateProviderFactory(LiveAudioParticipantDescriptorV1 descriptor) : FactoryBase(descriptor);
    private sealed class OutputFactory(LiveAudioParticipantDescriptorV1 descriptor) : FactoryBase(descriptor);

    private static LiveAudioParticipantFactoryRegistrationV1 Registration(FactoryBase factory) =>
        new(factory.GetType(), "HPD-Agent.LiveAudio.Contracts.Tests:" + factory.Descriptor.FactoryKey, factory.Descriptor);

    private sealed class GeneratedCatalog : LiveAudioParticipantFactoryCatalogV1
    {
        private GeneratedCatalog(IEnumerable<LiveAudioParticipantFactoryRegistrationV1> registrations,
            IEnumerable<ILiveAudioParticipantFactoryV1> factories) : base(registrations, factories) { }

        internal static GeneratedCatalog Create(IEnumerable<LiveAudioParticipantFactoryRegistrationV1> registrations,
            IEnumerable<ILiveAudioParticipantFactoryV1> factories) => new(registrations, factories);
    }
}
