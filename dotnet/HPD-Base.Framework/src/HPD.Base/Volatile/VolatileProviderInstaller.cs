using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class VolatileProviderInstaller(
    Action<HPDBaseVolatileStoreOptions>? configure) : IHPDBaseBuilderExtension
{
    /// <summary>Gets the ID.</summary>
    public string Id => "volatile";
    /// <summary>Gets the is record provider.</summary>
    public bool IsRecordProvider => true;
    /// <summary>Gets the supports required indexes.</summary>
    public bool SupportsRequiredIndexes => false;

    /// <summary>Executes the configure operation.</summary>
    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
    {
        services.AddHPDBaseVolatileStore(options =>
        {
            configure?.Invoke(options);
            options.CollectionIds = collections.Select(static item => item.Id).ToArray();
            options.Collections = collections.ToArray();
        });
    }

    /// <summary>Executes the initialize async operation.</summary>
    public ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseVolatileStore(services);
        return ValueTask.CompletedTask;
    }
}
