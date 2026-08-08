using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Vector.Testing;

/// <summary>Installs the deterministic exact vector provider for tests.</summary>
public static class HPDBaseVectorTestingBuilderExtensions
{
    /// <summary>Uses the explicit deterministic vector test provider.</summary>
    public static HPDBaseBuilder UseTestVectorProvider(this HPDBaseBuilder builder)
    { ArgumentNullException.ThrowIfNull(builder); return builder.Use(new Installer()); }

    private sealed class Installer : IHPDBaseBuilderExtension
    {
        public string Id => "vector.testing";
        public bool IsRecordProvider => false;
        public bool SupportsRequiredIndexes => false;
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        { services.AddSingleton<BaseTestVectorStore>(); services.AddSingleton<BaseTestVectorProvider>(); services.AddSingleton<IBaseVectorProvider>(static provider => provider.GetRequiredService<BaseTestVectorProvider>()); services.AddSingleton<IBaseVectorAuthority>(static provider => provider.GetRequiredService<BaseTestVectorProvider>()); }
        public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    }
}
