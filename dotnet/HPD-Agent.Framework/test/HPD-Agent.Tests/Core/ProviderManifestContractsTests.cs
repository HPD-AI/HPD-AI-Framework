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
        factories.Add(new ProviderRuntimeFactoryRegistration("test", Array.Empty<ProviderClientFamily>(), static () => throw new NotSupportedException()));

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
            new([chat], [new("test", [ProviderClientFamily.Chat], static () => throw new NotSupportedException())], [], []),
            new([embeddings], [new("test", [ProviderClientFamily.Embeddings], static () => throw new NotSupportedException())], [], [])]);

        Assert.Equal("test", composition.Descriptors.Canonicalize("LEGACY-TEST"));
        Assert.True(composition.Descriptors.TryGet("test", out var descriptor));
        Assert.Equal(2, descriptor!.Families.Count);
        Assert.NotNull(composition.Runtime.GetFactory("legacy-test", ProviderClientFamily.Chat));
    }

    [Fact]
    public void Composition_RejectsDuplicateFamily()
    {
        var exception = Assert.Throws<ProviderCompositionException>(() => ProviderComposition.Create([
            new([new TestDescriptor("test", ProviderClientFamily.Chat, [])], [], [], []),
            new([new TestDescriptor("test", ProviderClientFamily.Chat, [])], [], [], [])]));
        Assert.Equal("HPDP010", exception.Code);
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

    private sealed class TestDescriptor : IProviderDescriptor
    {
        public TestDescriptor() : this("test", ProviderClientFamily.Chat, []) { }
        public TestDescriptor(string key, ProviderClientFamily family, IReadOnlyList<string> aliases)
        {
            ProviderKey = key;
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [family] = new() { Family = family }
            };
            Aliases = aliases;
        }
        public string ProviderKey { get; }
        public string DisplayName => "Test";
        public Uri? DocumentationUri => null;
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();
        public IReadOnlyList<string> Aliases { get; }
    }
}
