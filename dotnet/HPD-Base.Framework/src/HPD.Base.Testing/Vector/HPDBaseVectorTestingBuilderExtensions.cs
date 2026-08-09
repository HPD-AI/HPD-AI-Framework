using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Testing;

/// <summary>Installs the deterministic exact vector provider for tests.</summary>
public static class HPDBaseVectorTestingBuilderExtensions
{
    /// <summary>Uses the explicit deterministic vector test provider.</summary>
    public static HPDBaseBuilder UseTestVectorProvider(this HPDBaseBuilder builder, Action<BaseTestVectorProviderOptions>? configure = null)
    { ArgumentNullException.ThrowIfNull(builder); return builder.Use(new Installer(configure)); }

    private sealed class Installer(Action<BaseTestVectorProviderOptions>? configure) : IHPDBaseBuilderExtension
    {
        public string Id => "vector.testing";
        public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
        {
            var options = new BaseTestVectorProviderOptions();
            configure?.Invoke(options);
            if (options.SearchDelay < TimeSpan.Zero || options.AdministrationDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.SearchDelay));
            services.AddSingleton<BaseExplicitVectorProviderRegistration>();
            services.AddSingleton(new BaseTestVectorProviderSnapshot(options.Consistency, options.SearchDelay, options.IgnoreSearchCancellation, options.AdministrationDelay, options.IgnoreAdministrationCancellation));
            services.AddSingleton<BaseTestVectorStore>(); services.AddSingleton<BaseTestVectorProvider>(); services.AddSingleton<IBaseVectorProvider>(static provider => provider.GetRequiredService<BaseTestVectorProvider>()); services.AddSingleton<IBaseVectorAuthority>(static provider => provider.GetRequiredService<BaseTestVectorProvider>()); services.AddSingleton<IBaseVectorAdministrationProvider>(static provider => provider.GetRequiredService<BaseTestVectorProvider>());
        }
        public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    }
}

/// <summary>Configures the explicit vector provider fixture used by tests.</summary>
public sealed class BaseTestVectorProviderOptions
{
    /// <summary>Gets or sets whether the fixture behaves as transactional or journal-derived.</summary>
    public BaseVectorProviderConsistency Consistency { get; set; } = BaseVectorProviderConsistency.TransactionalCurrent;
    /// <summary>Gets or sets a deterministic artificial search delay.</summary>
    public TimeSpan SearchDelay { get; set; }
    /// <summary>Gets or sets whether the artificial delay deliberately ignores cancellation.</summary>
    public bool IgnoreSearchCancellation { get; set; }
    /// <summary>Gets or sets a deterministic artificial administration-inspection delay.</summary>
    public TimeSpan AdministrationDelay { get; set; }
    /// <summary>Gets or sets whether administration inspection deliberately ignores cancellation.</summary>
    public bool IgnoreAdministrationCancellation { get; set; }
}

internal sealed record BaseTestVectorProviderSnapshot(BaseVectorProviderConsistency Consistency, TimeSpan SearchDelay, bool IgnoreSearchCancellation, TimeSpan AdministrationDelay, bool IgnoreAdministrationCancellation);
