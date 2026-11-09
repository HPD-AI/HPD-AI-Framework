using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderManifestContractsTests
{
    [Fact]
    public void Fragment_DefensivelyCopiesContributionCollections()
    {
        var descriptors = new List<IProviderDescriptor>();
        var factories = new List<ProviderRuntimeFactoryRegistration>();
        var fragment = new ProviderManifestFragment(descriptors, factories, Array.Empty<ProviderPayloadJsonContract>(), Array.Empty<ProviderSecretAliasRegistration>());

        descriptors.Add(new TestDescriptor());
        factories.Add(new ProviderRuntimeFactoryRegistration("test", ["default"], Array.Empty<ProviderClientFamily>(), static () => throw new NotSupportedException()));

        Assert.Empty(fragment.Descriptors);
        Assert.Empty(fragment.RuntimeFactories);
        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<IProviderDescriptor>>(
            fragment.Descriptors);
        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<ProviderRuntimeFactoryRegistration>>(
            fragment.RuntimeFactories);
    }

    [Fact]
    public void Fragment_RejectsNullCollections()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ProviderManifestFragment(null!, Array.Empty<ProviderRuntimeFactoryRegistration>(), Array.Empty<ProviderPayloadJsonContract>(), Array.Empty<ProviderSecretAliasRegistration>()));
        Assert.Throws<ArgumentNullException>(() =>
            new ProviderManifestFragment(Array.Empty<IProviderDescriptor>(), null!, Array.Empty<ProviderPayloadJsonContract>(), Array.Empty<ProviderSecretAliasRegistration>()));
    }

    [Fact]
    public void Composition_MergesFamiliesAndCanonicalizesAliases()
    {
        var chat = new TestDescriptor("test", ProviderClientFamily.Chat, ["legacy-test"]);
        var embeddings = new TestDescriptor("test", ProviderClientFamily.Embeddings, []);
        var composition = ProviderComposition.Create([
            new([chat], [new("test", ["default"], [ProviderClientFamily.Chat], static () => throw new NotSupportedException())], [], []),
            new([embeddings], [new("test", ["default"], [ProviderClientFamily.Embeddings], static () => throw new NotSupportedException())], [], [])]);

        Assert.Equal("test", composition.Descriptors.Canonicalize("LEGACY-TEST"));
        Assert.True(composition.Descriptors.TryGet("test", out var descriptor));
        Assert.Equal(2, descriptor!.Families.Count);
        Assert.NotNull(composition.Runtime.GetFactory("legacy-test", "default", ProviderClientFamily.Chat));
    }

    [Fact]
    public void Composition_MergesCompatibleBackendContributionsAcrossPackages()
    {
        var chat = new TestDescriptor(
            "openai",
            ProviderClientFamily.Chat,
            [],
            backend: Backend("platform", ProviderClientFamily.Chat));
        var speech = new TestDescriptor(
            "openai",
            ProviderClientFamily.SpeechToText,
            [],
            backend: Backend("platform", ProviderClientFamily.SpeechToText));
        var chatFactory = new ProviderRuntimeFactoryRegistration(
            "openai", ["platform"], [ProviderClientFamily.Chat], static () => throw new NotSupportedException());
        var speechFactory = new ProviderRuntimeFactoryRegistration(
            "openai", ["platform"], [ProviderClientFamily.SpeechToText], static () => throw new NotSupportedException());
        var composition = ProviderComposition.Create([
            new([chat], [chatFactory], [], []),
            new([speech], [speechFactory], [], [])]);

        var descriptor = Assert.Single(composition.Descriptors.Providers);
        Assert.Equal(2, descriptor.Families.Count);
        var backend = Assert.Single(descriptor.Backends).Value;
        Assert.True(backend.IsDefault);
        Assert.Equal(2, backend.Families.Count);
        var authentication = Assert.Single(backend.Authentication);
        Assert.True(authentication.IsDefault);
        Assert.Equal(
            [ProviderClientFamily.Chat, ProviderClientFamily.SpeechToText],
            authentication.SupportedFamilies.Order().ToArray());
        Assert.Same(chatFactory, composition.Runtime.GetFactory("openai", "platform", ProviderClientFamily.Chat));
        Assert.Same(speechFactory, composition.Runtime.GetFactory("openai", "platform", ProviderClientFamily.SpeechToText));
    }

    [Fact]
    public void Composition_RejectsConflictingFamilyContributions()
    {
        var exception = Assert.Throws<ProviderCompositionException>(() => ProviderComposition.Create([
            new([new TestDescriptor("test", ProviderClientFamily.Chat, [])], [], [], []),
            new([new TestDescriptor("test", ProviderClientFamily.Chat, [], ProviderFamilyLifetime.StatefulPerRun)], [], [], [])]));
        Assert.Equal("HPDP010", exception.Code);
    }

    [Fact]
    public void Composition_SelectsRuntimeFactoryByBackend()
    {
        var descriptor = new TestDescriptor("test", ProviderClientFamily.Chat, []);
        var platform = new ProviderRuntimeFactoryRegistration(
            "test", ["platform"], [ProviderClientFamily.Chat], static () => throw new InvalidOperationException("platform"));
        var account = new ProviderRuntimeFactoryRegistration(
            "test", ["account"], [ProviderClientFamily.Chat], static () => throw new InvalidOperationException("account"));
        var composition = ProviderComposition.Create([
            new([descriptor], [platform], [], []),
            new([descriptor], [account], [], [])]);

        Assert.Same(platform, composition.Runtime.GetFactory("test", "platform", ProviderClientFamily.Chat));
        Assert.Same(account, composition.Runtime.GetFactory("test", "account", ProviderClientFamily.Chat));
    }

    [Fact]
    public void Composition_RejectsAliasCollision()
    {
        var exception = Assert.Throws<ProviderCompositionException>(() => ProviderComposition.Create([
            new([new TestDescriptor("one", ProviderClientFamily.Chat, ["two"])], [], [], []),
            new([new TestDescriptor("two", ProviderClientFamily.Chat, [])], [], [], [])]));
        Assert.Equal("HPDP011", exception.Code);
    }

    [Fact]
    public void Composition_MergesSecretAliasesWithoutGlobalMutation()
    {
        var composition = ProviderComposition.Create([
            new([], [], [], [new("test:ApiKey", ["TEST_API_KEY", "TEST_API_KEY_FALLBACK"])])]);

        Assert.Equal(
            ["TEST_API_KEY", "TEST_API_KEY_FALLBACK"],
            composition.SecretAliases.GetEnvironmentVariables("test:ApiKey"));
        Assert.Null(composition.SecretAliases.GetEnvironmentVariables("missing:ApiKey"));
    }

    [Fact]
    public void Composition_BuildsDeterministicAuthorityCatalogFromGeneratedFragments()
    {
        var descriptor = new TestDescriptor("test", ProviderClientFamily.Chat, []);
        var contract = new ProviderPayloadJsonContract(
            "test", ProviderClientFamily.Chat, ProviderPayloadKind.Configuration,
            typeof(ProviderClientConfig), HPDJsonContext.Default.ProviderClientConfig);
        ProviderManifestFragment CreateFragment() => new(
            [descriptor],
            [new("test", ["default"], [ProviderClientFamily.Chat], static () => throw new NotSupportedException())],
            [contract],
            [new("test:ApiKey", ["TEST_API_KEY"])],
            "HPD.Agent.Providers.Test");

        var first = ProviderComposition.Create([CreateFragment()]);
        var second = ProviderComposition.Create([CreateFragment()]);

        Assert.NotNull(first.AuthorityCatalog);
        Assert.Equal(first.AuthorityCatalog, second.AuthorityCatalog);
        Assert.Equal(first.AuthorityCatalog!.Fingerprint, second.AuthorityCatalog!.Fingerprint);
        var contribution = Assert.Single(first.AuthorityCatalog.Contributions);
        Assert.Equal("HPD.Agent.Providers.Test", contribution.OwnerAssembly.ToString());
        Assert.Equal([HPD.Agent.Authority.ProviderRoleV1.Chat], contribution.Roles);
        Assert.Equal(HPD.Agent.Authority.ProviderLifetimeV1.AgentScoped, contribution.Lifetime);
        Assert.Single(contribution.CodecIds);
        Assert.Equal([new HPD.Agent.Authority.BoundedAscii("test:ApiKey")], contribution.CredentialAliases);
    }

    [Fact]
    public void Composition_DoesNotPromoteLegacyFragmentsToAuthorityMembership()
    {
        var composition = ProviderComposition.Create([
            new([new TestDescriptor()], [], [], [])]);

        Assert.Null(composition.AuthorityCatalog);
    }

    [Fact]
    public void Composition_RejectsConflictingSecretAliases()
    {
        var exception = Assert.Throws<ProviderCompositionException>(() => ProviderComposition.Create([
            new([], [], [], [new("test:ApiKey", ["FIRST_API_KEY"])]),
            new([], [], [], [new("test:ApiKey", ["SECOND_API_KEY"])])]));

        Assert.Equal("HPDP014", exception.Code);
    }

    [Fact]
    public void ValidatePayload_RejectsMissingProviderAndWrongConcreteType()
    {
        var descriptor = new TestDescriptor("test", ProviderClientFamily.Chat, []);
        var contract = new ProviderPayloadJsonContract(
            "test",
            ProviderClientFamily.Chat,
            ProviderPayloadKind.Configuration,
            typeof(ProviderClientConfig),
            HPDJsonContext.Default.ProviderClientConfig);
        var composition = ProviderComposition.Create([new([descriptor], [], [contract], [])]);

        var missing = Assert.Throws<AgentRunConfigurationException>(() => composition.ValidatePayload(
            null,
            ProviderClientFamily.Chat,
            ProviderPayloadKind.Configuration,
            new ProviderClientConfig(),
            "Clients.Chat.ProviderConfig"));
        var mismatch = Assert.Throws<AgentRunConfigurationException>(() => composition.ValidatePayload(
            "test",
            ProviderClientFamily.Chat,
            ProviderPayloadKind.Configuration,
            new object(),
            "Clients.Chat.ProviderConfig"));

        Assert.Equal("ProviderKeyRequired", missing.Code);
        Assert.Equal("ProviderConfigTypeMismatch", mismatch.Code);
        Assert.Equal(typeof(ProviderClientConfig), mismatch.ExpectedType);
        Assert.Equal(typeof(object), mismatch.ActualType);
    }

    private static ProviderBackendDescriptor Backend(
        string key,
        params ProviderClientFamily[] families) => new()
    {
        BackendKey = key,
        IsDefault = true,
        Families = families.ToDictionary(
            static family => family,
            static family => new ProviderFamilyDescriptor { Family = family }),
        Authentication =
        [
            new ProviderAuthenticationDescriptor
            {
                Kind = ProviderAuthenticationKind.ApiKey,
                IsDefault = true,
                DefaultSecretKey = "openai:ApiKey",
                SupportedFamilies = families.ToHashSet()
            }
        ]
    };

    private sealed class TestDescriptor : IProviderDescriptor
    {
        public TestDescriptor() : this("test", ProviderClientFamily.Chat, []) { }
        public TestDescriptor(
            string key,
            ProviderClientFamily family,
            IReadOnlyList<string> aliases,
            ProviderFamilyLifetime lifetime = ProviderFamilyLifetime.ReusableClient,
            ProviderBackendDescriptor? backend = null)
        {
            ProviderKey = key;
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [family] = new() { Family = family, Lifetime = lifetime }
            };
            Aliases = aliases;
            Backends = backend is null
                ? new Dictionary<string, ProviderBackendDescriptor>()
                : new Dictionary<string, ProviderBackendDescriptor> { [backend.BackendKey] = backend };
        }
        public string ProviderKey { get; }
        public string DisplayName => "Test";
        public Uri? DocumentationUri => null;
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();
        public IReadOnlyDictionary<string, ProviderBackendDescriptor> Backends { get; }
        public IReadOnlyList<string> Aliases { get; }
    }
}
