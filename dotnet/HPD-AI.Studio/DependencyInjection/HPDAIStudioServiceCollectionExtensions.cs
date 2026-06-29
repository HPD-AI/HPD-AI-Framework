using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.AI.Studio;

public static class HPDAIStudioServiceCollectionExtensions
{
    public static HPDAIStudioBuilder AddHPDAIStudio(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<HPDAIStudioOptions>();
        return new HPDAIStudioBuilder(services);
    }
}

public sealed class HPDAIStudioBuilder
{
    public HPDAIStudioBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }

    public HPDAIStudioBuilder AddModule(
        string id,
        string label,
        string title,
        string status,
        params string[] capabilities)
    {
        Services.Configure<HPDAIStudioOptions>(options =>
        {
            options.AddModule(new HPDAIStudioModuleOptions(id, label, title, status));
            foreach (var capability in capabilities)
            {
                options.AddCapability(capability);
            }
        });

        return this;
    }
}

public sealed class HPDAIStudioOptions
{
    public IList<string> Capabilities { get; } = [];

    public IList<HPDAIStudioModuleOptions> Modules { get; } = [];

    public void AddCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return;

        if (!Capabilities.Contains(capability, StringComparer.Ordinal))
            Capabilities.Add(capability);
    }

    public void AddModule(HPDAIStudioModuleOptions module)
    {
        ArgumentNullException.ThrowIfNull(module);

        for (var index = 0; index < Modules.Count; index++)
        {
            if (string.Equals(Modules[index].Id, module.Id, StringComparison.Ordinal))
            {
                Modules[index] = module;
                return;
            }
        }

        Modules.Add(module);
    }
}
