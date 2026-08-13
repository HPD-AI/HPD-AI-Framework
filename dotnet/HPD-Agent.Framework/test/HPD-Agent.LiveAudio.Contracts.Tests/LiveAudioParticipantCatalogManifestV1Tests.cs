using HPD.Agent.Authority;
using System.Security.Cryptography;
using System.Reflection;

namespace HPD.Agent.Audio.Tests;

public sealed class LiveAudioParticipantCatalogManifestV1Tests
{
    [Fact]
    public void Allocation_public_surface_and_xml_are_exact()
    {
        var attribute = typeof(HpdGraphParticipantAllocationAttribute);
        Assert.True(attribute.IsPublic && attribute.IsSealed);
        Assert.Single(attribute.GetConstructors());
        Assert.Equal(["Amounts", "Dimensions", "OrderedNodeKeys", "PurposeIdHex", "WindowPolicies"],
            attribute.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(static p => p.Name).Order().ToArray());

        var manifestAttribute = typeof(HpdLiveAudioParticipantManifestAttribute);
        Assert.Equal([9, 11], manifestAttribute.GetConstructors().Select(static c => c.GetParameters().Length).Order().ToArray());
        Assert.Equal(["CapacityDimensions", "Dependencies", "FactoryKey", "FactoryType", "GenerationFence", "GraphParticipantAllocationDeclarationBytes", "GraphParticipantAllocationDeclarationFingerprintBytes", "MaximumDrainNanoseconds", "MaximumPrepareNanoseconds", "MaximumTerminateNanoseconds", "Owner"],
            manifestAttribute.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(static p => p.Name).Order().ToArray());
        var registration = typeof(LiveAudioParticipantFactoryRegistrationV1);
        Assert.Equal([3, 5], registration.GetConstructors().Select(static c => c.GetParameters().Length).Order().ToArray());
        Assert.Equal(["Descriptor", "FactoryIdentity", "FactoryType", "GraphParticipantAllocationDeclarationBytes", "GraphParticipantAllocationDeclarationFingerprint"],
            registration.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(static p => p.Name).Order().ToArray());
        Assert.Equal(typeof(ReadOnlyMemory<byte>), registration.GetProperty("GraphParticipantAllocationDeclarationBytes")!.PropertyType);
        Assert.Equal(typeof(Hash256?), registration.GetProperty("GraphParticipantAllocationDeclarationFingerprint")!.PropertyType);

        var xmlPath = Path.Combine(AppContext.BaseDirectory, typeof(LiveAudioParticipantCatalogManifestV1).Assembly.GetName().Name + ".xml");
        Assert.True(File.Exists(xmlPath), xmlPath);
        var xml = File.ReadAllText(xmlPath);
        Assert.Contains("HpdGraphParticipantAllocationAttribute", xml, StringComparison.Ordinal);
        Assert.Contains("GraphParticipantAllocationDeclarationBytes", xml, StringComparison.Ordinal);
        Assert.Contains("GraphParticipantAllocationDeclarationFingerprint", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Allocation_carrier_has_fixed_canonical_vector_and_separate_fingerprint()
    {
        var bytes = LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("graph",
            ["node-b", "node-a"], [2, 1], ["22222222222222222222222222222222", "11111111111111111111111111111111"], [9, 7], [2, 1]);
        Assert.Equal("a40001016567726170680282666e6f64652d62666e6f64652d610382a4000101501111111111111111111111111111111102070301a4000201502222222222222222222222222222222202090302", Convert.ToHexString(bytes).ToLowerInvariant());
        var fingerprint = AllocationFingerprint(bytes);
        Assert.True(LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(bytes,
            new BoundedAscii("graph"), [new CapacityDimensionId(1), new CapacityDimensionId(2)], fingerprint));
        Assert.False(LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(bytes,
            new BoundedAscii("other"), [new CapacityDimensionId(1), new CapacityDimensionId(2)], fingerprint));
    }

    [Fact]
    public void Allocation_attribute_and_registration_own_every_array()
    {
        var nodes = new[] { "node" }; var dimensions = new ushort[] { 1 }; var purposes = new[] { "11111111111111111111111111111111" };
        var amounts = new ulong[] { 1 }; var policies = new byte[] { 1 };
        var attribute = new HpdGraphParticipantAllocationAttribute(nodes, dimensions, purposes, amounts, policies);
        nodes[0] = "changed"; dimensions[0] = 2; purposes[0] = new string('2', 32); amounts[0] = 2; policies[0] = 2;
        Assert.Equal("node", attribute.OrderedNodeKeys[0]); Assert.Equal((ushort)1, attribute.Dimensions[0]);
        var returned = attribute.OrderedNodeKeys; returned[0] = "mutated"; Assert.Equal("node", attribute.OrderedNodeKeys[0]);
        var bytes = LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", ["node"], [Dimension(OwnerSliceId.S2).Value], [new string('1', 32)], [1], [1]);
        var fingerprint = AllocationFingerprint(bytes); var registration = new LiveAudioParticipantFactoryRegistrationV1(typeof(MediaFactory), "tests:media", Descriptor("media", OwnerSliceId.S2), bytes, fingerprint);
        bytes[0] ^= 1; var copy = registration.GraphParticipantAllocationDeclarationBytes.ToArray(); copy[0] ^= 1;
        Assert.True(LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(registration.GraphParticipantAllocationDeclarationBytes.Span,
            new BoundedAscii("media"), [CapacityDimensionsV1.QueueItems], fingerprint));
    }

    [Fact]
    public void Allocation_bounds_and_single_nonempty_registration_are_closed()
    {
        static string[] Nodes(int count, int length = 1) => Enumerable.Range(0, count).Select(i => i.ToString("x2") + new string('n', length - 1)).ToArray();
        Assert.NotEmpty(LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", Nodes(64), [1], [new string('1', 32)], [1], [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", Nodes(65), [1], [new string('1', 32)], [1], [1]));
        Assert.NotEmpty(LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", [new string('n', 64)], [1], [new string('1', 32)], [1], [1]));
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", [new string('n', 65)], [1], [new string('1', 32)], [1], [1]));
        var dims = Enumerable.Range(1, 14).Select(static i => (ushort)i).ToArray(); var purposes = dims.Select(i => i.ToString("x32")).ToArray();
        Assert.NotEmpty(LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", ["node"], dims, purposes, dims.Select(static i => (ulong)i).ToArray(), dims.Select(static _ => (byte)1).ToArray()));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", ["node"], [..dims, 1], [..purposes, new string('f',32)], new ulong[15], new byte[15]));
    }

    [Fact]
    public void Allocation_validator_is_total_for_truncation_and_every_byte_mutation()
    {
        var bytes = LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", ["node"], [3], [new string('1',32)], [1], [1]);
        var fingerprint = AllocationFingerprint(bytes); var dimensions = new[] { new CapacityDimensionId(3) };
        for (var length=0;length<bytes.Length;length++)
            Assert.False(LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(bytes.AsSpan(0,length), new BoundedAscii("media"), dimensions, fingerprint));
        for (var i=0;i<bytes.Length;i++)
        {
            var changed=bytes.ToArray();changed[i]^=0x80;
            Assert.False(LiveAudioParticipantCatalogManifestV1.TryValidateGraphParticipantAllocationDeclaration(changed,new BoundedAscii("media"),dimensions,fingerprint));
        }
    }

    [Fact]
    public void Allocation_presence_owner_fence_and_v1_fingerprint_are_independent()
    {
        var media = new MediaFactory(Descriptor("media", OwnerSliceId.S2)); var legacy = Registration(media);
        var legacyManifest = LiveAudioParticipantCatalogManifestV1.Create([legacy]);
        var bytes = LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("media", ["node"], [Dimension(OwnerSliceId.S2).Value], [new string('1',32)], [1], [1]);
        var allocated = new LiveAudioParticipantFactoryRegistrationV1(typeof(MediaFactory), legacy.FactoryIdentity, media.Descriptor, bytes, AllocationFingerprint(bytes));
        var allocationManifest = LiveAudioParticipantCatalogManifestV1.Create([allocated]);
        Assert.Equal(legacyManifest.Fingerprint, allocationManifest.Fingerprint);
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantCatalogManifestV1.Create([allocated, allocated]));
        var provider = new ProviderFactory(Descriptor("provider", OwnerSliceId.S5));
        var providerBytes = LiveAudioParticipantCatalogManifestV1.EncodeGraphParticipantAllocationDeclaration("provider", ["node"], [Dimension(OwnerSliceId.S5).Value], [new string('1',32)], [1], [1]);
        var providerAllocated = new LiveAudioParticipantFactoryRegistrationV1(typeof(ProviderFactory), "tests:provider", provider.Descriptor, providerBytes, AllocationFingerprint(providerBytes));
        Assert.Throws<ArgumentException>(() => LiveAudioParticipantCatalogManifestV1.Create([providerAllocated]));
        Assert.Throws<ArgumentException>(() => new LiveAudioParticipantFactoryRegistrationV1(typeof(MediaFactory), "tests:media", media.Descriptor, bytes, null));
    }

    private static Hash256 AllocationFingerprint(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData("hpd-graph-participant-allocation-declaration-v1\0"u8); hash.AppendData(bytes); return Hash256.FromBytes(hash.GetHashAndReset());
    }
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
