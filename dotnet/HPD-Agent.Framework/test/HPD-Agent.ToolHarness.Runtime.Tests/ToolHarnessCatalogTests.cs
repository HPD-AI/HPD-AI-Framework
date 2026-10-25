using HPD.Agent;

namespace HPD.Agent.Tests;

public sealed class ToolHarnessCatalogTests
{
    private static ToolHarnessFactory Factory<T>(string? identity = null) where T : new() => new(
        Name: typeof(T).Name,
        ToolHarnessType: typeof(T),
        CreateInstance: static () => new T()!,
        CreateFunctions: static (_, _, _) => [],
        GetReferencedToolHarnesses: static () => [],
        GetReferencedFunctions: static () => [],
        StableIdentity: identity ?? $"catalog-test:{typeof(T).FullName}");

    [Fact]
    public void ExactTypesAndIdentitiesSurviveShortNameCollision()
    {
        var first = Factory<First.CatalogCollisionHarness>();
        var second = Factory<Second.CatalogCollisionHarness>();
        var catalog = new ToolHarnessCatalog();
        catalog.Register(first);
        catalog.Register(second);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGetByType(typeof(First.CatalogCollisionHarness), out var exact));
        Assert.Same(first, exact);
        Assert.Same(second, catalog[second.ActivationIdentity]);
        var error = Assert.Throws<InvalidOperationException>(() => catalog[first.Name]);
        Assert.Contains(first.ActivationIdentity, error.Message);
        Assert.Contains(second.ActivationIdentity, error.Message);
    }

    [Fact]
    public void UniqueAliasIsCaseInsensitiveButIdentityIsExact()
    {
        var factory = Factory<First.CatalogCollisionHarness>();
        var catalog = new ToolHarnessCatalog();
        catalog.Register(factory);
        Assert.Same(factory, catalog[factory.Name.ToLowerInvariant()]);
        Assert.Same(factory, catalog[factory.ActivationIdentity]);
        Assert.False(catalog.TryGetValue(factory.ActivationIdentity.ToUpperInvariant(), out _));
    }

    [Fact]
    public void RepeatedSnapshotsAreIdempotentAndConflictsFail()
    {
        var factory = Factory<First.CatalogCollisionHarness>();
        var catalog = new ToolHarnessCatalog();
        catalog.Register(factory);
        catalog.Register(factory);
        Assert.Single(catalog.Values);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.Register(Factory<Second.CatalogCollisionHarness>(factory.ActivationIdentity)));
        Assert.Throws<InvalidOperationException>(() =>
            catalog.Register(factory with { StableIdentity = "different-identity" }));
        Assert.Single(catalog.Values);
    }

    [Fact]
    public void TypedBuilderRegistrationDoesNotSelectTheFirstAlias()
    {
        AgentGeneratedRegistry.Register([FirstFactory, SecondFactory]);
        var builder = new AgentBuilder();
        builder.WithToolHarness<Second.CatalogCollisionHarness>();
        Assert.Same(SecondFactory, Assert.Single(builder._selectedToolHarnessFactories));
    }

    private static readonly ToolHarnessFactory FirstFactory = Factory<First.CatalogCollisionHarness>();
    private static readonly ToolHarnessFactory SecondFactory = Factory<Second.CatalogCollisionHarness>();

    public static class First { public sealed class CatalogCollisionHarness { } }
    public static class Second { public sealed class CatalogCollisionHarness { } }
}
