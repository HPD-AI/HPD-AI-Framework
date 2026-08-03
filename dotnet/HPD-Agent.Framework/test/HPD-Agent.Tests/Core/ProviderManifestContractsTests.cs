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
        factories.Add(new ProviderRuntimeFactoryRegistration("test", static () => throw new NotSupportedException()));

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

    private sealed class TestDescriptor : IProviderDescriptor
    {
        public string ProviderKey => "test";
        public string DisplayName => "Test";
        public Uri? DocumentationUri => null;
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; } =
            new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();
        public IReadOnlyList<string> Aliases => Array.Empty<string>();
    }
}
