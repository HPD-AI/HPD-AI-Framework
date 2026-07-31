using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class VolatileProviderInstaller(
    Action<HPDBaseVolatileStoreOptions>? configure) : IHPDBaseBuilderExtension
{
    public string Id => "volatile";
    public bool IsRecordProvider => true;
    public bool SupportsRequiredIndexes => false;

    public void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections)
    {
        services.AddHPDBaseVolatileStore(options =>
        {
            configure?.Invoke(options);
            options.CollectionIds = collections.Select(static item => item.Id).ToArray();
            options.Collections = collections.ToArray();
        });
    }

    public void Initialize(IServiceProvider services) =>
        services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseVolatileStore(services);
}
