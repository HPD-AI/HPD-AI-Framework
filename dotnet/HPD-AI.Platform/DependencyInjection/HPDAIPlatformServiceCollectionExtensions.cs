using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.AI.Platform;

public static class HPDAIPlatformServiceCollectionExtensions
{
    public static HPDAIPlatformBuilder AddHPDAIPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<HPDAIPlatformOptions>();
        return new HPDAIPlatformBuilder(services);
    }
}

public sealed class HPDAIPlatformBuilder
{
    public HPDAIPlatformBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }

    public HPDAIPlatformBuilder AddModule(
        string id,
        string label,
        string title,
        string status,
        params string[] capabilities)
    {
        Services.Configure<HPDAIPlatformOptions>(options =>
        {
            options.AddModule(new HPDAIPlatformModuleOptions(id, label, title, status));
            foreach (var capability in capabilities)
            {
                options.AddCapability(capability);
            }
        });

        return this;
    }
}

public sealed class HPDAIPlatformOptions
{
    public IList<string> Capabilities { get; } = [];

    public IList<HPDAIPlatformModuleOptions> Modules { get; } = [];

    public void AddCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability))
            return;

        if (!Capabilities.Contains(capability, StringComparer.Ordinal))
            Capabilities.Add(capability);
    }

    public void AddModule(HPDAIPlatformModuleOptions module)
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
