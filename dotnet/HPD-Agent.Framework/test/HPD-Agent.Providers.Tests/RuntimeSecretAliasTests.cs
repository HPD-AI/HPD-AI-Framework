using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Tests;

/// <summary>
/// Verifies the runtime <see cref="IProviderSecretAliasProvider"/> surface stays consistent
/// with the source-generated provider composition, and that the merge/fallback semantics
/// of <see cref="CompositeProviderSecretAliasRegistry"/> behave as intended.
/// </summary>
public sealed class RuntimeSecretAliasTests
{
    private static ProviderComposition CreateComposition()
    {
        var services = new ServiceCollection();
        HpdGeneratedProviderServiceCollectionExtensions.AddHpdGeneratedProviders(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ProviderComposition>();
    }

    /// <summary>
    /// Every provider registered in the generated composition must expose the runtime
    /// secret-alias surface.
    /// </summary>
    [Fact]
    public void EveryRegisteredProvider_ExposesRuntimeSecretAliasSurface()
    {
        var composition = CreateComposition();

        Assert.NotEmpty(composition.Runtime.Registrations);

        foreach (var registration in composition.Runtime.Registrations)
            Assert.IsAssignableFrom<IProviderSecretAliasProvider>(registration.Factory());
    }

    /// <summary>
    /// THE drift guard: for every provider, the runtime <see cref="IProviderSecretAliasProvider.SecretAliases"/>
    /// must exactly match the generated composition's secret-alias registry. If the manifest
    /// attribute and the runtime surface diverge, this test fails.
    /// </summary>
    [Fact]
    public void EveryRegisteredProvider_RuntimeSecretAliasesMatchGeneratedComposition()
    {
        var composition = CreateComposition();

        foreach (var registration in composition.Runtime.Registrations)
        {
            var provider = registration.Factory();
            var aliasProvider = Assert.IsAssignableFrom<IProviderSecretAliasProvider>(provider);

            Assert.NotNull(aliasProvider.SecretAliases);
            foreach (var alias in aliasProvider.SecretAliases)
            {
                Assert.False(string.IsNullOrWhiteSpace(alias.SecretKey),
                    $"{provider.ProviderKey} exposes an alias with a blank secret key.");
                Assert.NotEmpty(alias.EnvironmentVariables);

                var generated = composition.SecretAliases.GetEnvironmentVariables(alias.SecretKey);
                Assert.NotNull(generated);
                Assert.Equal(alias.EnvironmentVariables, generated);
            }
        }
    }

    /// <summary>Generated composition aliases win over runtime aliases for the same key.</summary>
    [Fact]
    public void CompositeRegistry_GeneratedWinsAndMissingReturnsNull()
    {
        var generated = new StaticAliasRegistry(("test:ApiKey", ["GENERATED_API_KEY"]));
        var runtime = new StaticAliasRegistry(("test:ApiKey", ["RUNTIME_API_KEY"]));

        var composite = new CompositeProviderSecretAliasRegistry(generated, runtime);

        Assert.Equal(["GENERATED_API_KEY"], composite.GetEnvironmentVariables("test:ApiKey"));
        Assert.Null(composite.GetEnvironmentVariables("missing:ApiKey"));
    }

    /// <summary>When no generated source exists, the runtime surface alone resolves.</summary>
    [Fact]
    public void CompositeRegistry_RuntimeFallbackWhenGeneratedAbsent()
    {
        var runtime = new StaticAliasRegistry(("deepseek:ApiKey", ["DEEPSEEK_API_KEY"]));

        var composite = new CompositeProviderSecretAliasRegistry(runtime);

        Assert.Equal(["DEEPSEEK_API_KEY"], composite.GetEnvironmentVariables("deepseek:ApiKey"));
    }

    private sealed class StaticAliasRegistry : IProviderSecretAliasRegistry
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _aliases;

        public StaticAliasRegistry(params (string Key, string[] Vars)[] aliases)
            => _aliases = aliases.ToDictionary(
                a => a.Key,
                a => (IReadOnlyList<string>)a.Vars,
                StringComparer.Ordinal);

        public IReadOnlyList<string>? GetEnvironmentVariables(string secretKey) =>
            _aliases.TryGetValue(secretKey, out var vars) ? vars : null;
    }
}
