using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderManifestContractsTests
{
    [Fact]
    public void Fragment_DefensivelyCopiesContributionCollections()
    {
        var descriptors = new List<IProviderDescriptor>();
        var factories = new List<ProviderRuntimeFactoryRegistration>();
        var fragment = new ProviderManifestFragment(descriptors, factories);

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
            new ProviderManifestFragment(null!, Array.Empty<ProviderRuntimeFactoryRegistration>()));
        Assert.Throws<ArgumentNullException>(() =>
            new ProviderManifestFragment(Array.Empty<IProviderDescriptor>(), null!));
    }

    [Fact]
    public void Composition_MergesFamiliesAndCanonicalizesAliases()
    {
        var chat = new TestDescriptor("test", ProviderClientFamily.Chat, ["legacy-test"]);
        var embeddings = new TestDescriptor("test", ProviderClientFamily.Embeddings, []);
        var composition = ProviderComposition.Create([
            new([chat], [new("test", [ProviderClientFamily.Chat], static () => throw new NotSupportedException())]),
            new([embeddings], [new("test", [ProviderClientFamily.Embeddings], static () => throw new NotSupportedException())])]);

        Assert.Equal("test", composition.Descriptors.Canonicalize("LEGACY-TEST"));
        Assert.True(composition.Descriptors.TryGet("test", out var descriptor));
        Assert.Equal(2, descriptor!.Families.Count);
        Assert.NotNull(composition.Runtime.GetFactory("legacy-test", ProviderClientFamily.Chat));
    }

    [Fact]
    public void Composition_RejectsDuplicateFamily()
    {
        var exception = Assert.Throws<ProviderCompositionException>(() => ProviderComposition.Create([
            new([new TestDescriptor("test", ProviderClientFamily.Chat, [])], []),
            new([new TestDescriptor("test", ProviderClientFamily.Chat, [])], [])]));
        Assert.Equal("HPDP010", exception.Code);
    }

    [Fact]
    public void Composition_RejectsAliasCollision()
    {
        var exception = Assert.Throws<ProviderCompositionException>(() => ProviderComposition.Create([
            new([new TestDescriptor("one", ProviderClientFamily.Chat, ["two"])], []),
            new([new TestDescriptor("two", ProviderClientFamily.Chat, [])], [])]));
        Assert.Equal("HPDP011", exception.Code);
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
